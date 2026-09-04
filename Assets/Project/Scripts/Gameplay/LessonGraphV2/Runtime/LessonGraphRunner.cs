using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public sealed class LessonGraphRunner : MonoBehaviour
    {
        private readonly object _gate = new object();
        private LessonGraph _graph;
        private INodeExecutorRegistry _registry;
        private ILessonStartPreflight _preflight;
        private INodeClock _clock;
        private bool _usesDefaultClock;
        private ICheckpointTelemetry _checkpointTelemetry;
        private CancellationTokenSource _lessonCancellation;
        private CancellationTokenSource _skipCancellation;
        private CancellationTokenSource _timeoutCancellation;
        private Task<LessonResult> _activeTask;
        private string _activeRunId;
        private string _activeActivationId;

        public event Action<NodeEnteredEvent> NodeEntered;
        public event Action<NodeCompletedEvent> NodeCompleted;
        public event Action<LessonCompletedEvent> LessonCompleted;

        public void Configure(LessonGraph graph, INodeExecutorRegistry registry, ILessonStartPreflight preflight = null, INodeClock clock = null, ICheckpointTelemetry checkpointTelemetry = null)
        {
            _graph = graph;
            _registry = registry;
            _preflight = preflight;
            _clock = clock;
            _usesDefaultClock = clock == null;
            _checkpointTelemetry = checkpointTelemetry;
        }

        public Task<LessonResult> StartLesson() => StartLessonAsync();

        public Task<LessonResult> StartLessonAsync()
        {
            lock (_gate)
            {
                if (_activeTask != null && !_activeTask.IsCompleted)
                {
                    Debug.LogWarning($"[LessonGraphV2] StartLesson ignored — lesson already running", this);
                    return _activeTask;
                }
                if (!CanStart()) return Task.FromResult(LessonResult.Failed(Guid.NewGuid().ToString("N"), LessonFailureReason.InvalidGraph));

                if (_usesDefaultClock) _clock = new MonotonicClock();
                _lessonCancellation = new CancellationTokenSource();
                _activeRunId = Guid.NewGuid().ToString("N");
                _activeTask = RunAsync(_activeRunId, _lessonCancellation.Token);
                return _activeTask;
            }
        }

        public void RequestSkip() => CancelActive(_skipCancellation);
        public void RequestTimeout() => CancelActive(_timeoutCancellation);
        public void AbortLesson() => CancelActive(_lessonCancellation);

        private bool CanStart()
        {
            try
            {
                if (_graph == null)
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: graph is null (did Installer configure runner?)", this);
                    return false;
                }
                if (_registry == null)
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: registry is null", this);
                    return false;
                }
                var validation = LessonGraphValidator.Validate(_graph);
                if (!validation.IsValid)
                {
                    var errorDetails = string.Join("; ", validation.Errors);
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: graph validation failed: {errorDetails}", this);
                    return false;
                }
                if (_preflight != null && !_preflight.IsReady(_graph, out var reason))
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: preflight not ready. Reason: {reason}", this);
                    return false;
                }
                if (!_graph.Nodes.All(node => node != null && _registry.TryGet(node.NodeType, out var executor) && executor != null))
                {
                    var missing = _graph.Nodes.Where(node => node == null || !_registry.TryGet(node.NodeType, out var exec) || exec == null)
                                              .Select(node => node?.Id ?? "null_node");
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: missing executor for nodes: {string.Join(", ", missing)}", this);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LessonGraphV2] CanStart exception: {ex}", this);
                return false;
            }
        }

        private async Task<LessonResult> RunAsync(string runId, CancellationToken cancellationToken)
        {
            try
            {
                var nodes = _graph.Nodes.ToDictionary(node => node.Id);
                var node = nodes[_graph.EntryNodeId];
                Debug.Log($"[LessonGraphV2] LESSON START graph={_graph.name} entry={node.Id} runId={runId}", this);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var activationId = Guid.NewGuid().ToString("N");
                    BeginActivation(runId, activationId);
                    Emit(NodeEntered, new NodeEnteredEvent(runId, activationId, node.Id, _clock.ElapsedSeconds));
                    _registry.TryGet(node.NodeType, out var executor);
                    NodeResult result;
                    try
                    {
                        var executionTask = executor.ExecuteAsync(new NodeExecutionContext(runId, activationId, _graph.name, node, _clock.ElapsedSeconds,
                            cancellationToken, _skipCancellation.Token, _timeoutCancellation.Token, _checkpointTelemetry, _clock));
                        if (executionTask == null)
                        {
                            result = null;
                        }
                        else
                        {
                            using (var abort = new CancellationSignal(cancellationToken))
                            {
                                if (await Task.WhenAny(executionTask, abort.Task) != executionTask)
                                {
                                    ObserveFault(executionTask);
                                    var abortResult = LessonResult.Failed(runId, LessonFailureReason.Aborted, _clock.ElapsedSeconds);
                                    Emit(LessonCompleted, new LessonCompletedEvent(abortResult));
                                    return abortResult;
                                }
                            }
                            result = await executionTask;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        var abortResult = LessonResult.Failed(runId, LessonFailureReason.Aborted, _clock.ElapsedSeconds);
                        Emit(LessonCompleted, new LessonCompletedEvent(abortResult));
                        return abortResult;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LessonGraphV2] Executor exception node={node.Id}: {ex}", this);
                        result = NodeResult.Completed(node.Id, activationId, NodeStatus.Failed, _clock.ElapsedSeconds, "exception");
                    }

                    if (cancellationToken.IsCancellationRequested || !IsCurrentActivation(runId, activationId))
                    {
                        var abortResult = LessonResult.Failed(runId, LessonFailureReason.Aborted, _clock.ElapsedSeconds);
                        Emit(LessonCompleted, new LessonCompletedEvent(abortResult));
                        return abortResult;
                    }
                    if (result == null || result.ActivationId != activationId || result.NodeId != node.Id)
                    {
                        Debug.LogWarning($"[LessonGraphV2] Invalid/null result for node={node.Id}, treating as failed", this);
                        result = NodeResult.Completed(node.Id, activationId, NodeStatus.Failed, _clock.ElapsedSeconds, "invalid_result");
                    }

                    Emit(NodeCompleted, new NodeCompletedEvent(result));
                    var next = SelectEdge(node.Id, result.Status);
                    Debug.Log($"[LessonGraphV2] EDGE node={node.Id} status={result.Status} → next={next?.ToNodeId ?? "TERMINAL"}", this);
                    if (next == null)
                    {
                        var completed = LessonResult.Completed(runId, result, _clock.ElapsedSeconds);
                        Emit(LessonCompleted, new LessonCompletedEvent(completed));
                        return completed;
                    }
                    node = nodes[next.ToNodeId];
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var abortResult = LessonResult.Failed(runId, LessonFailureReason.Aborted, _clock.ElapsedSeconds);
                Emit(LessonCompleted, new LessonCompletedEvent(abortResult));
                return abortResult;
            }
            finally
            {
                EndRun(runId);
            }
        }

        private LessonEdgeData SelectEdge(string nodeId, NodeStatus status)
        {
            var outgoing = _graph.Edges.Where(edge => edge != null && edge.FromNodeId == nodeId).Select((edge, index) => new { edge, index });
            var matchedStatus = outgoing.Where(candidate => candidate.edge.Condition is StatusCondition condition && condition.RequiredStatus == NodeStatusCondition.ToCondition(status))
                .OrderBy(candidate => candidate.edge.Priority).ThenBy(candidate => candidate.index).FirstOrDefault();
            if (matchedStatus != null) return matchedStatus.edge;
            return outgoing.Where(candidate => candidate.edge.Condition is AlwaysCondition).OrderBy(candidate => candidate.edge.Priority).ThenBy(candidate => candidate.index)
                .Select(candidate => candidate.edge).FirstOrDefault();
        }

        private void BeginActivation(string runId, string activationId)
        {
            lock (_gate)
            {
                _skipCancellation?.Dispose();
                _timeoutCancellation?.Dispose();
                _skipCancellation = new CancellationTokenSource();
                _timeoutCancellation = new CancellationTokenSource();
                _activeRunId = runId;
                _activeActivationId = activationId;
            }
        }

        private bool IsCurrentActivation(string runId, string activationId)
        {
            lock (_gate) return _activeRunId == runId && _activeActivationId == activationId;
        }

        private void EndRun(string runId)
        {
            lock (_gate)
            {
                if (_activeRunId != runId) return;
                _lessonCancellation?.Dispose(); _lessonCancellation = null;
                _skipCancellation?.Dispose(); _skipCancellation = null;
                _timeoutCancellation?.Dispose(); _timeoutCancellation = null;
                _activeRunId = null;
                _activeActivationId = null;
            }
        }

        private static void CancelActive(CancellationTokenSource source)
        {
            try { source?.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(completed => {
                if (completed.Exception != null)
                    Debug.LogError($"[LessonGraphV2] Executor task faulted: {completed.Exception}");
            },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task => _completion.Task;

            public CancellationSignal(CancellationToken token)
            {
                if (token.CanBeCanceled) _registration = token.Register(() => _completion.TrySetResult(true));
            }

            public void Dispose() => _registration.Dispose();
        }
        private static void Emit<T>(Action<T> eventHandler, T payload)
        {
            if (eventHandler == null) return;
            foreach (var subscriber in eventHandler.GetInvocationList())
            {
                if (subscriber is Action<T> handler)
                {
                    try { handler(payload); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
        }

        private void OnDisable() => AbortLesson();
        private void OnDestroy() => AbortLesson();
    }
}