using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Firebase.Firestore;
using VRAutism.Gameplay.LessonGraphV2.Remote;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    public static class LessonTelemetryEventTypeV2
    {
        public const string NodeEntered = "NODE_ENTERED";
        public const string NodeCompleted = "NODE_COMPLETED";
        public const string NodeCancelled = "NODE_CANCELLED";
        public const string LessonPaused = "LESSON_PAUSED";
        public const string LessonResumed = "LESSON_RESUMED";
        public const string LessonCompleted = "LESSON_COMPLETED";
        public const string LessonFailed = "LESSON_FAILED";
        public const string LessonCancelled = "LESSON_CANCELLED";
        public const string CheckpointReached = "CHECKPOINT_REACHED";
        public const string CommandAccepted = "COMMAND_ACCEPTED";
        public const string CommandRejected = "COMMAND_REJECTED";
    }

    public interface IUtcClockV2
    {
        DateTimeOffset UtcNow { get; }
    }

    public sealed class SystemUtcClockV2 : IUtcClockV2
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public sealed class LessonTelemetryContextV2
    {
        public string SessionId { get; }
        public string RunId { get; }
        public string GraphId { get; }
        public string LessonId { get; }
        public string LaunchToken { get; }
        public int LessonVoiceRevision { get; }
        public int ChildPhraseRevision { get; }

        public LessonTelemetryContextV2(string sessionId, string runId, string graphId, string lessonId,
            string launchToken, int lessonVoiceRevision, int childPhraseRevision)
        {
            SessionId = Require(sessionId, nameof(sessionId));
            RunId = Require(runId, nameof(runId));
            GraphId = graphId ?? string.Empty;
            LessonId = lessonId ?? string.Empty;
            LaunchToken = launchToken ?? string.Empty;
            if (lessonVoiceRevision < 0) throw new ArgumentOutOfRangeException(nameof(lessonVoiceRevision));
            if (childPhraseRevision < 0) throw new ArgumentOutOfRangeException(nameof(childPhraseRevision));
            LessonVoiceRevision = lessonVoiceRevision;
            ChildPhraseRevision = childPhraseRevision;
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", name);
            return value;
        }
    }

    public sealed class LessonTelemetryNodeV2
    {
        public string NodeId { get; }
        public string NodeType { get; }
        public string NodeName { get; }
        public int NodeIndex { get; }
        public string ActivationId { get; }

        public LessonTelemetryNodeV2(string nodeId, string nodeType, string nodeName, int nodeIndex, string activationId)
        {
            NodeId = Require(nodeId, nameof(nodeId));
            NodeType = nodeType ?? string.Empty;
            NodeName = nodeName ?? string.Empty;
            if (nodeIndex < 0) throw new ArgumentOutOfRangeException(nameof(nodeIndex));
            NodeIndex = nodeIndex;
            ActivationId = Require(activationId, nameof(activationId));
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", name);
            return value;
        }
    }

    /// <summary>
    /// Immutable observation input. ElapsedSeconds is a monotonic run reading, never a wall-clock duration.
    /// </summary>
    public sealed class LessonLifecycleEventV2
    {
        private readonly LessonStateV2 _stateSnapshot;

        public LessonTelemetryContextV2 Context { get; }
        public LessonTelemetryNodeV2 Node { get; }
        public string EventType { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public double ElapsedSeconds { get; }
        public string Status { get; }
        public string CompletionChannel { get; }
        public string CommandId { get; }
        public string Command { get; }
        public string BindingId { get; }
        public string Reason { get; }
        public bool IsStateTransition { get; }
        public LessonStateV2 StateSnapshot => CloneState(_stateSnapshot);

        private LessonLifecycleEventV2(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            string eventType, DateTimeOffset occurredAtUtc, double elapsedSeconds, string status,
            string completionChannel, string commandId, string command, string bindingId, string reason,
            bool isStateTransition, LessonStateV2 stateSnapshot)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            EventType = Require(eventType, nameof(eventType));
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            Node = node;
            OccurredAtUtc = occurredAtUtc.ToUniversalTime();
            ElapsedSeconds = elapsedSeconds;
            Status = status ?? string.Empty;
            CompletionChannel = completionChannel ?? string.Empty;
            CommandId = commandId ?? string.Empty;
            Command = command ?? string.Empty;
            BindingId = bindingId ?? string.Empty;
            Reason = reason ?? string.Empty;
            IsStateTransition = isStateTransition;
            _stateSnapshot = CloneState(stateSnapshot);
        }

        public static LessonLifecycleEventV2 NodeEntered(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            DateTimeOffset occurredAtUtc, double elapsedSeconds, LessonStateV2 state) =>
            Create(context, node, LessonTelemetryEventTypeV2.NodeEntered, occurredAtUtc, elapsedSeconds,
                "running", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, true, state);

        public static LessonLifecycleEventV2 NodeCompleted(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            string status, string completionChannel, DateTimeOffset occurredAtUtc, double elapsedSeconds,
            LessonStateV2 state) =>
            Create(context, node, LessonTelemetryEventTypeV2.NodeCompleted, occurredAtUtc, elapsedSeconds,
                status, completionChannel, string.Empty, string.Empty, string.Empty, string.Empty, true, state);

        public static LessonLifecycleEventV2 NodeCancelled(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            string reason, DateTimeOffset occurredAtUtc, double elapsedSeconds, LessonStateV2 state) =>
            Create(context, node, LessonTelemetryEventTypeV2.NodeCancelled, occurredAtUtc, elapsedSeconds,
                "cancelled", string.Empty, string.Empty, string.Empty, string.Empty, reason, true, state);

        public static LessonLifecycleEventV2 CommandDisposition(LessonTelemetryContextV2 context,
            LessonTelemetryNodeV2 node, string commandId, string command, string bindingId,
            bool accepted, string reason, DateTimeOffset occurredAtUtc, double elapsedSeconds,
            LessonStateV2 state) =>
            Create(context, node,
                accepted ? LessonTelemetryEventTypeV2.CommandAccepted : LessonTelemetryEventTypeV2.CommandRejected,
                occurredAtUtc, elapsedSeconds, state == null ? string.Empty : state.status,
                string.Empty, commandId, command, bindingId, reason, false, null);

        public static LessonLifecycleEventV2 LessonTerminated(LessonTelemetryContextV2 context, string status,
            DateTimeOffset occurredAtUtc, double elapsedSeconds, LessonStateV2 state)
        {
            string eventType = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                ? LessonTelemetryEventTypeV2.LessonCompleted
                : string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                    ? LessonTelemetryEventTypeV2.LessonFailed
                    : LessonTelemetryEventTypeV2.LessonCancelled;
            return Create(context, null, eventType, occurredAtUtc, elapsedSeconds, status,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, true, state);
        }

        public static LessonLifecycleEventV2 StateTransition(LessonTelemetryContextV2 context,
            LessonTelemetryNodeV2 node, string eventType, string status, string reason,
            DateTimeOffset occurredAtUtc, double elapsedSeconds, LessonStateV2 state)
        {
            bool transition = eventType == LessonTelemetryEventTypeV2.LessonPaused ||
                              eventType == LessonTelemetryEventTypeV2.LessonResumed ||
                              eventType == LessonTelemetryEventTypeV2.CheckpointReached;
            if (!transition) throw new ArgumentException("Only pause, resume, and checkpoint state transitions are supported.", nameof(eventType));
            return Create(context, node, eventType, occurredAtUtc, elapsedSeconds, status,
                string.Empty, string.Empty, string.Empty, string.Empty, reason, true, state);
        }

        private static LessonLifecycleEventV2 Create(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            string eventType, DateTimeOffset occurredAtUtc, double elapsedSeconds, string status,
            string completionChannel, string commandId, string command, string bindingId, string reason,
            bool isStateTransition, LessonStateV2 state) =>
            new LessonLifecycleEventV2(context, node, eventType, occurredAtUtc, elapsedSeconds, status,
                completionChannel, commandId, command, bindingId, reason, isStateTransition, state);

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", name);
            return value;
        }

        internal static LessonStateV2 CloneState(LessonStateV2 source)
        {
            if (source == null) return null;
            var clone = new LessonStateV2
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
                clone.bindings = new LessonBindingV2[source.bindings.Length];
                for (int i = 0; i < source.bindings.Length; i++)
                {
                    LessonBindingV2 binding = source.bindings[i];
                    clone.bindings[i] = binding == null ? null : new LessonBindingV2
                    {
                        binding_id = binding.binding_id,
                        npc_binding_id = binding.npc_binding_id,
                        can_verbal_hint = binding.can_verbal_hint,
                        can_visual_hint = binding.can_visual_hint
                    };
                }
            }
            return clone;
        }
    }

    [FirestoreData]
    public sealed class LessonAuditEventDataV2
    {
        [FirestoreProperty] public string event_id { get; set; }
        [FirestoreProperty] public string event_type { get; set; }
        [FirestoreProperty] public string session_id { get; set; }
        [FirestoreProperty] public string run_id { get; set; }
        [FirestoreProperty] public string graph_id { get; set; }
        [FirestoreProperty] public string lesson_id { get; set; }
        [FirestoreProperty] public string node_id { get; set; }
        [FirestoreProperty] public string node_type { get; set; }
        [FirestoreProperty] public int node_index { get; set; }
        [FirestoreProperty] public string activation_id { get; set; }
        [FirestoreProperty] public string occurred_at_utc { get; set; }
        [FirestoreProperty] public double elapsed_seconds { get; set; }
        [FirestoreProperty] public string status { get; set; }
        [FirestoreProperty] public string command_id { get; set; }
        [FirestoreProperty] public string command { get; set; }
        [FirestoreProperty] public string binding_id { get; set; }
        [FirestoreProperty] public string reason { get; set; }
        [FirestoreProperty] public string launch_token { get; set; }
        [FirestoreProperty] public int lesson_voice_revision { get; set; }
        [FirestoreProperty] public int child_phrase_revision { get; set; }
    }

    public sealed class QuestHintSummaryV2
    {
        public int AcceptedVerbalHints { get; set; }
        public int AcceptedVisualHints { get; set; }
        public double TotalDurationSeconds { get; set; }
        public double LastVisualHintElapsedSeconds { get; set; } = -1d;
        public double TerminalElapsedSeconds { get; set; }
    }

    public sealed class TelemetryWriteBatchV2
    {
        public LessonStateV2 state_projection { get; internal set; }
        public System.Collections.Generic.Dictionary<string, LessonAuditEventDataV2> audit_events_by_id { get; } =
            new System.Collections.Generic.Dictionary<string, LessonAuditEventDataV2>();
        public System.Collections.Generic.Dictionary<string, VRAutism.Cloud.Models.NodeLogData> node_logs_by_id { get; } =
            new System.Collections.Generic.Dictionary<string, VRAutism.Cloud.Models.NodeLogData>();
        public System.Collections.Generic.Dictionary<string, VRAutism.Cloud.Models.QuestLogData> quest_logs_by_id { get; } =
            new System.Collections.Generic.Dictionary<string, VRAutism.Cloud.Models.QuestLogData>();
    }

    internal static class LessonTelemetryIdentityV2
    {
        public static string CompositeKey(params string[] values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                string item = value ?? string.Empty;
                builder.Append(item.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(item);
                builder.Append(';');
            }
            return builder.ToString();
        }

        public static string Lifecycle(string sessionId, string runId, string activationId, string eventType) =>
            Hash("lifecycle", sessionId, runId, activationId, eventType);

        public static string Command(string sessionId, string runId, string commandId, string disposition) =>
            Hash("command", sessionId, runId, commandId, disposition);

        public static string NodeLog(string sessionId, string runId, string activationId) =>
            Hash("node", sessionId, runId, activationId);

        public static string QuestLog(string sessionId, string runId, string nodeId) =>
            Hash("quest", sessionId, runId, nodeId);

        private static string Hash(params string[] values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                string item = value ?? string.Empty;
                builder.Append(item.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(item);
                builder.Append(';');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }
}
