using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public sealed class StructuredBranchEvidence
    {
        private readonly ReadOnlyDictionary<string, NodeResult> _results;
        public string ParallelNodeId { get; }
        public IReadOnlyDictionary<string, NodeResult> Results => _results;
        public int Count => _results.Count;

        internal StructuredBranchEvidence(string parallelNodeId, IDictionary<string, NodeResult> results)
        {
            ParallelNodeId = parallelNodeId;
            _results = new ReadOnlyDictionary<string, NodeResult>(new Dictionary<string, NodeResult>(results, StringComparer.Ordinal));
        }
    }

    public sealed class StructuredFlowResult
    {
        public NodeResult ParentResult { get; }
        public StructuredBranchEvidence BranchEvidence { get; }
        internal StructuredFlowResult(NodeResult parentResult, StructuredBranchEvidence branchEvidence)
        {
            ParentResult = parentResult;
            BranchEvidence = branchEvidence;
        }
    }

    public sealed class StructuredFlowExecution
    {
        private readonly LessonGraph _graph;
        private readonly INodeExecutorRegistry _registry;
        private readonly INodeClock _clock;
        private readonly ILessonVariableSource _variables;
        private readonly ICheckpointTelemetry _telemetry;
        private readonly Action<string> _visitNode;
        private readonly Action<string, string> _visitEdge;

        public StructuredFlowExecution(LessonGraph graph, INodeExecutorRegistry registry, INodeClock clock,
            ILessonVariableSource variables, ICheckpointTelemetry telemetry, Action<string> visitNode,
            Action<string, string> visitEdge = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _clock = clock;
            _variables = variables;
            _telemetry = telemetry;
            _visitNode = visitNode;
            _visitEdge = visitEdge;
        }

        public async Task<StructuredFlowResult> ExecuteParallelAsync(NodeExecutionContext context)
        {
            if (!(context.Node.Config is ParallelNodeConfig config) || config.Branches == null || config.Branches.Count == 0)
                return FailedParent(context, "invalid_parallel");

            var branchIds = new HashSet<string>(StringComparer.Ordinal);
            var childIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var branch in config.Branches)
                if (branch == null || string.IsNullOrWhiteSpace(branch.BranchId) || string.IsNullOrWhiteSpace(branch.ChildNodeId) ||
                    !branchIds.Add(branch.BranchId) || !childIds.Add(branch.ChildNodeId))
                    return FailedParent(context, "invalid_parallel_branches");

            var results = new Dictionary<string, NodeResult>(StringComparer.Ordinal);
            using (var owner = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                var active = new List<BranchTask>();
                foreach (var branch in config.Branches)
                {
                    var child = FindNode(branch.ChildNodeId);
                    if (child == null)
                    {
                        Cancel(owner, active);
                        Observe(active);
                        return FailedParent(context, "missing_child");
                    }
                    _visitEdge?.Invoke(context.Node.Id, child.Id);
                    var scope = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
                    active.Add(new BranchTask(branch.BranchId, child, scope, ExecuteChildAsync(context, child, scope)));
                }

                try
                {
                    while (active.Count > 0)
                    {
                        using (var signals = new CancellationSignals(context.SkipToken, context.TimeoutToken, context.CancellationToken))
                        {
                            var pending = active.Select(branch => (Task)branch.Task).Concat(signals.Tasks).ToArray();
                            var completed = await Task.WhenAny(pending);
                            if (context.CancellationToken.IsCancellationRequested)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                throw new OperationCanceledException(context.CancellationToken);
                            }
                            if (context.SkipToken.IsCancellationRequested)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                var evidence = new StructuredBranchEvidence(context.Node.Id, results);
                                return new StructuredFlowResult(ParentResult(context, NodeStatus.Skipped, "skip"), evidence);
                            }
                            if (context.TimeoutToken.IsCancellationRequested)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                var evidence = new StructuredBranchEvidence(context.Node.Id, results);
                                return new StructuredFlowResult(ParentResult(context, NodeStatus.Timeout, "timeout"), evidence);
                            }

                            var branchTask = active.FirstOrDefault(branch => ReferenceEquals(branch.Task, completed));
                            if (branchTask == null) continue;
                            active.Remove(branchTask);
                            branchTask.Scope.Dispose();
                            var outcome = await branchTask.Task;
                            if (context.CancellationToken.IsCancellationRequested)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                throw new OperationCanceledException(context.CancellationToken);
                            }
                            if (context.SkipToken.IsCancellationRequested || context.TimeoutToken.IsCancellationRequested)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                var status = context.SkipToken.IsCancellationRequested ? NodeStatus.Skipped : NodeStatus.Timeout;
                                var channel = status == NodeStatus.Skipped ? "skip" : "timeout";
                                return new StructuredFlowResult(ParentResult(context, status, channel),
                                    new StructuredBranchEvidence(context.Node.Id, results));
                            }
                            var branchResult = outcome.IsValid
                                ? outcome.Result
                                : NodeResult.Completed(branchTask.Node.Id, outcome.ActivationId, NodeStatus.Failed,
                                    CurrentTime(context), outcome.Error ?? "invalid_result");
                            if (outcome.IsValid) results[branchTask.BranchId] = branchResult;

                            if (config.JoinPolicy == ParallelJoinPolicy.FirstCompleted)
                            {
                                if (!outcome.IsValid) continue;
                                Cancel(owner, active);
                                Observe(active);
                                var evidence = new StructuredBranchEvidence(context.Node.Id, results);
                                return new StructuredFlowResult(ParentResult(context, branchResult.Status, "parallel_first_completed"), evidence);
                            }
                            if (!outcome.IsValid || branchResult.Status != NodeStatus.Success)
                            {
                                Cancel(owner, active);
                                Observe(active);
                                var evidence = new StructuredBranchEvidence(context.Node.Id, results);
                                return new StructuredFlowResult(ParentResult(context, NodeStatus.Failed, "parallel_branch_failed"), evidence);
                            }
                        }
                    }
                }
                catch
                {
                    Cancel(owner, active);
                    Observe(active);
                    throw;
                }
            }

            var completedEvidence = new StructuredBranchEvidence(context.Node.Id, results);
            if (config.JoinPolicy == ParallelJoinPolicy.FirstCompleted)
                return new StructuredFlowResult(ParentResult(context, NodeStatus.Failed, "parallel_no_valid_completion"), completedEvidence);
            return new StructuredFlowResult(ParentResult(context, NodeStatus.Success, "parallel_all_success"), completedEvidence);
        }

        public NodeResult ExecuteGate(NodeExecutionContext context, StructuredBranchEvidence evidence)
        {
            if (!(context.Node.Config is GateNodeConfig config) || evidence == null ||
                !string.Equals(evidence.ParallelNodeId, config.ParallelNodeId, StringComparison.Ordinal) ||
                config.CompletedBranchIds == null || config.CompletedBranchIds.Count == 0 ||
                !Enum.IsDefined(typeof(GateConditionMode), config.Mode))
                return ParentResult(context, NodeStatus.Failed, "invalid_gate_evidence");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var anySuccess = false;
            foreach (var branchId in config.CompletedBranchIds)
            {
                if (string.IsNullOrWhiteSpace(branchId) || !seen.Add(branchId))
                    return ParentResult(context, NodeStatus.Failed, "invalid_gate_inputs");
                if (!evidence.Results.TryGetValue(branchId, out var result) || result == null)
                {
                    if (config.Mode == GateConditionMode.And)
                        return ParentResult(context, NodeStatus.Failed, "missing_gate_result");
                    continue;
                }
                if (result.Status == NodeStatus.Success) anySuccess = true;
                else if (config.Mode == GateConditionMode.And)
                    return ParentResult(context, NodeStatus.Failed, "gate_branch_failed");
            }

            bool success = config.Mode == GateConditionMode.And ? seen.Count > 0 : anySuccess;
            return ParentResult(context, success ? NodeStatus.Success : NodeStatus.Failed, success ? "gate_satisfied" : "gate_unsatisfied");
        }

        public async Task<StructuredFlowResult> ExecuteLoopAsync(NodeExecutionContext context)
        {
            if (!(context.Node.Config is LoopNodeConfig config) || config.MaximumIterations <= 0 ||
                string.IsNullOrWhiteSpace(config.BodyChildNodeId) || string.IsNullOrWhiteSpace(config.ExitNodeId))
                return FailedParent(context, "invalid_loop");

            var body = FindNode(config.BodyChildNodeId);
            if (body == null) return FailedParent(context, "missing_loop_body");
            using (var owner = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                for (var iteration = 0; iteration < config.MaximumIterations; iteration++)
                {
                    _visitEdge?.Invoke(context.Node.Id, body.Id);
                    var scope = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
                    var childTask = ExecuteChildAsync(context, body, scope);
                    using (var signals = new CancellationSignals(context.SkipToken, context.TimeoutToken, context.CancellationToken))
                    {
                        var completed = await Task.WhenAny(childTask, signals.Tasks[0], signals.Tasks[1], signals.Tasks[2]);
                        if (context.CancellationToken.IsCancellationRequested)
                        {
                            TryCancel(owner);
                            scope.Dispose();
                            Observe(childTask);
                            throw new OperationCanceledException(context.CancellationToken);
                        }
                        if (context.SkipToken.IsCancellationRequested || context.TimeoutToken.IsCancellationRequested)
                        {
                            TryCancel(owner);
                            scope.Dispose();
                            Observe(childTask);
                            var status = context.SkipToken.IsCancellationRequested ? NodeStatus.Skipped : NodeStatus.Timeout;
                            var channel = status == NodeStatus.Skipped ? "skip" : "timeout";
                            return new StructuredFlowResult(ParentResult(context, status, channel), null);
                        }
                        if (!ReferenceEquals(completed, childTask)) continue;
                    }
                    scope.Dispose();
                    var outcome = await childTask;
                    if (context.CancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(context.CancellationToken);
                    if (context.SkipToken.IsCancellationRequested || context.TimeoutToken.IsCancellationRequested)
                    {
                        TryCancel(owner);
                        var status = context.SkipToken.IsCancellationRequested ? NodeStatus.Skipped : NodeStatus.Timeout;
                        var channel = status == NodeStatus.Skipped ? "skip" : "timeout";
                        return new StructuredFlowResult(ParentResult(context, status, channel), null);
                    }
                    if (!outcome.IsValid)
                        return new StructuredFlowResult(ParentResult(context, NodeStatus.Failed, "invalid_loop_child_result"), null);
                    if (LessonConditionEvaluator.Evaluate(config.ExitCondition, outcome.Result.Status, _variables))
                        return new StructuredFlowResult(ParentResult(context, NodeStatus.Success, "loop_exit"), null);
                }
            }
            return new StructuredFlowResult(ParentResult(context, NodeStatus.Failed, "loop_limit"), null);
        }

        private async Task<ChildOutcome> ExecuteChildAsync(NodeExecutionContext parent, LessonNodeData child, CancellationTokenSource scope)
        {
            var activationId = Guid.NewGuid().ToString("N");
            try
            {
                _visitNode?.Invoke(child.Id);
                if (!_registry.TryGet(child.NodeType, out var executor) || executor == null)
                    return ChildOutcome.Invalid(activationId, "missing_executor");
                var context = new NodeExecutionContext(parent.RunId, activationId, parent.GraphId, child, CurrentTime(parent),
                    scope.Token, parent.SkipToken, parent.TimeoutToken, _telemetry ?? parent.CheckpointTelemetry, _clock ?? parent.Clock);
                var task = executor.ExecuteAsync(context);
                if (task == null) return ChildOutcome.Invalid(activationId, "null_task");
                var result = await task;
                if (scope.IsCancellationRequested) return ChildOutcome.Invalid(activationId, "cancelled");
                if (result == null || result.NodeId != child.Id || result.ActivationId != activationId)
                    return ChildOutcome.Invalid(activationId, "invalid_child_result");
                return ChildOutcome.Valid(result, activationId);
            }
            catch (OperationCanceledException) when (scope.IsCancellationRequested)
            {
                return ChildOutcome.Invalid(activationId, "cancelled");
            }
            catch (Exception exception)
            {
                return ChildOutcome.Invalid(activationId, "child_exception:" + exception.GetType().Name);
            }
        }

        private LessonNodeData FindNode(string id) => _graph.Nodes.FirstOrDefault(node => node != null && node.Id == id);
        private double CurrentTime(NodeExecutionContext context) => (_clock ?? context.Clock)?.ElapsedSeconds ?? context.ElapsedSeconds;
        private NodeResult ParentResult(NodeExecutionContext context, NodeStatus status, string channel) =>
            NodeResult.Completed(context.Node.Id, context.ActivationId, status, CurrentTime(context), channel);
        private StructuredFlowResult FailedParent(NodeExecutionContext context, string channel) =>
            new StructuredFlowResult(ParentResult(context, NodeStatus.Failed, channel), null);

        private static void Cancel(CancellationTokenSource owner, IEnumerable<BranchTask> branches)
        {
            TryCancel(owner);
            foreach (var branch in branches) TryCancel(branch.Scope);
        }

        private static void TryCancel(CancellationTokenSource source)
        {
            if (source == null || source.IsCancellationRequested) return;
            try { source.Cancel(); }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }
        }

        private static void Observe(IEnumerable<BranchTask> branches)
        {
            foreach (var branch in branches)
            {
                Observe(branch.Task);
                branch.Scope.Dispose();
            }
        }

        private static void Observe(Task task)
        {
            if (task == null) return;
            task.ContinueWith(completed => { var ignored = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private sealed class BranchTask
        {
            public string BranchId { get; }
            public LessonNodeData Node { get; }
            public CancellationTokenSource Scope { get; }
            public Task<ChildOutcome> Task { get; }
            public BranchTask(string branchId, LessonNodeData node, CancellationTokenSource scope, Task<ChildOutcome> task)
            { BranchId = branchId; Node = node; Scope = scope; Task = task; }
        }

        private sealed class ChildOutcome
        {
            public NodeResult Result { get; }
            public string ActivationId { get; }
            public string Error { get; }
            public bool IsValid => Result != null;
            private ChildOutcome(NodeResult result, string activationId, string error)
            { Result = result; ActivationId = activationId; Error = error; }
            public static ChildOutcome Valid(NodeResult result, string activationId) => new ChildOutcome(result, activationId, null);
            public static ChildOutcome Invalid(string activationId, string error) => new ChildOutcome(null, activationId, error);
        }

        private sealed class CancellationSignals : IDisposable
        {
            private readonly CancellationTokenRegistration[] _registrations;
            public Task[] Tasks { get; }
            public CancellationSignals(params CancellationToken[] tokens)
            {
                var completions = tokens.Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
                Tasks = completions.Select(completion => completion.Task).ToArray();
                _registrations = tokens.Select((token, index) => token.Register(() => completions[index].TrySetResult(true))).ToArray();
                for (var i = 0; i < tokens.Length; i++) if (tokens[i].IsCancellationRequested) completions[i].TrySetResult(true);
            }
            public void Dispose() { foreach (var registration in _registrations) registration.Dispose(); }
        }
    }
}
