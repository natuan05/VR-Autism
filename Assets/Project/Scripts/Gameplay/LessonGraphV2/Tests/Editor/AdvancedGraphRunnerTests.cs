using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphRunnerTests
    {
        [UnityTest]
        public IEnumerator StartLessonAsync_AuthoringValidAdvancedGraphFailsBeforeExecutorActivation()
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

                Assert.AreEqual(LessonFailureReason.InvalidGraph, result.FailureReason);
                Assert.AreEqual(0, registry.Lookups,
                    "Unsupported advanced execution must be rejected before executor lookup/activation.");
                Assert.AreEqual(0, executor.Executions);
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

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Runner task did not complete within 30 editor frames.");
        }
    }
}
