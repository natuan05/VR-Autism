using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public enum NodeStatus { Success, Skipped, Timeout, Failed }
    public enum LessonFailureReason { None, InvalidGraph, Aborted }

    public static class NodeStatusCondition
    {
        public static string ToCondition(NodeStatus status)
        {
            switch (status)
            {
                case NodeStatus.Success: return "success";
                case NodeStatus.Skipped: return "skipped";
                case NodeStatus.Timeout: return "timeout";
                default: return "failed";
            }
        }
    }

    public sealed class NodeResult
    {
        public string NodeId { get; }
        public string ActivationId { get; }
        public NodeStatus Status { get; }
        public double ElapsedSeconds { get; }
        public string CompletionChannel { get; }
        public string TelemetryEventId { get; }
        private NodeResult(string nodeId, string activationId, NodeStatus status, double elapsedSeconds, string completionChannel, string telemetryEventId)
        { NodeId = nodeId; ActivationId = activationId; Status = status; ElapsedSeconds = elapsedSeconds; CompletionChannel = completionChannel; TelemetryEventId = telemetryEventId; }
        public static NodeResult Completed(string nodeId, string activationId, NodeStatus status, double elapsedSeconds, string completionChannel = null, string telemetryEventId = null)
            => new NodeResult(nodeId, activationId, status, elapsedSeconds, completionChannel, telemetryEventId ?? $"node:{activationId}:{nodeId}");
    }

    public sealed class LessonResult
    {
        public string RunId { get; }
        public LessonFailureReason FailureReason { get; }
        public NodeResult FinalNodeResult { get; }
        public double ElapsedSeconds { get; }
        public string TelemetryEventId { get; }
        public bool IsSuccess => FailureReason == LessonFailureReason.None;
        private LessonResult(string runId, LessonFailureReason failureReason, NodeResult finalNodeResult, double elapsedSeconds)
        { RunId = runId; FailureReason = failureReason; FinalNodeResult = finalNodeResult; ElapsedSeconds = elapsedSeconds; TelemetryEventId = $"lesson:{runId}"; }
        public static LessonResult Completed(string runId, NodeResult finalNodeResult, double elapsedSeconds) => new LessonResult(runId, LessonFailureReason.None, finalNodeResult, elapsedSeconds);
        public static LessonResult Failed(string runId, LessonFailureReason reason, double elapsedSeconds = 0) => new LessonResult(runId, reason, null, elapsedSeconds);
    }

    public sealed class NodeEnteredEvent { public string RunId { get; } public string ActivationId { get; } public string NodeId { get; } public double ElapsedSeconds { get; } public NodeEnteredEvent(string runId, string activationId, string nodeId, double elapsedSeconds) { RunId = runId; ActivationId = activationId; NodeId = nodeId; ElapsedSeconds = elapsedSeconds; } }
    public sealed class NodeCompletedEvent { public NodeResult Result { get; } public NodeCompletedEvent(NodeResult result) { Result = result; } }
    public sealed class LessonCompletedEvent { public LessonResult Result { get; } public LessonCompletedEvent(LessonResult result) { Result = result; } }

    public interface ILessonStartPreflight { bool IsReady(LessonGraph graph, out string reason); }
    public interface INodeClock { double ElapsedSeconds { get; } Task Delay(float seconds, CancellationToken cancellationToken); }
    public interface ICheckpointTelemetry { void Record(CheckpointMarker marker); }
    public sealed class CheckpointMarker
    {
        public string RunId { get; } public string ActivationId { get; } public string GraphId { get; } public string NodeId { get; } public string CheckpointId { get; } public double ElapsedSeconds { get; } public string EventId { get; }
        public CheckpointMarker(string runId, string activationId, string graphId, string nodeId, string checkpointId, double elapsedSeconds)
        { RunId = runId; ActivationId = activationId; GraphId = graphId; NodeId = nodeId; CheckpointId = checkpointId; ElapsedSeconds = elapsedSeconds; EventId = $"checkpoint:{runId}:{activationId}:{nodeId}"; }
    }
    public sealed class NodeExecutionContext
    {
        public string RunId { get; } public string ActivationId { get; } public string GraphId { get; } public LessonNodeData Node { get; } public double ElapsedSeconds { get; } public CancellationToken CancellationToken { get; } public CancellationToken SkipToken { get; } public CancellationToken TimeoutToken { get; } public ICheckpointTelemetry CheckpointTelemetry { get; }
        public INodeClock Clock { get; }
        public NodeExecutionContext(string runId, string activationId, string graphId, LessonNodeData node, double elapsedSeconds, CancellationToken cancellationToken, CancellationToken skipToken, CancellationToken timeoutToken, ICheckpointTelemetry checkpointTelemetry, INodeClock clock = null)
        { RunId = runId; ActivationId = activationId; GraphId = graphId; Node = node; ElapsedSeconds = elapsedSeconds; CancellationToken = cancellationToken; SkipToken = skipToken; TimeoutToken = timeoutToken; CheckpointTelemetry = checkpointTelemetry; Clock = clock; }
    }
    public interface INodeExecutor { Task<NodeResult> ExecuteAsync(NodeExecutionContext context); }
    public interface INodeExecutorRegistry { bool TryGet(NodeType type, out INodeExecutor executor); }

    public sealed class MonotonicClock : INodeClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;
        public Task Delay(float seconds, CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }
}
