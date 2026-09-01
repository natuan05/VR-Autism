using System;
using System.Collections.Generic;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Validation
{
    /// <summary>
    /// Static preflight validator for Phase 1 LessonGraph assets.
    /// Callable by editor tooling and by LessonGraphRunner before StartLesson().
    /// Does not load scenes or resolve binding IDs to MonoBehaviours.
    /// Collects ALL errors before returning — callers see the complete picture.
    /// </summary>
    public static class LessonGraphValidator
    {
        /// <summary>
        /// Highest schema version this validator understands.
        /// Graphs with a higher version must be rejected as unsupported.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        // Phase 1 allowed node types.
        private static readonly HashSet<NodeType> s_allowedNodeTypes = new HashSet<NodeType>
        {
            NodeType.Quest,
            NodeType.Dialogue,
            NodeType.Wait,
            NodeType.Checkpoint,
        };

        // Phase 1 allowed edge condition concrete types (whitelist).
        private static readonly HashSet<Type> s_allowedConditionTypes = new HashSet<Type>
        {
            typeof(AlwaysCondition),
            typeof(StatusCondition),
        };

        // Valid Phase 1 node completion statuses for StatusCondition.
        private static readonly HashSet<string> s_validStatuses = new HashSet<string>
        {
            StatusCondition.Success,
            StatusCondition.Skipped,
            StatusCondition.Timeout,
            StatusCondition.Failed,
        };

        // Maps each Phase 1 NodeType to its required concrete config type.
        private static readonly Dictionary<NodeType, Type> s_expectedConfigType = new Dictionary<NodeType, Type>
        {
            { NodeType.Quest,      typeof(QuestNodeConfig)      },
            { NodeType.Dialogue,   typeof(DialogueNodeConfig)   },
            { NodeType.Wait,       typeof(WaitNodeConfig)       },
            { NodeType.Checkpoint, typeof(CheckpointNodeConfig) },
        };

        /// <summary>
        /// Validates the supplied graph and returns an immutable result.
        /// </summary>
        /// <exception cref="ArgumentNullException">graph is null.</exception>
        public static GraphValidationResult Validate(LessonGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var errors = new List<GraphValidationError>();

            // ── 0. Schema version ──────────────────────────────────────────────
            if (graph.SchemaVersion <= 0)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidSchemaVersion,
                    $"SchemaVersion must be >= 1. Got: {graph.SchemaVersion}."));
            }
            else if (graph.SchemaVersion > CurrentSchemaVersion)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.UnsupportedSchemaVersion,
                    $"SchemaVersion {graph.SchemaVersion} is not supported by this validator " +
                    $"(current: {CurrentSchemaVersion}). Update the validator or downgrade the asset."));
            }

            // ── 1. Null collection guard ───────────────────────────────────────
            bool nodesValid = true;
            bool edgesValid = true;

            if (graph.Nodes == null)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.NullCollection,
                    "LessonGraph.Nodes collection is null. Initialize it to an empty list."));
                nodesValid = false;
            }

            if (graph.Edges == null)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.NullCollection,
                    "LessonGraph.Edges collection is null. Initialize it to an empty list."));
                edgesValid = false;
            }

            // ── 2. Entry node ID ───────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(graph.EntryNodeId))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.MissingEntryNodeId,
                    "EntryNodeId is null or whitespace."));
            }

            // ── 3. Build node ID index — reject null/blank IDs, check duplicates ─
            var nodeIds      = new HashSet<string>();
            var duplicateIds = new HashSet<string>();

            if (nodesValid)
            {
                foreach (var node in graph.Nodes)
                {
                    // Reject null entries.
                    if (node == null)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NullNodeEntry,
                            "The Nodes list contains a null entry. Remove it."));
                        continue;
                    }

                    // Reject null/blank IDs before using as dict key.
                    if (string.IsNullOrWhiteSpace(node.Id))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.InvalidNodeId,
                            "A node has a null, empty, or whitespace ID. Every node must have a unique non-blank ID."));
                        continue;
                    }

                    if (!nodeIds.Add(node.Id))
                        duplicateIds.Add(node.Id);
                }

                foreach (var dup in duplicateIds)
                {
                    errors.Add(new GraphValidationError(
                        GraphValidationErrorCode.DuplicateNodeId,
                        $"Duplicate node ID: \"{dup}\".",
                        dup));
                }

                // ── 4. Entry node must exist ───────────────────────────────────
                if (!string.IsNullOrWhiteSpace(graph.EntryNodeId) && !nodeIds.Contains(graph.EntryNodeId))
                {
                    errors.Add(new GraphValidationError(
                        GraphValidationErrorCode.EntryNodeNotFound,
                        $"EntryNodeId \"{graph.EntryNodeId}\" does not reference any node in the nodes list."));
                }

                // ── 5. Per-node validation ─────────────────────────────────────
                var globalBindingIds  = new HashSet<string>();
                var globalSequenceIds = new HashSet<string>();

                foreach (var node in graph.Nodes)
                {
                    if (node == null || string.IsNullOrWhiteSpace(node.Id)) continue;

                    var nodeId = node.Id;

                    // 5a. Phase 2 node types are rejected.
                    if (!s_allowedNodeTypes.Contains(node.NodeType))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.Phase2NodeType,
                            $"Node type \"{node.NodeType}\" is not supported in Phase 1.",
                            nodeId));
                    }

                    // 5b. Config must not be null.
                    if (node.Config == null)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NullNodeConfig,
                            "Node has null config.",
                            nodeId));
                        continue;
                    }

                    // 5c. NodeType ↔ Config type must match (Phase 1 nodes only).
                    if (s_allowedNodeTypes.Contains(node.NodeType) &&
                        s_expectedConfigType.TryGetValue(node.NodeType, out var expectedType) &&
                        node.Config.GetType() != expectedType)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NodeTypeConfigMismatch,
                            $"NodeType \"{node.NodeType}\" requires config type \"{expectedType.Name}\" " +
                            $"but got \"{node.Config.GetType().Name}\".",
                            nodeId));
                        continue; // cast would throw — skip field validation
                    }

                    // 5d. Config-specific field validation.
                    switch (node.Config)
                    {
                        case QuestNodeConfig q:
                            ValidateQuestConfig(q, nodeId, globalBindingIds, errors);
                            break;
                        case DialogueNodeConfig d:
                            ValidateDialogueConfig(d, nodeId, globalSequenceIds, errors);
                            break;
                        case WaitNodeConfig w:
                            ValidateWaitConfig(w, nodeId, errors);
                            break;
                        case CheckpointNodeConfig c:
                            ValidateCheckpointConfig(c, nodeId, errors);
                            break;
                    }
                }
            }

            // ── 6. Edge validation ─────────────────────────────────────────────
            if (edgesValid)
            {
                foreach (var edge in graph.Edges)
                {
                    // Reject null entries.
                    if (edge == null)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NullEdgeEntry,
                            "The Edges list contains a null entry. Remove it."));
                        continue;
                    }

                    // 6a. fromNodeId must exist.
                    if (nodesValid && !nodeIds.Contains(edge.FromNodeId))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.DanglingEdgeSource,
                            $"Edge has fromNodeId \"{edge.FromNodeId}\" which does not exist in the nodes list."));
                    }

                    // 6b. toNodeId must exist.
                    if (nodesValid && !nodeIds.Contains(edge.ToNodeId))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.MissingEdgeTarget,
                            $"Edge from \"{edge.FromNodeId}\" targets unknown node \"{edge.ToNodeId}\"."));
                    }

                    // 6c. Condition must not be null.
                    if (edge.Condition == null)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NullEdgeCondition,
                            $"Edge from \"{edge.FromNodeId}\" to \"{edge.ToNodeId}\" has a null condition. " +
                            "Use AlwaysCondition for unconditional transitions."));
                        continue;
                    }

                    // 6d. Phase 2 condition types are rejected.
                    var condType = edge.Condition.GetType();
                    if (!s_allowedConditionTypes.Contains(condType))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.Phase2EdgeCondition,
                            $"Edge condition type \"{condType.Name}\" is not supported in Phase 1."));
                        continue;
                    }

                    // 6e. StatusCondition value must be in the allowed set.
                    if (edge.Condition is StatusCondition sc &&
                        !s_validStatuses.Contains(sc.RequiredStatus))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.InvalidStatusValue,
                            $"StatusCondition has invalid requiredStatus \"{sc.RequiredStatus}\". " +
                            $"Allowed: {string.Join(", ", s_validStatuses)}."));
                    }
                }
            }

            // ── 7. Cycle detection (DFS on ALL nodes, including disconnected) ──
            if (nodesValid && edgesValid)
                DetectCycles(graph, nodeIds, errors);

            return errors.Count == 0
                ? GraphValidationResult.Ok()
                : GraphValidationResult.Fail(errors);
        }

        // ── Config validators ──────────────────────────────────────────────────

        private static void ValidateQuestConfig(
            QuestNodeConfig config,
            string nodeId,
            HashSet<string> globalBindingIds,
            List<GraphValidationError> errors)
        {
            // Binding IDs.
            if (config.CompletionBindingIds == null || config.CompletionBindingIds.Count == 0)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.EmptyBindingIds,
                    "QuestNodeConfig.completionBindingIds must have at least one entry.",
                    nodeId));
            }
            else
            {
                foreach (var bid in config.CompletionBindingIds)
                {
                    if (bid == null)
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.NullBindingId,
                            "A completionBindingId is null.",
                            nodeId));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(bid))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.WhitespaceBindingId,
                            "A completionBindingId is empty or whitespace.",
                            nodeId));
                        continue;
                    }

                    if (!globalBindingIds.Add(bid))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.DuplicateBindingId,
                            $"Duplicate completionBindingId \"{bid}\" found across QuestNodeConfigs.",
                            nodeId));
                    }
                }
            }

            // Timeout domain: -1 (no timeout) or > 0 finite.
            var t = config.TimeoutSeconds;
            if (!IsValidTimeout(t))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidTimeoutValue,
                    $"QuestNodeConfig.timeoutSeconds must be -1 (no timeout) or a finite positive value. Got: {t}.",
                    nodeId));
            }
        }

        private static void ValidateDialogueConfig(
            DialogueNodeConfig config,
            string nodeId,
            HashSet<string> globalSequenceIds,
            List<GraphValidationError> errors)
        {
            // Required text fields.
            if (string.IsNullOrWhiteSpace(config.SequenceId))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidDialogueConfig,
                    "DialogueNodeConfig.sequenceId must not be empty or whitespace.",
                    nodeId));
            }
            else if (!globalSequenceIds.Add(config.SequenceId))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.DuplicateSequenceId,
                    $"DialogueNodeConfig.sequenceId \"{config.SequenceId}\" is already used by another " +
                    "Dialogue node. SequenceIds must be unique across the lesson (used as SPEAK_SCRIPT_DONE correlation IDs).",
                    nodeId));
            }

            if (string.IsNullOrWhiteSpace(config.Text))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidDialogueConfig,
                    "DialogueNodeConfig.text must not be empty or whitespace.",
                    nodeId));
            }

            // Timeout domain: -1 (no timeout) or > 0 finite.
            var t = config.TimeoutSeconds;
            if (!IsValidTimeout(t))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidTimeoutValue,
                    $"DialogueNodeConfig.timeoutSeconds must be -1 (no timeout) or a finite positive value. Got: {t}.",
                    nodeId));
            }
        }

        private static void ValidateWaitConfig(
            WaitNodeConfig config,
            string nodeId,
            List<GraphValidationError> errors)
        {
            var d = config.Duration;
            if (float.IsNaN(d) || float.IsInfinity(d) || d <= 0f)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidWaitConfig,
                    $"WaitNodeConfig.duration must be finite and > 0. Got: {d}.",
                    nodeId));
            }
        }

        private static void ValidateCheckpointConfig(
            CheckpointNodeConfig config,
            string nodeId,
            List<GraphValidationError> errors)
        {
            if (string.IsNullOrWhiteSpace(config.CheckpointId))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidCheckpointConfig,
                    "CheckpointNodeConfig.checkpointId must not be empty or whitespace.",
                    nodeId));
            }
        }

        // Returns true when a timeout value is in the allowed domain:
        //   -1  = explicitly no timeout
        //   > 0, finite, not NaN = valid positive timeout
        private static bool IsValidTimeout(float t) =>
            t == -1f || (t > 0f && !float.IsNaN(t) && !float.IsInfinity(t));

        // ── Cycle detection ────────────────────────────────────────────────────

        /// <summary>
        /// DFS cycle detection across ALL nodes (including disconnected subgraphs).
        /// Only edges whose both endpoints are valid node IDs are included.
        /// </summary>
        private static void DetectCycles(
            LessonGraph graph,
            HashSet<string> allNodeIds,
            List<GraphValidationError> errors)
        {
            var adjacency = new Dictionary<string, List<string>>(allNodeIds.Count);
            foreach (var id in allNodeIds)
                adjacency[id] = new List<string>();

            foreach (var edge in graph.Edges)
            {
                if (edge == null) continue;
                if (!allNodeIds.Contains(edge.FromNodeId)) continue;
                if (!allNodeIds.Contains(edge.ToNodeId))  continue;
                adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            }

            // Three-color DFS: 0=white, 1=gray (in stack), 2=black (done).
            var color = new Dictionary<string, int>(allNodeIds.Count);
            foreach (var id in allNodeIds)
                color[id] = 0;

            foreach (var startId in allNodeIds)
            {
                if (color[startId] == 0)
                    DfsVisit(startId, adjacency, color, errors);
            }
        }

        private static void DfsVisit(
            string nodeId,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, int> color,
            List<GraphValidationError> errors)
        {
            color[nodeId] = 1; // gray

            foreach (var neighbor in adjacency[nodeId])
            {
                if (!color.TryGetValue(neighbor, out var c)) continue;

                if (c == 1)
                {
                    errors.Add(new GraphValidationError(
                        GraphValidationErrorCode.CycleDetected,
                        $"Cycle detected: edge \"{nodeId}\" → \"{neighbor}\" is a back-edge. " +
                        "Phase 1 graph must be a DAG.",
                        nodeId));
                }
                else if (c == 0)
                {
                    DfsVisit(neighbor, adjacency, color, errors);
                }
            }

            color[nodeId] = 2; // black
        }
    }
}
