using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class StructuredFlowExecutionTests
    {
        [UnityTest]
        public IEnumerator ExecuteParallel_AllSuccessCollectsNamedResultsAndVisitsEveryBranch()
        {
            var graph = CreateGraph(
                new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                    new[] { new ParallelBranch("left", "left-node"), new ParallelBranch("right", "right-node") }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("left-node", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("right-node", NodeType.Dialogue, new DialogueNodeConfig("seq", "text", "npc")),
                new LessonNodeData("gate", NodeType.Gate, new GateNodeConfig("parallel", new[] { "left", "right" }, GateConditionMode.And)));
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var visited = new List<string>();
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context => Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, 0)));
                registry.Set(NodeType.Dialogue, context => Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, 0)));
                var flow = new StructuredFlowExecution(graph, registry, clock, null, null, visited.Add);
                var task = flow.ExecuteParallelAsync(Context(graph, "parallel", clock));
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();

                Assert.AreEqual(NodeStatus.Success, result.ParentResult.Status);
                Assert.AreEqual(NodeStatus.Success, flow.ExecuteGate(Context(graph, "gate", clock), result.BranchEvidence).Status);
                CollectionAssert.AreEquivalent(new[] { "left-node", "right-node" }, visited);
                Assert.AreEqual(2, result.BranchEvidence.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(graph); }
        }

        [UnityTest]
        public IEnumerator ExecuteParallel_FirstCompletedCancelsLoserAndLateCompletionCannotChangeEvidence()
        {
            var graph = CreateGraph(
                new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                    new[] { new ParallelBranch("winner", "winner-node"), new ParallelBranch("late", "late-node") }, "gate", ParallelJoinPolicy.FirstCompleted)),
                new LessonNodeData("winner-node", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("late-node", NodeType.Dialogue, new DialogueNodeConfig("seq", "text", "npc")),
                new LessonNodeData("gate", NodeType.Gate, new GateNodeConfig("parallel", new[] { "winner", "late" }, GateConditionMode.Or)));
            var lateCompletion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loserCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context => Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, 0)));
                registry.Set(NodeType.Dialogue, context =>
                {
                    context.CancellationToken.Register(() => loserCancelled.TrySetResult(true));
                    return lateCompletion.Task;
                });
                var flow = new StructuredFlowExecution(graph, registry, clock, null, null, _ => { });
                var task = flow.ExecuteParallelAsync(Context(graph, "parallel", clock));
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();
                yield return CompleteWithinFrames(loserCancelled.Task);
                Assert.AreEqual(NodeStatus.Success, result.ParentResult.Status);
                Assert.AreEqual(1, result.BranchEvidence.Count);

                lateCompletion.TrySetResult(NodeResult.Completed("late-node", "late-activation", NodeStatus.Success, 0));
                yield return null;
                Assert.AreEqual(1, result.BranchEvidence.Count, "A completion after cancellation must not mutate the join evidence.");
            }
            finally
            {
                lateCompletion.TrySetCanceled();
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator ExecuteParallel_AllSuccessFailureCancelsSiblingAndFailsJoin()
        {
            var graph = CreateGraph(
                new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                    new[] { new ParallelBranch("failed", "failed-node"), new ParallelBranch("pending", "pending-node") }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("failed-node", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("pending-node", NodeType.Dialogue, new DialogueNodeConfig("seq", "text", "npc")),
                new LessonNodeData("gate", NodeType.Gate, new GateNodeConfig("parallel", new[] { "failed", "pending" }, GateConditionMode.Or)));
            var pendingCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingCompletion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context => Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Failed, 0)));
                registry.Set(NodeType.Dialogue, context =>
                {
                    context.CancellationToken.Register(() => pendingCancelled.TrySetResult(true));
                    return pendingCompletion.Task;
                });
                var flow = new StructuredFlowExecution(graph, registry, clock, null, null, _ => { });
                var task = flow.ExecuteParallelAsync(Context(graph, "parallel", clock));
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();
                yield return CompleteWithinFrames(pendingCancelled.Task);
                Assert.AreEqual(NodeStatus.Failed, result.ParentResult.Status);
                Assert.AreEqual(NodeStatus.Failed, flow.ExecuteGate(Context(graph, "gate", clock), result.BranchEvidence).Status);
            }
            finally
            {
                pendingCompletion.TrySetCanceled();
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator ExecuteParallel_FirstCompletedIgnoresWrongChildResultAndAcceptsLaterValidBranch()
        {
            var graph = CreateGraph(
                new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                    new[] { new ParallelBranch("wrong", "wrong-node"), new ParallelBranch("good", "good-node") }, "gate", ParallelJoinPolicy.FirstCompleted)),
                new LessonNodeData("wrong-node", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("good-node", NodeType.Dialogue, new DialogueNodeConfig("seq", "text", "npc")),
                new LessonNodeData("gate", NodeType.Gate, new GateNodeConfig("parallel", new[] { "wrong", "good" }, GateConditionMode.Or)));
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context => Task.FromResult(NodeResult.Completed(
                    "some-other-node", context.ActivationId, NodeStatus.Success, 0)));
                registry.Set(NodeType.Dialogue, context => Task.FromResult(NodeResult.Completed(
                    context.Node.Id, context.ActivationId, NodeStatus.Success, 0)));
                var flow = new StructuredFlowExecution(graph, registry, clock, null, null, _ => { });
                var task = flow.ExecuteParallelAsync(Context(graph, "parallel", clock));
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();

                Assert.AreEqual(NodeStatus.Success, result.ParentResult.Status);
                Assert.AreEqual(1, result.BranchEvidence.Count);
                Assert.IsTrue(result.BranchEvidence.Results.ContainsKey("good"));
                Assert.IsFalse(result.BranchEvidence.Results.ContainsKey("wrong"));
                Assert.AreEqual(NodeStatus.Success, flow.ExecuteGate(Context(graph, "gate", clock), result.BranchEvidence).Status);
            }
            finally { UnityEngine.Object.DestroyImmediate(graph); }
        }

        [UnityTest]
        public IEnumerator ExecuteLoop_ExitsAfterConditionAndDoesNotStartAnotherBody()
        {
            var graph = CreateGraph(
                new LessonNodeData("loop", NodeType.Loop, new LoopNodeConfig("body", "exit", 3,
                    new VariableCondition("done", VariableValueType.Boolean, VariableComparisonOperator.Equal, true))),
                new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)));
            var executions = 0;
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var source = new MutableVariableSource();
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context =>
                {
                    executions++;
                    source.Done = executions == 2;
                    return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, 0));
                });
                var flow = new StructuredFlowExecution(graph, registry, clock, source, null, _ => { });
                var task = flow.ExecuteLoopAsync(Context(graph, "loop", clock));
                yield return CompleteWithinFrames(task);

                Assert.AreEqual(NodeStatus.Success, task.GetAwaiter().GetResult().ParentResult.Status);
                Assert.AreEqual(2, executions);
            }
            finally { UnityEngine.Object.DestroyImmediate(graph); }
        }

        [UnityTest]
        public IEnumerator ExecuteLoop_FailsAtExactLimitWithoutStartingExtraBody()
        {
            var graph = CreateGraph(
                new LessonNodeData("loop", NodeType.Loop, new LoopNodeConfig("body", "exit", 2,
                    new VariableCondition("done", VariableValueType.Boolean, VariableComparisonOperator.Equal, true))),
                new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)));
            var executions = 0;
            var bodyVisits = new List<string>();
            var registry = new ResultRegistry(_ => Task.FromResult<NodeResult>(null));
            var clock = new TestClock();
            try
            {
                registry.Set(NodeType.Wait, context =>
                {
                    if (context.Node.Id == "body") executions++;
                    return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, 0));
                });
                var flow = new StructuredFlowExecution(graph, registry, clock, new MutableVariableSource(), null,
                    bodyVisits.Add);
                var task = flow.ExecuteLoopAsync(Context(graph, "loop", clock));
                yield return CompleteWithinFrames(task);

                Assert.AreEqual(NodeStatus.Failed, task.GetAwaiter().GetResult().ParentResult.Status);
                Assert.AreEqual(2, executions);
                CollectionAssert.AreEqual(new[] { "body", "body" }, bodyVisits);
            }
            finally { UnityEngine.Object.DestroyImmediate(graph); }
        }

        private static LessonGraph CreateGraph(params LessonNodeData[] nodes)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId(nodes[0].Id);
            graph.Editor_SetNodes(new List<LessonNodeData>(nodes));
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            return graph;
        }

        private static NodeExecutionContext Context(LessonGraph graph, string nodeId, INodeClock clock)
        {
            LessonNodeData node = null;
            foreach (var candidate in graph.Nodes) if (candidate.Id == nodeId) node = candidate;
            return new NodeExecutionContext("run", "parent", graph.name, node, 0, CancellationToken.None,
                CancellationToken.None, CancellationToken.None, null, clock);
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Structured flow task did not complete within 30 editor frames.");
        }

        private sealed class MutableVariableSource : ILessonVariableSource
        {
            public bool Done;
            public bool TryGetValue(string name, out LessonVariableValue value)
            {
                value = LessonVariableValue.FromBoolean(Done);
                return name == "done";
            }
        }

        private sealed class ResultRegistry : INodeExecutorRegistry
        {
            private readonly Dictionary<NodeType, Func<NodeExecutionContext, Task<NodeResult>>> _executors = new Dictionary<NodeType, Func<NodeExecutionContext, Task<NodeResult>>>();
            private readonly Func<NodeExecutionContext, Task<NodeResult>> _fallback;
            public ResultRegistry(Func<NodeExecutionContext, Task<NodeResult>> fallback) { _fallback = fallback; }
            public void Set(NodeType type, Func<NodeExecutionContext, Task<NodeResult>> execute) => _executors[type] = execute;
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                if (_executors.TryGetValue(type, out var execute)) { executor = new DelegateExecutor(execute); return true; }
                executor = new DelegateExecutor(_fallback);
                return true;
            }
        }

        private sealed class DelegateExecutor : INodeExecutor
        {
            private readonly Func<NodeExecutionContext, Task<NodeResult>> _execute;
            public DelegateExecutor(Func<NodeExecutionContext, Task<NodeResult>> execute) { _execute = execute; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) => _execute(context);
        }

        private sealed class TestClock : INodeClock
        {
            public double ElapsedSeconds => 0;
            public Task Delay(float seconds, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
