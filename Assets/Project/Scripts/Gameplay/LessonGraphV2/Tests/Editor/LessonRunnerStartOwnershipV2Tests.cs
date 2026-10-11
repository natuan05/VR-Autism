using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonRunnerStartOwnershipV2Tests
    {
        [UnityTest]
        public IEnumerator StartLessonAsync_WithoutExplicitOwnerFailsClosedBeforeActivation()
        {
            var graph = CreateGraph();
            var gameObject = new GameObject(nameof(StartLessonAsync_WithoutExplicitOwnerFailsClosedBeforeActivation));
            try
            {
                var executor = new ImmediateExecutor();
                var runner = gameObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, new SingleRegistry(executor));

                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] Refusing to start because no active owner authorized the lesson. No active V2 runtime owner is registered.");
                var result = runner.StartLessonAsync();
                yield return CompleteWithinFrames(result);

                Assert.AreEqual(LessonFailureReason.InvalidGraph, result.GetAwaiter().GetResult().FailureReason);
                Assert.IsNull(runner.CurrentState);
                Assert.AreEqual(0, executor.ExecutionCount);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator StartLessonAsync_ExplicitOwnerAuthorizesExactlyOneRun()
        {
            var graph = CreateGraph();
            var gameObject = new GameObject(nameof(StartLessonAsync_ExplicitOwnerAuthorizesExactlyOneRun));
            try
            {
                var executor = new ImmediateExecutor();
                var runner = gameObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, new SingleRegistry(executor));
                var owner = new ExplicitTestOwner();
                runner.RegisterStartOwner(owner);

                var first = runner.StartLessonAsync();
                yield return CompleteWithinFrames(first);
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] Refusing to start because no active owner authorized the lesson. test owner already authorized its run");
                var second = runner.StartLessonAsync();

                Assert.IsTrue(first.GetAwaiter().GetResult().IsSuccess);
                Assert.AreEqual(LessonFailureReason.InvalidGraph, second.GetAwaiter().GetResult().FailureReason);
                Assert.AreEqual(1, owner.StartCount);
                Assert.AreEqual(1, executor.ExecutionCount);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(graph);
            }
        }

        private static LessonGraph CreateGraph()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.Editor_SetEntryNodeId("entry");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("entry", NodeType.Wait, new WaitNodeConfig(1f))
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            return graph;
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "The focused runner operation must complete within 120 frames.");
        }

        private sealed class ExplicitTestOwner : ILessonRunnerStartOwnerV2
        {
            public bool IsAvailable => true;
            public int StartCount { get; private set; }
            public bool CanAuthorizeLessonStart(out string reason)
            {
                reason = StartCount == 0 ? null : "test owner already authorized its run";
                return StartCount == 0;
            }

            public void MarkLessonStartRequested() => StartCount++;
        }

        private sealed class SingleRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public SingleRegistry(INodeExecutor executor) => _executor = executor;
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = type == NodeType.Wait ? _executor : null;
                return executor != null;
            }
        }

        private sealed class ImmediateExecutor : INodeExecutor
        {
            public int ExecutionCount { get; private set; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                ExecutionCount++;
                return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId,
                    NodeStatus.Success, context.ElapsedSeconds));
            }
        }
    }
}
