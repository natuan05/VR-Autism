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
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphRunnerTests
    {
        [UnityTest]
        public IEnumerator StartLessonAsync_BoundedLoopExecutesOwnedBodyAndExit()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("AdvancedGraphRunnerTests");
            var executor = new CountingExecutor();
            var registry = new CountingRegistry(executor);
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("loop");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("loop", NodeType.Loop, new LoopNodeConfig("body", "exit", 2, new AlwaysCondition())),
                    new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>());
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());

                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(2, executor.Executions, "The Loop owns one body activation and the graph then executes its configured exit.");
                Assert.AreEqual(1, runner.RunNodeVisitCounts["loop"]);
                Assert.AreEqual(1, runner.RunNodeVisitCounts["body"]);
                Assert.AreEqual(1, runner.RunNodeVisitCounts["exit"]);
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator StartLessonAsync_SchemaOneWaitGraphStillExecutes()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("SchemaOneGraphRunnerTests");
            var executor = new CountingExecutor();
            var registry = new CountingRegistry(executor);
            try
            {
                graph.Editor_SetEntryNodeId("wait");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("wait", NodeType.Wait, new WaitNodeConfig(1)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>());

                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);
                var result = task.GetAwaiter().GetResult();

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(1, executor.Executions);
                Assert.Greater(registry.Lookups, 0);
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator StartLessonAsync_ParallelRunsBranchesThenGateAndPublishesOnlyParentResults()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("StructuredParallelGraphRunnerTests");
            var executor = new CountingExecutor();
            var registry = new CountingRegistry(executor);
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("parallel");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                        new[] { new ParallelBranch("first", "first-child"), new ParallelBranch("second", "second-child") },
                        "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("first-child", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("second-child", NodeType.Dialogue, new DialogueNodeConfig("sequence", "text", "npc")),
                    new LessonNodeData("gate", NodeType.Gate,
                        new GateNodeConfig("parallel", new[] { "first", "second" }, GateConditionMode.And)),
                    new LessonNodeData("done", NodeType.Wait, new WaitNodeConfig(1)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("parallel", "gate", new AlwaysCondition()),
                    new LessonEdgeData("gate", "done", new AlwaysCondition()),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                var publicCompletions = 0;
                runner.NodeCompleted += _ => publicCompletions++;
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);

                Assert.IsTrue(task.GetAwaiter().GetResult().IsSuccess);
                Assert.AreEqual(3, publicCompletions,
                    "Only the Parallel, Gate and ordinary exit publish graph-level completion.");
                Assert.AreEqual(3, executor.Executions, "The two owned branches and the final graph node execute.");
                Assert.AreEqual(1, runner.RunNodeVisitCounts["first-child"]);
                Assert.AreEqual(1, runner.RunNodeVisitCounts["second-child"]);
                Assert.AreEqual(1, runner.RunEdgeVisitCounts["parallel->gate"]);
                Assert.AreEqual(1, runner.RunEdgeVisitCounts["parallel->first-child"]);
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator StartLessonAsync_VariableEdgeUsesTypedSourceAfterStatusPrecedence()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("VariableConditionRunnerTests");
            var executor = new CountingExecutor();
            var registry = new CountingRegistry(executor);
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("start");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("start", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("variable-target", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("status-target", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("fallback", NodeType.Wait, new WaitNodeConfig(1)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("start", "variable-target", new VariableCondition("ready", VariableValueType.Boolean,
                        VariableComparisonOperator.Equal, true), 0),
                    new LessonEdgeData("start", "status-target", new StatusCondition("success"), 10),
                    new LessonEdgeData("start", "fallback", new AlwaysCondition(), 20),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock(), variableSource: new FixedVariableSource(true));
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);

                Assert.IsTrue(task.GetAwaiter().GetResult().IsSuccess);
                Assert.AreEqual(0, runner.RunNodeVisitCounts.ContainsKey("variable-target") ? runner.RunNodeVisitCounts["variable-target"] : 0);
                Assert.AreEqual(1, runner.RunNodeVisitCounts["status-target"],
                    "A matching status edge keeps Phase-1 precedence over variable conditions.");
                Assert.IsFalse(runner.RunNodeVisitCounts.ContainsKey("fallback"));
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator StartLessonAsync_LoopLimitUsesFailureStatusEdge()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("LoopLimitRunnerTests");
            var executor = new CountingExecutor();
            var registry = new CountingRegistry(executor);
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("loop");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("loop", NodeType.Loop, new LoopNodeConfig("body", "exit", 2,
                        new VariableCondition("done", VariableValueType.Boolean, VariableComparisonOperator.Equal, true))),
                    new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("recovery", NodeType.Wait, new WaitNodeConfig(1)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("loop", "recovery", new StatusCondition("failed")),
                    new LessonEdgeData("loop", "exit", new AlwaysCondition()),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock(), variableSource: new FixedVariableSource(false));
                var loopFailed = false;
                runner.NodeCompleted += completed =>
                {
                    if (completed.Result.NodeId == "loop") loopFailed = completed.Result.Status == NodeStatus.Failed;
                };
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);

                Assert.IsTrue(task.GetAwaiter().GetResult().IsSuccess,
                    "The configured failure route completes at the recovery node.");
                Assert.IsTrue(loopFailed);
                Assert.AreEqual(2, runner.RunNodeVisitCounts["body"]);
                Assert.AreEqual(1, runner.RunNodeVisitCounts["recovery"]);
                Assert.IsFalse(runner.RunNodeVisitCounts.ContainsKey("exit"));
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator AbortLesson_CancelsOwnedBranchScopesWithoutWaitingForUncooperativeChildren()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("StructuredAbortRunnerTests");
            var registry = new BlockingRegistry();
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("parallel");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                        new[] { new ParallelBranch("first", "first-child"), new ParallelBranch("second", "second-child") },
                        "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("first-child", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("second-child", NodeType.Dialogue, new DialogueNodeConfig("sequence", "text", "npc")),
                    new LessonNodeData("gate", NodeType.Gate,
                        new GateNodeConfig("parallel", new[] { "first", "second" }, GateConditionMode.And)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("parallel", "gate", new AlwaysCondition()),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                var task = runner.StartLessonAsync();
                for (var frame = 0; frame < 30 && registry.StartedCount < 2; frame++) yield return null;
                Assert.AreEqual(2, registry.StartedCount, "Both branch activations must be active before abort.");

                runner.AbortLesson();
                yield return CompleteWithinFrames(task);
                Assert.AreEqual(LessonFailureReason.Aborted, task.GetAwaiter().GetResult().FailureReason);
                Assert.AreEqual(2, registry.CancelledCount, "Abort must cancel each owned branch scope.");
            }
            finally
            {
                registry.CancelPendingChildren();
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator Runner_OnDisableCancelsOwnedParallelChildrenAndIgnoresLateCompletions()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("StructuredDisableRunnerTests");
            var registry = new LateCompletingRegistry();
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("parallel");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                        new[] { new ParallelBranch("first", "first-child"), new ParallelBranch("second", "second-child") },
                        "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("first-child", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("second-child", NodeType.Dialogue, new DialogueNodeConfig("sequence", "text", "npc")),
                    new LessonNodeData("gate", NodeType.Gate,
                        new GateNodeConfig("parallel", new[] { "first", "second" }, GateConditionMode.And)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("parallel", "gate", new AlwaysCondition()),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                var entered = new System.Collections.Generic.List<string>();
                runner.NodeEntered += item => entered.Add(item.NodeId);
                var task = runner.StartLessonAsync();
                for (var frame = 0; frame < 30 && registry.StartedCount < 2; frame++) yield return null;
                Assert.AreEqual(2, registry.StartedCount, "Both owned child activations must be active before scene unload.");

                typeof(LessonGraphRunner)
                    .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(runner, null);
                runnerObject.SetActive(false);
                yield return CompleteWithinFrames(task);
                Assert.AreEqual(LessonFailureReason.Aborted, task.GetAwaiter().GetResult().FailureReason);
                Assert.AreEqual(2, registry.CancelledCount, "OnDisable must cancel every active owned child scope.");

                registry.CompleteAll(NodeStatus.Success);
                for (var frame = 0; frame < 3; frame++) yield return null;
                Assert.IsFalse(entered.Contains("gate"), "Late child results must not join the Parallel or enter its Gate.");
                Assert.AreEqual(LessonFailureReason.Aborted, task.GetAwaiter().GetResult().FailureReason);
            }
            finally
            {
                registry.CompleteAll(NodeStatus.Success);
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator PauseAndResume_CancelAndRecreateOwnedBranchScopes()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("StructuredPauseRunnerTests");
            var registry = new BlockingRegistry();
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("parallel");
                graph.Editor_SetNodes(new System.Collections.Generic.List<LessonNodeData>
                {
                    new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(
                        new[] { new ParallelBranch("first", "first-child"), new ParallelBranch("second", "second-child") },
                        "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("first-child", NodeType.Wait, new WaitNodeConfig(1)),
                    new LessonNodeData("second-child", NodeType.Dialogue, new DialogueNodeConfig("sequence", "text", "npc")),
                    new LessonNodeData("gate", NodeType.Gate,
                        new GateNodeConfig("parallel", new[] { "first", "second" }, GateConditionMode.And)),
                });
                graph.Editor_SetEdges(new System.Collections.Generic.List<LessonEdgeData>
                {
                    new LessonEdgeData("parallel", "gate", new AlwaysCondition()),
                });
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, registry, clock: new TestClock());
                runner.ConfigureSession(new LessonSessionContextV2("session", "lesson", "launch", 0, 0));
                var lessonTask = runner.StartLessonAsync();
                for (var frame = 0; frame < 30 && registry.StartedCount < 2; frame++) yield return null;
                Assert.AreEqual(2, registry.StartedCount);

                var pause = runner.ApplyCommandAsync(Command(runner.CurrentState, LessonCommandKindV2.Pause, "pause-1"));
                yield return CompleteWithinFrames(pause, "pause command", () =>
                    $"state={runner.CurrentState.status}, cancelled children={registry.CancelledCount}/{registry.StartedCount}");
                Assert.IsTrue(pause.GetAwaiter().GetResult().accepted);
                Assert.AreEqual("paused", runner.CurrentState.status);
                Assert.AreEqual(2, registry.CancelledCount);

                var resume = runner.ApplyCommandAsync(Command(runner.CurrentState, LessonCommandKindV2.Resume, "resume-1"));
                yield return CompleteWithinFrames(resume, "resume command", () =>
                    $"state={runner.CurrentState.status}, started children={registry.StartedCount}");
                Assert.IsTrue(resume.GetAwaiter().GetResult().accepted);
                for (var frame = 0; frame < 30 && registry.StartedCount < 4; frame++) yield return null;
                Assert.AreEqual(4, registry.StartedCount, "Resume creates fresh child activations.");

                runner.AbortLesson();
                yield return CompleteWithinFrames(lessonTask, "aborted lesson");
                Assert.AreEqual(4, registry.CancelledCount);
            }
            finally
            {
                registry.CancelPendingChildren();
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
            }
        }

        private sealed class CountingRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public int Lookups { get; private set; }
            public CountingRegistry(INodeExecutor executor) { _executor = executor; }
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                Lookups++;
                executor = _executor;
                return true;
            }
        }

        private sealed class CountingExecutor : INodeExecutor
        {
            public int Executions { get; private set; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                Executions++;
                return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId,
                    NodeStatus.Success, context.ElapsedSeconds));
            }
        }

        private sealed class TestClock : INodeClock
        {
            public double ElapsedSeconds => 0;
            public Task Delay(float seconds, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class FixedVariableSource : ILessonVariableSource
        {
            private readonly bool _ready;
            public FixedVariableSource(bool ready) { _ready = ready; }
            public bool TryGetValue(string name, out LessonVariableValue value)
            {
                value = LessonVariableValue.FromBoolean(_ready);
                return name == "ready";
            }
        }

        private sealed class BlockingRegistry : INodeExecutorRegistry
        {
            private readonly List<CancellationToken> _tokens = new List<CancellationToken>();
            private readonly List<TaskCompletionSource<NodeResult>> _pending = new List<TaskCompletionSource<NodeResult>>();
            public int StartedCount => _tokens.Count;
            public int CancelledCount { get; private set; }
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = new BlockingExecutor(this);
                return type == NodeType.Wait || type == NodeType.Dialogue;
            }
            public void Start(NodeExecutionContext context)
            {
                _tokens.Add(context.CancellationToken);
                var completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(completion);
                context.CancellationToken.Register(() => CancelledCount++);
            }
            public Task<NodeResult> PendingTask(int index) => _pending[index].Task;
            public void CancelPendingChildren()
            {
                foreach (var completion in _pending) completion.TrySetCanceled();
            }
        }

        private sealed class BlockingExecutor : INodeExecutor
        {
            private readonly BlockingRegistry _owner;
            public BlockingExecutor(BlockingRegistry owner) { _owner = owner; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                _owner.Start(context);
                return _owner.PendingTask(_owner.StartedCount - 1);
            }
        }

        private sealed class LateCompletingRegistry : INodeExecutorRegistry
        {
            private readonly System.Collections.Generic.List<PendingChild> _children = new System.Collections.Generic.List<PendingChild>();
            public int StartedCount => _children.Count;
            public int CancelledCount { get; private set; }
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = new LateCompletingExecutor(this);
                return type == NodeType.Wait || type == NodeType.Dialogue;
            }
            public Task<NodeResult> Start(NodeExecutionContext context)
            {
                var completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var child = new PendingChild(context.Node.Id, context.ActivationId, context.ElapsedSeconds, completion);
                _children.Add(child);
                context.CancellationToken.Register(() => CancelledCount++);
                return completion.Task;
            }
            public void CompleteAll(NodeStatus status)
            {
                foreach (var child in _children)
                    child.Completion.TrySetResult(NodeResult.Completed(child.NodeId, child.ActivationId, status, child.ElapsedSeconds));
            }
            private sealed class PendingChild
            {
                public readonly string NodeId;
                public readonly string ActivationId;
                public readonly double ElapsedSeconds;
                public readonly TaskCompletionSource<NodeResult> Completion;
                public PendingChild(string nodeId, string activationId, double elapsedSeconds, TaskCompletionSource<NodeResult> completion)
                { NodeId = nodeId; ActivationId = activationId; ElapsedSeconds = elapsedSeconds; Completion = completion; }
            }
        }

        private sealed class LateCompletingExecutor : INodeExecutor
        {
            private readonly LateCompletingRegistry _owner;
            public LateCompletingExecutor(LateCompletingRegistry owner) { _owner = owner; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) => _owner.Start(context);
        }

        private static IEnumerator CompleteWithinFrames(Task task, string operation = "runner task", System.Func<string> diagnostics = null)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, operation + " did not complete within 30 editor frames. " + diagnostics?.Invoke());
        }

        private static LessonCommandV2 Command(LessonStateV2 state, string command, string commandId) => new LessonCommandV2
        {
            contract_version = LessonRemoteContractV2.ContractVersion,
            @event = LessonRemoteContractV2.CommandEvent,
            command_id = commandId,
            session_id = state.session_id,
            run_id = state.run_id,
            node_id = state.node_id,
            activation_id = state.activation_id,
            command = command,
        };
    }
}
