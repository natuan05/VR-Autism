using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Cloud.Models;
using VRAutism.Gameplay.LessonGraphV2.Remote;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    public interface ILessonTelemetrySinkV2
    {
        Task WriteStateAsync(LessonStateV2 state, CancellationToken token);
        Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token);
    }

    /// <summary>
    /// Serializes one session's V2 telemetry writes. Queued states are coalesced to the newest
    /// revision while audit and compatibility records remain keyed by their stable identities.
    /// </summary>
    public sealed class LessonTelemetryWriterV2 : IDisposable
    {
        private static readonly object SessionWriterGate = new object();
        private static readonly Dictionary<string, LessonTelemetryWriterV2> SessionWriters =
            new Dictionary<string, LessonTelemetryWriterV2>(StringComparer.Ordinal);

        private readonly object _gate = new object();
        private readonly LinkedList<TelemetryWriteBatchV2> _pending = new LinkedList<TelemetryWriteBatchV2>();
        private readonly ILessonTelemetrySinkV2 _sink;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private TelemetryWriteBatchV2 _inFlight;
        private Task _pumpTask;
        private TaskCompletionSource<bool> _settled;
        private bool _pumpRunning;
        private bool _disposed;
        private string _ownerRunId;
        private string _lastError;

        public LessonTelemetryWriterV2(ILessonTelemetrySinkV2 sink,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        }

        public int PendingCount
        {
            get
            {
                lock (_gate) return _pending.Count + (_inFlight == null ? 0 : 1);
            }
        }

        public string LastError
        {
            get { lock (_gate) return _lastError; }
        }

        /// <summary>Registers the single writer allowed to own a Firebase session projection.</summary>
        public static bool TryRegisterSessionWriter(string sessionId, LessonTelemetryWriterV2 writer)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            lock (SessionWriterGate)
            {
                if (SessionWriters.ContainsKey(sessionId)) return false;
                SessionWriters.Add(sessionId, writer);
                return true;
            }
        }

        public static void ReleaseSessionWriter(string sessionId, LessonTelemetryWriterV2 writer)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || writer == null) return;
            lock (SessionWriterGate)
            {
                if (SessionWriters.TryGetValue(sessionId, out LessonTelemetryWriterV2 current) && ReferenceEquals(current, writer))
                    SessionWriters.Remove(sessionId);
            }
        }

        public void Enqueue(TelemetryWriteBatchV2 batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            TelemetryWriteBatchV2 frozen = CopyBatch(batch);
            if (IsEmpty(frozen)) return;

            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LessonTelemetryWriterV2));
                EnsureRunOwnership(frozen);
                if (_pending.Last == null)
                    _pending.AddLast(frozen);
                else
                    MergeInto(_pending.Last.Value, frozen);
                EnsurePumpLocked();
            }
        }

        /// <summary>
        /// Waits for the current queue to drain. Cancelling this caller's wait leaves the writer
        /// and its retry loop active; unapplied batches remain available through PendingCount/LastError.
        /// </summary>
        public Task FlushAsync(CancellationToken token)
        {
            Task waitTask;
            lock (_gate)
            {
                if (_pending.Count == 0 && _inFlight == null) return Task.CompletedTask;
                if (_lifetime.IsCancellationRequested) return Task.CompletedTask;
                if (_settled == null || _settled.Task.IsCompleted)
                    _settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _settled.Task;
                EnsurePumpLocked();
            }

            if (!token.CanBeCanceled) return waitTask;
            return WaitWithCancellationAsync(waitTask, token);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _lastError = _lastError ?? "Telemetry writer disposed with pending writes.";
                _lifetime.Cancel();
                if (!_pumpRunning) SignalSettledLocked();
            }
        }

        private static async Task WaitWithCancellationAsync(Task waitTask, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(waitTask, cancelled.Task);
                if (!ReferenceEquals(completed, waitTask))
                    throw new OperationCanceledException(token);
            }

            await waitTask;
        }

        private void EnsurePumpLocked()
        {
            if (_pumpRunning || _lifetime.IsCancellationRequested || _pending.Count == 0) return;
            _pumpRunning = true;
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pumpTask = PumpAfterStartAsync(start.Task);
            start.TrySetResult(true);
        }

        private async Task PumpAfterStartAsync(Task start)
        {
            try
            {
                await start;
                await PumpAsync();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _lastError = exception.ToString();
                    RestoreInFlightLocked();
                }
            }
            finally
            {
                lock (_gate)
                {
                    _pumpRunning = false;
                    SignalSettledLocked();
                    if (!_lifetime.IsCancellationRequested && _pending.Count > 0)
                        EnsurePumpLocked();
                }
            }
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                TelemetryWriteBatchV2 work;
                lock (_gate)
                {
                    if (_lifetime.IsCancellationRequested) return;
                    if (_inFlight == null)
                    {
                        if (_pending.Count == 0) return;
                        _inFlight = _pending.First.Value;
                        _pending.RemoveFirst();
                    }
                    work = CopyBatch(_inFlight);
                }

                int retry = 0;
                while (true)
                {
                    try
                    {
                        _lifetime.Token.ThrowIfCancellationRequested();
                        if (work.state_projection != null)
                            await _sink.WriteStateAsync(CopyState(work.state_projection), _lifetime.Token);
                        if (HasFirestoreWrites(work))
                            await _sink.UpsertBatchAsync(CopyBatch(work), _lifetime.Token);

                        lock (_gate)
                        {
                            _inFlight = null;
                            _lastError = null;
                            if (_pending.Count == 0) SignalSettledLocked();
                        }
                        break;
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                    {
                        lock (_gate)
                        {
                            _lastError = _lastError ?? "Telemetry write cancelled with the current batch pending.";
                            RestoreInFlightLocked();
                        }
                        return;
                    }
                    catch (Exception exception)
                    {
                        lock (_gate) _lastError = exception.ToString();
                        TimeSpan delay = RetryDelay(retry++);
                        try
                        {
                            await _delay(delay, _lifetime.Token);
                        }
                        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                        {
                            lock (_gate)
                            {
                                _lastError = _lastError ?? "Telemetry retry delay cancelled with the current batch pending.";
                                RestoreInFlightLocked();
                            }
                            return;
                        }
                        catch (Exception delayException)
                        {
                            lock (_gate) _lastError = delayException.ToString();
                        }
                    }
                }
            }
        }

        private void EnsureRunOwnership(TelemetryWriteBatchV2 batch)
        {
            string runId = batch.state_projection?.run_id;
            if (string.IsNullOrWhiteSpace(runId))
                runId = batch.audit_events_by_id.Values.Select(item => item.run_id).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(runId))
                runId = batch.node_logs_by_id.Values.Select(item => item.run_id).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(runId)) return;
            if (_ownerRunId == null) _ownerRunId = runId;
            else if (!string.Equals(_ownerRunId, runId, StringComparison.Ordinal))
                throw new InvalidOperationException("A session telemetry writer cannot accept events from multiple lesson runs.");
        }

        private void RestoreInFlightLocked()
        {
            if (_inFlight == null) return;
            _pending.AddFirst(_inFlight);
            _inFlight = null;
        }

        private void SignalSettledLocked()
        {
            if (_settled != null) _settled.TrySetResult(true);
        }

        private static TimeSpan RetryDelay(int retry)
        {
            double seconds = retry >= 5 ? 30d : Math.Min(30d, Math.Pow(2d, Math.Max(0, retry)));
            return TimeSpan.FromSeconds(seconds);
        }

        private static bool IsEmpty(TelemetryWriteBatchV2 batch) =>
            batch.state_projection == null && batch.audit_events_by_id.Count == 0 &&
            batch.node_logs_by_id.Count == 0 && batch.quest_logs_by_id.Count == 0;

        private static bool HasFirestoreWrites(TelemetryWriteBatchV2 batch) =>
            batch.audit_events_by_id.Count > 0 || batch.node_logs_by_id.Count > 0 || batch.quest_logs_by_id.Count > 0;

        private static void MergeInto(TelemetryWriteBatchV2 destination, TelemetryWriteBatchV2 source)
        {
            if (source.state_projection != null &&
                (destination.state_projection == null ||
                 source.state_projection.state_revision > destination.state_projection.state_revision))
                destination.state_projection = CopyState(source.state_projection);

            foreach (KeyValuePair<string, LessonAuditEventDataV2> item in source.audit_events_by_id)
                if (!destination.audit_events_by_id.ContainsKey(item.Key))
                    destination.audit_events_by_id.Add(item.Key, CopyAudit(item.Value));
            foreach (KeyValuePair<string, NodeLogData> item in source.node_logs_by_id)
                destination.node_logs_by_id[item.Key] = CopyNodeLog(item.Value);
            foreach (KeyValuePair<string, QuestLogData> item in source.quest_logs_by_id)
                destination.quest_logs_by_id[item.Key] = CopyQuestLog(item.Value);
        }

        private static TelemetryWriteBatchV2 CopyBatch(TelemetryWriteBatchV2 source)
        {
            var copy = new TelemetryWriteBatchV2
            {
                state_projection = CopyState(source.state_projection)
            };
            foreach (KeyValuePair<string, LessonAuditEventDataV2> item in source.audit_events_by_id)
                copy.audit_events_by_id[item.Key] = CopyAudit(item.Value);
            foreach (KeyValuePair<string, NodeLogData> item in source.node_logs_by_id)
                copy.node_logs_by_id[item.Key] = CopyNodeLog(item.Value);
            foreach (KeyValuePair<string, QuestLogData> item in source.quest_logs_by_id)
                copy.quest_logs_by_id[item.Key] = CopyQuestLog(item.Value);
            return copy;
        }

        internal static LessonStateV2 CopyState(LessonStateV2 source)
        {
            if (source == null) return null;
            var copy = new LessonStateV2
            {
                contract_version = source.contract_version,
                session_id = source.session_id,
                run_id = source.run_id,
                graph_id = source.graph_id,
                lesson_id = source.lesson_id,
                launch_token = source.launch_token,
                lesson_voice_revision = source.lesson_voice_revision,
                child_phrase_revision = source.child_phrase_revision,
                node_id = source.node_id,
                node_type = source.node_type,
                node_index = source.node_index,
                activation_id = source.activation_id,
                status = source.status,
                checkpoint_id = source.checkpoint_id,
                updated_at_utc = source.updated_at_utc,
                state_revision = source.state_revision,
                active_node_ids = source.active_node_ids == null ? null : (string[])source.active_node_ids.Clone(),
                parallel_group_id = source.parallel_group_id
            };
            if (source.bindings != null)
            {
                copy.bindings = new LessonBindingV2[source.bindings.Length];
                for (int i = 0; i < source.bindings.Length; i++)
                {
                    LessonBindingV2 binding = source.bindings[i];
                    copy.bindings[i] = binding == null ? null : new LessonBindingV2
                    {
                        binding_id = binding.binding_id,
                        npc_binding_id = binding.npc_binding_id,
                        can_verbal_hint = binding.can_verbal_hint,
                        can_visual_hint = binding.can_visual_hint
                    };
                }
            }
            return copy;
        }

        private static LessonAuditEventDataV2 CopyAudit(LessonAuditEventDataV2 source) => new LessonAuditEventDataV2
        {
            event_id = source.event_id,
            event_type = source.event_type,
            session_id = source.session_id,
            run_id = source.run_id,
            graph_id = source.graph_id,
            lesson_id = source.lesson_id,
            node_id = source.node_id,
            node_type = source.node_type,
            node_index = source.node_index,
            activation_id = source.activation_id,
            occurred_at_utc = source.occurred_at_utc,
            elapsed_seconds = source.elapsed_seconds,
            status = source.status,
            command_id = source.command_id,
            command = source.command,
            binding_id = source.binding_id,
            reason = source.reason,
            launch_token = source.launch_token,
            lesson_voice_revision = source.lesson_voice_revision,
            child_phrase_revision = source.child_phrase_revision
        };

        private static NodeLogData CopyNodeLog(NodeLogData source) => new NodeLogData
        {
            event_id = source.event_id,
            session_id = source.session_id,
            run_id = source.run_id,
            graph_id = source.graph_id,
            lesson_id = source.lesson_id,
            launch_token = source.launch_token,
            lesson_voice_revision = source.lesson_voice_revision,
            child_phrase_revision = source.child_phrase_revision,
            node_id = source.node_id,
            node_type = source.node_type,
            node_name = source.node_name,
            node_index = source.node_index,
            activation_id = source.activation_id,
            entered_at_utc = source.entered_at_utc,
            exited_at_utc = source.exited_at_utc,
            duration_seconds = source.duration_seconds,
            elapsed_seconds = source.elapsed_seconds,
            status = source.status,
            completion_channel = source.completion_channel
        };

        private static QuestLogData CopyQuestLog(QuestLogData source) => new QuestLogData
        {
            index = source.index,
            quest_name = source.quest_name,
            response_time = source.response_time,
            completion_status = source.completion_status,
            hints_verbal = source.hints_verbal,
            hints_visual = source.hints_visual,
            hints_physical = source.hints_physical,
            response_time_from_hint = source.response_time_from_hint
        };
    }
}
