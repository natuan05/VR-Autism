using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    /// <summary>
    /// Converts immutable runner notifications into telemetry observations. This adapter never
    /// calls a runner transition method; queued batches are owned by the session writer.
    /// </summary>
    public sealed class LessonTelemetryAdapterV2
    {
        private readonly LessonTelemetryWriterV2 _writer;
        private readonly LessonGraph _graph;
        private readonly IUtcClockV2 _utcClock;
        private readonly Func<double> _elapsedSeconds;
        private readonly LessonTelemetryReducerV2 _reducer = new LessonTelemetryReducerV2();
        private readonly Stopwatch _monotonicClock = new Stopwatch();
        private LessonGraphRunner _runner;
        private LessonSessionContextV2 _sessionContext;
        private LessonStateV2 _lastObservedState;
        private string _elapsedRunId;
        private bool _terminalCaptured;

        public LessonTelemetryAdapterV2(LessonTelemetryWriterV2 writer, LessonGraph graph,
            IUtcClockV2 utcClock = null, Func<double> elapsedSeconds = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _utcClock = utcClock ?? new SystemUtcClockV2();
            _elapsedSeconds = elapsedSeconds ?? (() => _monotonicClock.Elapsed.TotalSeconds);
        }

        public bool IsAttached => _runner != null;
        public string LastError { get; private set; }

        public void Attach(LessonGraphRunner runner, LessonSessionContextV2 context)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (ReferenceEquals(_runner, runner) && ReferenceEquals(_sessionContext, context)) return;
            Detach();

            _runner = runner;
            _sessionContext = context;
            _lastObservedState = LessonTelemetryWriterV2.CopyState(runner.CurrentState);
            _elapsedRunId = null;
            _monotonicClock.Reset();
            if (_lastObservedState != null) StartRunClock(_lastObservedState.run_id);
            _terminalCaptured = false;
            runner.NodeEntered += OnNodeEntered;
            runner.NodeCompleted += OnNodeCompleted;
            runner.NodeCancelled += OnNodeCancelled;
            runner.StateChanged += OnStateChanged;
            runner.CommandEvaluated += OnCommandEvaluated;
            runner.LessonCompleted += OnLessonCompleted;
        }

        public void Detach()
        {
            if (_runner == null) return;
            _runner.NodeEntered -= OnNodeEntered;
            _runner.NodeCompleted -= OnNodeCompleted;
            _runner.NodeCancelled -= OnNodeCancelled;
            _runner.StateChanged -= OnStateChanged;
            _runner.CommandEvaluated -= OnCommandEvaluated;
            _runner.LessonCompleted -= OnLessonCompleted;
            _runner = null;
            _sessionContext = null;
            _lastObservedState = null;
            _elapsedRunId = null;
            _monotonicClock.Reset();
        }

        /// <summary>
        /// Captures a terminal observation on scene teardown when the runner did not already
        /// publish completion. The runner snapshot is copied and changed locally only.
        /// </summary>
        public bool CaptureSceneUnload()
        {
            if (_sessionContext == null || _terminalCaptured) return false;
            // Unity destruction order can clear the runner's live state before this component's
            // teardown callback runs. Keep the last immutable notification as a safe fallback.
            LessonStateV2 current = (_runner == null ? null : LessonTelemetryWriterV2.CopyState(_runner.CurrentState)) ??
                                    LessonTelemetryWriterV2.CopyState(_lastObservedState);
            if (current == null) return false;

            LessonStateV2 terminal = LessonTelemetryWriterV2.CopyState(current);
            if (!IsTerminal(terminal.status))
            {
                terminal.status = "cancelled";
                terminal.state_revision = Math.Max(terminal.state_revision + 1, 1);
            }
            terminal.updated_at_utc = FormatUtc(_utcClock.UtcNow);
            terminal.active_node_ids = new string[0];
            var item = LessonLifecycleEventV2.LessonTerminated(
                Context(current.run_id), terminal.status, _utcClock.UtcNow, Elapsed(), terminal);
            Record(item);
            _terminalCaptured = true;
            return true;
        }

        private void OnNodeEntered(NodeEnteredEvent item)
        {
            if (item == null || _sessionContext == null) return;
            StartRunClock(item.RunId);
            LessonStateV2 state = CurrentStateFor(item.RunId, item.NodeId, item.ActivationId);
            LessonTelemetryNodeV2 node = CreateNode(item.NodeId, item.ActivationId, state);
            Record(LessonLifecycleEventV2.NodeEntered(Context(item.RunId), node,
                _utcClock.UtcNow, Elapsed(), state));
        }

        private void OnNodeCompleted(NodeCompletedEvent item)
        {
            if (item?.Result == null || _sessionContext == null) return;
            NodeResult result = item.Result;
            string runId = _runner?.CurrentState?.run_id ?? _lastObservedState?.run_id;
            StartRunClock(runId);
            LessonStateV2 state = CurrentStateFor(runId,
                result.NodeId, result.ActivationId);
            LessonTelemetryNodeV2 node = CreateNode(result.NodeId, result.ActivationId, state);
            string status = NodeStatusCondition.ToCondition(result.Status);
            Record(LessonLifecycleEventV2.NodeCompleted(Context(state?.run_id), node, status,
                result.CompletionChannel, _utcClock.UtcNow, Elapsed(), state));
        }

        private void OnNodeCancelled(NodeCancelledEventV2 item)
        {
            if (item == null || _sessionContext == null) return;
            LessonStateV2 current = CurrentStateFor(item.RunId, item.NodeId, item.ActivationId);
            LessonTelemetryNodeV2 node = CreateNode(item.NodeId, item.ActivationId, current);
            StartRunClock(item.RunId);
            // The matching StateChanged notification supplies the final paused/cancelled projection.
            Record(LessonLifecycleEventV2.NodeCancelled(Context(item.RunId), node, item.Reason,
                _utcClock.UtcNow, Elapsed(), null));
        }

        private void OnStateChanged(LessonStateV2 state)
        {
            if (state == null || _sessionContext == null) return;
            LessonStateV2 snapshot = LessonTelemetryWriterV2.CopyState(state);
            StartRunClock(snapshot.run_id);
            bool hasNewRevision = _lastObservedState == null || snapshot.state_revision > _lastObservedState.state_revision;
            if (string.Equals(snapshot.status, "pausing", StringComparison.OrdinalIgnoreCase))
            {
                // `pausing` is a real dashboard state, but not a separate audit lifecycle event.
                // Persist the cloned projection without inventing an event or touching the runner.
                RecordStateProjection(snapshot);
                _lastObservedState = snapshot;
                return;
            }

            string eventType = null;
            if (string.Equals(snapshot.status, "paused", StringComparison.OrdinalIgnoreCase) &&
                (_lastObservedState == null || !string.Equals(_lastObservedState.status, "paused", StringComparison.OrdinalIgnoreCase)))
                eventType = LessonTelemetryEventTypeV2.LessonPaused;
            else if (string.Equals(snapshot.status, "running", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(_lastObservedState?.status, "paused", StringComparison.OrdinalIgnoreCase))
                eventType = LessonTelemetryEventTypeV2.LessonResumed;

            if (eventType != null)
            {
                LessonTelemetryNodeV2 node = CreateNode(snapshot.node_id, snapshot.activation_id, snapshot);
                Record(LessonLifecycleEventV2.StateTransition(Context(snapshot.run_id), node, eventType,
                    snapshot.status, string.Empty, _utcClock.UtcNow, Elapsed(), snapshot));
            }
            else if (hasNewRevision)
            {
                // Running revisions carry executor readiness and the binding set used for hint routing.
                // Persist every newer projection even when the lifecycle status did not change.
                RecordStateProjection(snapshot);
            }
            _lastObservedState = snapshot;
        }

        private void OnCommandEvaluated(LessonCommandResultV2 result)
        {
            if (result == null || _sessionContext == null) return;
            LessonStateV2 state = result.state == null ? null : LessonTelemetryWriterV2.CopyState(result.state);
            LessonStateV2 currentState = (_runner == null ? null : _runner.CurrentState) ?? _lastObservedState;
            string runId = !string.IsNullOrWhiteSpace(state?.run_id) ? state.run_id :
                !string.IsNullOrWhiteSpace(currentState?.run_id) ? currentState.run_id : null;
            if (string.IsNullOrWhiteSpace(runId)) return;

            string nodeId = !string.IsNullOrWhiteSpace(result.node_id) ? result.node_id : state?.node_id;
            string activationId = !string.IsNullOrWhiteSpace(result.activation_id) ? result.activation_id : state?.activation_id;
            LessonStateV2 nodeState = state != null && string.Equals(state.node_id, nodeId, StringComparison.Ordinal)
                ? state
                : null;
            LessonTelemetryNodeV2 node = CreateNode(nodeId, activationId, nodeState);
            // The result run_id is the submitted target. Audit rejected commands under the
            // authoritative run that owns this session's writer, while retaining target node and
            // activation correlation from the result above.
            StartRunClock(runId);
            Record(LessonLifecycleEventV2.CommandDisposition(Context(runId), node,
                result.command_id, result.command, result.binding_id, result.accepted, result.reason,
                _utcClock.UtcNow, Elapsed(), state));
        }

        private void OnLessonCompleted(LessonCompletedEvent item)
        {
            if (item?.Result == null || _sessionContext == null || _terminalCaptured) return;
            LessonStateV2 state = _runner?.CurrentState;
            StartRunClock(item.Result.RunId);
            string status = IsTerminal(state?.status)
                ? state.status
                : item.Result.IsSuccess ? "completed" : "failed";
            if (state == null)
            {
                state = new LessonStateV2
                {
                    contract_version = LessonRemoteContractV2.ContractVersion,
                    session_id = _sessionContext.SessionId,
                    run_id = item.Result.RunId,
                    graph_id = _graph.name,
                    lesson_id = _sessionContext.LessonId,
                    launch_token = _sessionContext.LaunchToken,
                    lesson_voice_revision = _sessionContext.LessonVoiceRevision,
                    child_phrase_revision = _sessionContext.ChildPhraseRevision,
                    node_index = -1,
                    state_revision = 1
                };
            }
            LessonStateV2 terminal = LessonTelemetryWriterV2.CopyState(state);
            terminal.status = status;
            terminal.updated_at_utc = FormatUtc(_utcClock.UtcNow);
            var itemToRecord = LessonLifecycleEventV2.LessonTerminated(Context(item.Result.RunId),
                status, _utcClock.UtcNow, Math.Max(0d, item.Result.ElapsedSeconds), terminal);
            Record(itemToRecord);
            _lastObservedState = LessonTelemetryWriterV2.CopyState(terminal);
            _terminalCaptured = true;
        }

        private LessonStateV2 CurrentStateFor(string runId, string nodeId, string activationId)
        {
            LessonStateV2 current = _runner?.CurrentState;
            if (current != null && (string.IsNullOrEmpty(runId) || string.Equals(current.run_id, runId, StringComparison.Ordinal)))
                return current;
            return new LessonStateV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                session_id = _sessionContext.SessionId,
                run_id = runId ?? string.Empty,
                graph_id = _graph.name,
                lesson_id = _sessionContext.LessonId,
                launch_token = _sessionContext.LaunchToken,
                lesson_voice_revision = _sessionContext.LessonVoiceRevision,
                child_phrase_revision = _sessionContext.ChildPhraseRevision,
                node_id = nodeId ?? string.Empty,
                node_type = string.Empty,
                node_index = NodeIndex(nodeId),
                activation_id = activationId ?? string.Empty,
                status = "running",
                updated_at_utc = FormatUtc(_utcClock.UtcNow),
                state_revision = 0,
                active_node_ids = new[] { nodeId ?? string.Empty },
                bindings = new LessonBindingV2[0]
            };
        }

        private LessonTelemetryNodeV2 CreateNode(string nodeId, string activationId, LessonStateV2 state)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(activationId)) return null;
            LessonNodeData graphNode = _graph.Nodes.FirstOrDefault(node => node != null &&
                string.Equals(node.Id, nodeId, StringComparison.Ordinal));
            string nodeType = state?.node_type;
            if (string.IsNullOrWhiteSpace(nodeType) && graphNode != null) nodeType = graphNode.NodeType.ToString();
            int nodeIndex = state != null && state.node_index >= 0 ? state.node_index : NodeIndex(nodeId);
            return new LessonTelemetryNodeV2(nodeId, nodeType, string.Empty, Math.Max(0, nodeIndex), activationId);
        }

        private int NodeIndex(string nodeId)
        {
            for (int i = 0; i < _graph.Nodes.Count; i++)
                if (_graph.Nodes[i] != null && string.Equals(_graph.Nodes[i].Id, nodeId, StringComparison.Ordinal)) return i;
            return 0;
        }

        private LessonTelemetryContextV2 Context(string runId) => new LessonTelemetryContextV2(
            _sessionContext.SessionId,
            string.IsNullOrWhiteSpace(runId) ? "run-unavailable" : runId,
            _graph.name,
            _sessionContext.LessonId,
            _sessionContext.LaunchToken,
            _sessionContext.LessonVoiceRevision,
            _sessionContext.ChildPhraseRevision);

        private void RecordStateProjection(LessonStateV2 snapshot)
        {
            var stateOnly = new TelemetryWriteBatchV2 { state_projection = snapshot };
            try
            {
                _writer.Enqueue(stateOnly);
                LastError = null;
            }
            catch (Exception exception)
            {
                LastError = exception.ToString();
                Debug.LogError("[LessonGraphV2] V2 telemetry observation failed: " + exception);
            }
        }

        private void StartRunClock(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId) || string.Equals(_elapsedRunId, runId, StringComparison.Ordinal)) return;
            _elapsedRunId = runId;
            _monotonicClock.Restart();
        }

        private double Elapsed() => Math.Max(0d, _elapsedSeconds?.Invoke() ?? _monotonicClock.Elapsed.TotalSeconds);

        private void Record(LessonLifecycleEventV2 item)
        {
            try
            {
                TelemetryWriteBatchV2 batch = _reducer.Observe(item);
                if (batch.state_projection != null || batch.audit_events_by_id.Count > 0 ||
                    batch.node_logs_by_id.Count > 0 || batch.quest_logs_by_id.Count > 0)
                    _writer.Enqueue(batch);
                LastError = null;
            }
            catch (Exception exception)
            {
                LastError = exception.ToString();
                Debug.LogError("[LessonGraphV2] V2 telemetry observation failed: " + exception);
            }
        }

        private static bool IsTerminal(string status) =>
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

        private static string FormatUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    }
}
