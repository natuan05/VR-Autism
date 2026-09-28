using System;
using System.Collections.Generic;
using System.Linq;
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
        public const int CurrentSchemaVersion = 2;

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
            { NodeType.Timeline, typeof(TimelineNodeConfig) },
            { NodeType.Parallel, typeof(ParallelNodeConfig) },
            { NodeType.Gate, typeof(GateNodeConfig) },
            { NodeType.Loop, typeof(LoopNodeConfig) },
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

                    var schemaOneNode = s_allowedNodeTypes.Contains(node.NodeType);
                    var schemaTwoNode = s_expectedConfigType.ContainsKey(node.NodeType);
                    if (!schemaTwoNode || (graph.SchemaVersion == 1 && !schemaOneNode))
                    {
                        errors.Add(new GraphValidationError(
                            GraphValidationErrorCode.Phase2NodeType,
                            $"Node type \"{node.NodeType}\" is not supported by schema {graph.SchemaVersion}.",
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
                    if (s_expectedConfigType.TryGetValue(node.NodeType, out var expectedType) &&
                        (graph.SchemaVersion == 2 || schemaOneNode) && node.Config.GetType() != expectedType)
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
                            if (graph.SchemaVersion == 2 && string.IsNullOrWhiteSpace(c.ResumeCompatibilityKey))
                                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidCheckpointConfig,
                                    "Schema 2 checkpoints require a nonblank resumeCompatibilityKey.", nodeId));
                            break;
                        case TimelineNodeConfig t:
                            ValidateTimelineConfig(t, nodeId, errors);
                            break;
                        case ParallelNodeConfig p:
                            if (p.Branches == null || p.Branches.Count < 2 || string.IsNullOrWhiteSpace(p.GateNodeId) ||
                                !Enum.IsDefined(typeof(ParallelJoinPolicy), p.JoinPolicy))
                                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidParallelConfig,
                                    "Parallel requires at least two branches, a gate node ID, and a valid join policy.", nodeId));
                            break;
                        case GateNodeConfig g:
                            if (string.IsNullOrWhiteSpace(g.ParallelNodeId) || g.CompletedBranchIds == null ||
                                g.CompletedBranchIds.Count == 0 || !Enum.IsDefined(typeof(GateConditionMode), g.Mode))
                                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                                    "Gate requires an owner parallel ID, completed branch inputs, and a valid mode.", nodeId));
                            break;
                        case LoopNodeConfig l:
                            if (string.IsNullOrWhiteSpace(l.BodyChildNodeId) || string.IsNullOrWhiteSpace(l.ExitNodeId) ||
                                l.BodyChildNodeId == l.ExitNodeId || l.BodyChildNodeId == nodeId || l.ExitNodeId == nodeId ||
                                l.MaximumIterations <= 0 || l.ExitCondition == null)
                                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidLoopConfig,
                                    "Loop requires distinct body and exit IDs, a positive maximum iteration count, and an exit condition.", nodeId));
                            if (l.ExitCondition != null)
                                ValidateCondition(l.ExitCondition, graph.SchemaVersion, nodeId, errors,
                                    new HashSet<IEdgeCondition>(), 1, GraphValidationErrorCode.InvalidLoopConfig);
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

                    // 6d. Conditions are schema-versioned and strictly whitelisted.
                    var condType = edge.Condition.GetType();
                    if (!s_allowedConditionTypes.Contains(condType) &&
                        !(graph.SchemaVersion == 2 && (condType == typeof(VariableCondition) || condType == typeof(CompositeCondition))))
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
                    else if (edge.Condition is VariableCondition variable)
                    {
                        ValidateVariableCondition(variable, errors, string.Empty);
                    }
                    else if (edge.Condition is CompositeCondition composite)
                    {
                        ValidateCondition(composite, graph.SchemaVersion, string.Empty, errors,
                            new HashSet<IEdgeCondition>(), 1, GraphValidationErrorCode.InvalidCompositeCondition);
                    }
                }
            }

            // ── 7. Cycle detection (DFS on ALL nodes, including disconnected) ──
            if (nodesValid && edgesValid)
            {
                DetectCycles(graph, nodeIds, errors);
                ValidateStructuredReferences(graph, nodeIds, errors);
            }

            if (nodesValid && edgesValid &&
                !string.IsNullOrWhiteSpace(graph.EntryNodeId) &&
                nodeIds.Contains(graph.EntryNodeId))
            {
                DetectUnreachableNodesWithOwnership(graph, graph.EntryNodeId, nodeIds, errors);
            }

            return errors.Count == 0
                ? GraphValidationResult.Ok()
                : GraphValidationResult.Fail(errors);
        }

        /// <summary>Runtime gate used by the Phase 1 runner after authoring validation.</summary>
        public static GraphValidationResult ValidateForExecution(LessonGraph graph)
        {
            var authoring = Validate(graph);
            if (!authoring.IsValid) return authoring;
            var errors = new List<GraphValidationError>();
            foreach (var node in graph.Nodes)
            {
                if (node != null && (node.NodeType == NodeType.Timeline || node.NodeType == NodeType.Parallel ||
                    node.NodeType == NodeType.Gate || node.NodeType == NodeType.Loop))
                    errors.Add(new GraphValidationError(GraphValidationErrorCode.UnsupportedExecutionFeature,
                        $"Node type '{node.NodeType}' is authoring-valid but is not supported by the Phase 1 runner.", node.Id));
            }
            foreach (var edge in graph.Edges)
                if (edge?.Condition is VariableCondition || edge?.Condition is CompositeCondition)
                    errors.Add(new GraphValidationError(GraphValidationErrorCode.UnsupportedExecutionFeature,
                        "Variable and composite conditions are not supported by the Phase 1 runner."));
            return errors.Count == 0 ? GraphValidationResult.Ok() : GraphValidationResult.Fail(errors);
        }

        private static void ValidateTimelineConfig(TimelineNodeConfig config, string nodeId, List<GraphValidationError> errors)
        {
            if (config.TimelineAsset == null || string.IsNullOrWhiteSpace(config.ExpectedSignalName) ||
                float.IsNaN(config.TimeoutSeconds) || float.IsInfinity(config.TimeoutSeconds) || config.TimeoutSeconds <= 0 ||
                (config.TimeoutOutcome != TimelineTimeoutOutcome.Timeout && config.TimeoutOutcome != TimelineTimeoutOutcome.Failed))
                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidTimelineConfig,
                    "Timeline requires a PlayableAsset, expected signal name, finite positive timeout, and Timeout or Failed outcome.", nodeId));
        }

        private static void ValidateVariableCondition(VariableCondition condition, List<GraphValidationError> errors, string nodeId)
        {
            bool invalid = string.IsNullOrWhiteSpace(condition.VariableName) ||
                !Enum.IsDefined(typeof(VariableValueType), condition.ValueType) ||
                !Enum.IsDefined(typeof(VariableComparisonOperator), condition.Operator);
            var ordering = condition.Operator == VariableComparisonOperator.LessThan ||
                condition.Operator == VariableComparisonOperator.LessThanOrEqual ||
                condition.Operator == VariableComparisonOperator.GreaterThan ||
                condition.Operator == VariableComparisonOperator.GreaterThanOrEqual;
            if ((condition.ValueType == VariableValueType.Boolean || condition.ValueType == VariableValueType.String) && ordering)
                invalid = true;
            if (condition.ValueType == VariableValueType.Float &&
                (float.IsNaN(condition.FloatValue) || float.IsInfinity(condition.FloatValue))) invalid = true;
            if (condition.ValueType == VariableValueType.String && condition.StringValue == null) invalid = true;
            if (invalid)
                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidVariableCondition,
                    "Variable condition has a blank name, unsupported type/operator, invalid ordering operator, or non-finite value.", nodeId));
        }

        private static void ValidateCondition(IEdgeCondition condition, int schemaVersion, string nodeId,
            List<GraphValidationError> errors, HashSet<IEdgeCondition> path, int depth, GraphValidationErrorCode code)
        {
            if (condition == null)
            {
                errors.Add(new GraphValidationError(code, "Condition tree contains a null child.", nodeId));
                return;
            }
            if (condition is CompositeCondition && depth > 16)
            {
                errors.Add(new GraphValidationError(code, "Composite conditions may not nest deeper than 16 levels.", nodeId));
                return;
            }
            if (!path.Add(condition))
            {
                errors.Add(new GraphValidationError(code, "Condition tree contains a reference cycle.", nodeId));
                return;
            }
            if (condition is VariableCondition variable)
                ValidateVariableCondition(variable, errors, nodeId);
            else if (condition is CompositeCondition composite)
            {
                if (schemaVersion < 2 || !Enum.IsDefined(typeof(CompositeConditionOperator), composite.Operator) ||
                    composite.Conditions == null || composite.Conditions.Count == 0)
                    errors.Add(new GraphValidationError(code, "Composite condition requires schema 2, a valid operator, and nonempty children.", nodeId));
                else
                    foreach (var child in composite.Conditions)
                        ValidateCondition(child, schemaVersion, nodeId, errors, path, depth + 1, code);
            }
            else if (condition is StatusCondition status && !s_validStatuses.Contains(status.RequiredStatus))
                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidStatusValue,
                    $"StatusCondition has invalid requiredStatus '{status.RequiredStatus}'.", nodeId));
            else if (condition.GetType() != typeof(AlwaysCondition) && condition.GetType() != typeof(StatusCondition))
                errors.Add(new GraphValidationError(code, $"Unsupported condition type '{condition.GetType().Name}'.", nodeId));
            path.Remove(condition);
        }

        private static void ValidateStructuredReferences(LessonGraph graph, HashSet<string> ids, List<GraphValidationError> errors)
        {
            var byId = new Dictionary<string, LessonNodeData>();
            foreach (var n in graph.Nodes)
                if (n != null && !string.IsNullOrWhiteSpace(n.Id) && !byId.ContainsKey(n.Id)) byId.Add(n.Id, n);
            var outgoing = new Dictionary<string, List<string>>();
            foreach (var id in ids) outgoing[id] = new List<string>();
            foreach (var edge in graph.Edges)
                if (edge != null && !string.IsNullOrWhiteSpace(edge.FromNodeId) && !string.IsNullOrWhiteSpace(edge.ToNodeId) &&
                    ids.Contains(edge.FromNodeId) && ids.Contains(edge.ToNodeId)) outgoing[edge.FromNodeId].Add(edge.ToNodeId);
            var checkpointIds = new HashSet<string>();
            foreach (var node in graph.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id)) continue;
                if (graph.SchemaVersion == 2 && node.Config is CheckpointNodeConfig checkpoint && !string.IsNullOrWhiteSpace(checkpoint.CheckpointId) &&
                    !checkpointIds.Add(checkpoint.CheckpointId))
                    errors.Add(new GraphValidationError(GraphValidationErrorCode.DuplicateCheckpointId,
                        $"Checkpoint ID '{checkpoint.CheckpointId}' is used more than once.", node.Id));
                if (node.Config is ParallelNodeConfig parallel)
                {
                    var branches = parallel.Branches;
                    var branchIds = new HashSet<string>();
                    var childIds = new HashSet<string>();
                    if (branches == null) continue;
                    foreach (var branch in branches)
                    {
                        if (branch == null || string.IsNullOrWhiteSpace(branch.BranchId) || branchIds.Contains(branch.BranchId) ||
                            string.IsNullOrWhiteSpace(branch.ChildNodeId) || childIds.Contains(branch.ChildNodeId) ||
                            branch.ChildNodeId == node.Id || !byId.ContainsKey(branch.ChildNodeId))
                        {
                            errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidParallelConfig,
                                "Parallel branch IDs must be unique and nonblank, and child IDs must be unique existing nodes.", node.Id));
                            continue;
                        }
                        branchIds.Add(branch.BranchId);
                        childIds.Add(branch.ChildNodeId);
                        if (outgoing[branch.ChildNodeId].Count > 0)
                            errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidParallelConfig,
                                $"Parallel child '{branch.ChildNodeId}' cannot have ordinary outgoing edges.", branch.ChildNodeId));
                    }
                    if (string.IsNullOrWhiteSpace(parallel.GateNodeId) || !byId.TryGetValue(parallel.GateNodeId, out var gateNode) || gateNode.NodeType != NodeType.Gate ||
                        !(gateNode.Config is GateNodeConfig gate) || gate.ParallelNodeId != node.Id)
                    {
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                            "Parallel must reference a Gate whose ownerParallelNodeId matches this node.", node.Id));
                        continue;
                    }
                    if (!graph.Edges.Any(e => e != null && e.FromNodeId == node.Id && e.ToNodeId == parallel.GateNodeId))
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                            "A Parallel-to-Gate edge is required.", node.Id));
                    var gateInputs = new HashSet<string>();
                    if (gate.CompletedBranchIds == null) continue;
                    foreach (var branchId in gate.CompletedBranchIds)
                        if (string.IsNullOrWhiteSpace(branchId) || !gateInputs.Add(branchId) || !branchIds.Contains(branchId))
                            errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                                "Gate branch inputs must be unique IDs belonging to the owning Parallel.", gateNode.Id));
                    if (gateInputs.Count == 0 || branchIds.Any(id => !gateInputs.Contains(id)))
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                            "Gate inputs must include each branch owned by its Parallel.", gateNode.Id));
                    if (parallel.JoinPolicy == ParallelJoinPolicy.FirstCompleted && gate.Mode == GateConditionMode.And)
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.ParallelGateDeadlock,
                            "FirstCompleted cannot use an And gate because cancelled branches cannot complete it.", node.Id));
                    foreach (var childId in childIds)
                    {
                        var pending = new Queue<string>(); var seen = new HashSet<string>(); pending.Enqueue(childId);
                        while (pending.Count > 0)
                        {
                            var current = pending.Dequeue();
                            if (!seen.Add(current) || current == parallel.GateNodeId) continue;
                            if (byId.TryGetValue(current, out var pathNode) && pathNode.Config is CheckpointNodeConfig)
                                errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidCheckpointPlacement,
                                    "Checkpoints are only valid after the Parallel-to-Gate join boundary.", current));
                            foreach (var next in outgoing[current]) pending.Enqueue(next);
                        }
                    }
                    var joinPath = new Queue<string>();
                    var joinVisited = new HashSet<string>();
                    foreach (var next in outgoing[node.Id]) joinPath.Enqueue(next);
                    while (joinPath.Count > 0)
                    {
                        var current = joinPath.Dequeue();
                        if (!joinVisited.Add(current) || current == parallel.GateNodeId) continue;
                        if (byId.TryGetValue(current, out var pathNode) && pathNode.Config is CheckpointNodeConfig)
                            errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidCheckpointPlacement,
                                "Checkpoints are only valid after the Parallel-to-Gate join boundary.", current));
                        foreach (var next in outgoing[current]) joinPath.Enqueue(next);
                    }
                }
                if (node.Config is GateNodeConfig gateConfig &&
                    (string.IsNullOrWhiteSpace(gateConfig.ParallelNodeId) || !byId.TryGetValue(gateConfig.ParallelNodeId, out var owner) || owner.NodeType != NodeType.Parallel ||
                     !(owner.Config is ParallelNodeConfig ownerConfig) || ownerConfig.GateNodeId != node.Id))
                    errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidGateConfig,
                        "Gate owner must be a Parallel that references this Gate.", node.Id));
                if (node.Config is LoopNodeConfig loop)
                {
                    if (string.IsNullOrWhiteSpace(loop.BodyChildNodeId) || string.IsNullOrWhiteSpace(loop.ExitNodeId) ||
                        !byId.ContainsKey(loop.BodyChildNodeId) || !byId.ContainsKey(loop.ExitNodeId))
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidLoopConfig,
                            "Loop body and exit IDs must reference existing nodes.", node.Id));
                    if (!string.IsNullOrWhiteSpace(loop.BodyChildNodeId) && byId.ContainsKey(loop.BodyChildNodeId) &&
                        outgoing[loop.BodyChildNodeId].Count > 0)
                        errors.Add(new GraphValidationError(GraphValidationErrorCode.InvalidLoopConfig,
                            "Loop body child cannot have ordinary outgoing edges.", loop.BodyChildNodeId));
                }
            }
        }

        private static void DetectUnreachableNodesWithOwnership(LessonGraph graph, string entryNodeId,
            HashSet<string> allNodeIds, List<GraphValidationError> errors)
        {
            var adjacency = new Dictionary<string, List<string>>();
            foreach (var id in allNodeIds) adjacency[id] = new List<string>();
            foreach (var edge in graph.Edges)
                if (edge != null && !string.IsNullOrWhiteSpace(edge.FromNodeId) && !string.IsNullOrWhiteSpace(edge.ToNodeId) &&
                    adjacency.ContainsKey(edge.FromNodeId) && adjacency.ContainsKey(edge.ToNodeId))
                    adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            foreach (var node in graph.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id)) continue;
                if (node.Config is ParallelNodeConfig parallel && adjacency.ContainsKey(node.Id))
                {
                    if (!string.IsNullOrWhiteSpace(parallel.GateNodeId) && adjacency.ContainsKey(parallel.GateNodeId))
                        adjacency[node.Id].Add(parallel.GateNodeId);
                    if (parallel.Branches != null) foreach (var branch in parallel.Branches)
                        if (branch != null && !string.IsNullOrWhiteSpace(branch.ChildNodeId) && adjacency.ContainsKey(branch.ChildNodeId))
                            adjacency[node.Id].Add(branch.ChildNodeId);
                }
                else if (node.Config is LoopNodeConfig loop && adjacency.ContainsKey(node.Id))
                {
                    if (!string.IsNullOrWhiteSpace(loop.BodyChildNodeId) && adjacency.ContainsKey(loop.BodyChildNodeId))
                        adjacency[node.Id].Add(loop.BodyChildNodeId);
                    if (!string.IsNullOrWhiteSpace(loop.ExitNodeId) && adjacency.ContainsKey(loop.ExitNodeId))
                        adjacency[node.Id].Add(loop.ExitNodeId);
                }
            }
            var reachable = new HashSet<string> { entryNodeId };
            var pending = new Queue<string>(); pending.Enqueue(entryNodeId);
            while (pending.Count > 0)
                foreach (var next in adjacency[pending.Dequeue()]) if (reachable.Add(next)) pending.Enqueue(next);
            foreach (var id in allNodeIds.OrderBy(id => id, StringComparer.Ordinal))
                if (!reachable.Contains(id)) errors.Add(new GraphValidationError(GraphValidationErrorCode.UnreachableNode,
                    $"Node '{id}' is unreachable from EntryNodeId '{entryNodeId}'.", id));
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

            if (string.IsNullOrWhiteSpace(config.NpcBindingId))
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.InvalidDialogueConfig,
                    "DialogueNodeConfig.npcBindingId must not be empty or whitespace.",
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

        private static void DetectUnreachableNodes(
            LessonGraph graph,
            string entryNodeId,
            HashSet<string> allNodeIds,
            List<GraphValidationError> errors)
        {
            var adjacency = new Dictionary<string, List<string>>(allNodeIds.Count);
            foreach (var id in allNodeIds)
                adjacency[id] = new List<string>();

            foreach (var edge in graph.Edges)
            {
                if (edge == null ||
                    string.IsNullOrWhiteSpace(edge.FromNodeId) ||
                    string.IsNullOrWhiteSpace(edge.ToNodeId))
                    continue;

                if (!adjacency.TryGetValue(edge.FromNodeId, out var neighbors) ||
                    !allNodeIds.Contains(edge.ToNodeId))
                    continue;

                neighbors.Add(edge.ToNodeId);
            }

            var reachable = new HashSet<string> { entryNodeId };
            var pending = new Queue<string>();
            pending.Enqueue(entryNodeId);
            while (pending.Count > 0)
            {
                var nodeId = pending.Dequeue();
                foreach (var neighbor in adjacency[nodeId])
                {
                    if (reachable.Add(neighbor))
                        pending.Enqueue(neighbor);
                }
            }

            var unreachable = new List<string>();
            foreach (var nodeId in allNodeIds)
                if (!reachable.Contains(nodeId))
                    unreachable.Add(nodeId);
            unreachable.Sort(StringComparer.Ordinal);

            foreach (var nodeId in unreachable)
            {
                errors.Add(new GraphValidationError(
                    GraphValidationErrorCode.UnreachableNode,
                    $"Node \"{nodeId}\" is unreachable from EntryNodeId \"{entryNodeId}\".",
                    nodeId));
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
