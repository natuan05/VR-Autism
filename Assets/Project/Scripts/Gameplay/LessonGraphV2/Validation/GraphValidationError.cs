using System;

namespace VRAutism.Gameplay.LessonGraphV2.Validation
{
    /// <summary>
    /// Categorizes a single graph validation failure.
    /// </summary>
    public enum GraphValidationErrorCode
    {
        // Schema errors
        InvalidSchemaVersion,        // value <= 0
        UnsupportedSchemaVersion,    // value > current supported version

        // Collection-level errors
        NullCollection,              // Nodes or Edges list is null
        NullNodeEntry,               // a null entry inside the Nodes list
        NullEdgeEntry,               // a null entry inside the Edges list

        // Entry point errors
        MissingEntryNodeId,
        EntryNodeNotFound,

        // Node ID errors
        InvalidNodeId,               // null / empty / whitespace node ID

        // Node errors
        DuplicateNodeId,
        NullNodeConfig,
        NodeTypeConfigMismatch,      // NodeType does not match concrete config type
        Phase2NodeType,

        // QuestNodeConfig errors
        EmptyBindingIds,
        NullBindingId,
        WhitespaceBindingId,
        DuplicateBindingId,
        InvalidTimeoutValue,         // Quest or Dialogue timeout outside allowed domain

        // DialogueNodeConfig errors
        InvalidDialogueConfig,       // empty sequenceId or text
        DuplicateSequenceId,         // sequenceId not unique across lesson

        // WaitNodeConfig errors
        InvalidWaitConfig,           // duration <= 0, NaN, or infinite

        // CheckpointNodeConfig errors
        InvalidCheckpointConfig,     // empty checkpointId

        // Edge errors
        DanglingEdgeSource,          // fromNodeId not in nodes list
        MissingEdgeTarget,           // toNodeId not in nodes list
        NullEdgeCondition,           // condition is null

        // Edge condition errors
        Phase2EdgeCondition,
        InvalidStatusValue,          // StatusCondition value not in allowed set

        // Graph structure errors
        CycleDetected,
    }

    /// <summary>
    /// A single validation error found during graph preflight validation.
    /// Immutable typed DTO — not Dictionary&lt;string, object&gt;.
    /// </summary>
    [Serializable]
    public sealed class GraphValidationError
    {
        public GraphValidationErrorCode ErrorCode { get; }
        public string Message { get; }

        /// <summary>Node ID associated with this error, or empty if not node-specific.</summary>
        public string NodeId { get; }

        public GraphValidationError(GraphValidationErrorCode code, string message, string nodeId = "")
        {
            ErrorCode = code;
            Message = message ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
        }

        public override string ToString() =>
            string.IsNullOrEmpty(NodeId)
                ? $"[{ErrorCode}] {Message}"
                : $"[{ErrorCode}] (node: {NodeId}) {Message}";
    }
}
