using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class TimelineExecutionCompositionTests
    {
        [Test]
        public void Registry_RegistersTimelineOnlyWhenSceneControllerIsProvided()
        {
            var clock = new FixedClock();
            var absent = new LessonGraphExecutorRegistry(new EmptyResolver(), clock);
            Assert.IsFalse(absent.TryGet(NodeType.Timeline, out _));

            var present = new LessonGraphExecutorRegistry(new EmptyResolver(), clock,
                timelinePlaybackController: new FakeTimelineController());
            Assert.IsTrue(present.TryGet(NodeType.Timeline, out var executor));
            Assert.IsInstanceOf<TimelineNodeExecutor>(executor);
        }

        [Test]
        public void ExecutionPreflight_AllowsSchemaTwoTimelineAndStructuredFlowNodes()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId("timeline");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("timeline", NodeType.Timeline,
                    new TimelineNodeConfig(asset, "finished", 5f, TimelineTimeoutOutcome.Timeout))
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            try
            {
                Assert.IsTrue(LessonGraphValidator.ValidateForExecution(graph).IsValid,
                    LessonGraphValidator.ValidateForExecution(graph).ToString());

                graph.Editor_SetNodes(new List<LessonNodeData>
                {
                    new LessonNodeData("parallel", NodeType.Parallel,
                        new ParallelNodeConfig(new List<ParallelBranch>
                        {
                            new ParallelBranch("a", "a"), new ParallelBranch("b", "b")
                        }, "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("a", NodeType.Wait, new WaitNodeConfig(1f)),
                    new LessonNodeData("b", NodeType.Wait, new WaitNodeConfig(1f)),
                    new LessonNodeData("gate", NodeType.Gate,
                        new GateNodeConfig("parallel", new List<string> { "a", "b" }, GateConditionMode.And))
                });
                graph.Editor_SetEntryNodeId("parallel");
                graph.Editor_SetEdges(new List<LessonEdgeData>
                {
                    new LessonEdgeData("parallel", "gate", new AlwaysCondition())
                });
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());
                Assert.IsTrue(LessonGraphValidator.ValidateForExecution(graph).IsValid,
                    LessonGraphValidator.ValidateForExecution(graph).ToString());
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ExecutionPreflight_RejectsParallelWithMultipleTimelineChildren()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var firstAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            var secondAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId("parallel");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("parallel", NodeType.Parallel, new ParallelNodeConfig(new List<ParallelBranch>
                {
                    new ParallelBranch("first", "first-timeline"), new ParallelBranch("second", "second-timeline")
                }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("first-timeline", NodeType.Timeline,
                    new TimelineNodeConfig(firstAsset, "first-done", 5f, TimelineTimeoutOutcome.Timeout)),
                new LessonNodeData("second-timeline", NodeType.Timeline,
                    new TimelineNodeConfig(secondAsset, "second-done", 5f, TimelineTimeoutOutcome.Timeout)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", new List<string> { "first", "second" }, GateConditionMode.And)),
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition()),
            });
            try
            {
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());
                var execution = LessonGraphValidator.ValidateForExecution(graph);
                Assert.IsFalse(execution.IsValid);
                Assert.IsTrue(execution.Errors.Any(error => error.ErrorCode == GraphValidationErrorCode.UnsupportedExecutionFeature &&
                    error.NodeId == "parallel"));
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [UnityTest]
        public IEnumerator RunnerRejectsTimelineBeforeActivationWhenRegistryHasNoExecutor()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            var runnerObject = new GameObject("timeline-preflight-test");
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId("timeline");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("timeline", NodeType.Timeline,
                    new TimelineNodeConfig(asset, "finished", 5f, TimelineTimeoutOutcome.Timeout))
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            try
            {
                var runner = runnerObject.AddComponent<LessonGraphRunner>();
                runner.Configure(graph, new LessonGraphExecutorRegistry(new EmptyResolver(), new FixedClock()),
                    clock: new FixedClock());
                var task = runner.StartLessonAsync();
                yield return CompleteWithinFrames(task);
                Assert.AreEqual(LessonFailureReason.InvalidGraph, task.GetAwaiter().GetResult().FailureReason);
                Assert.IsNull(runner.CurrentState, "Missing Timeline executor must be rejected before activation.");
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(asset);
            }
        }

        private sealed class FakeTimelineController : ITimelinePlaybackController
        {
            public ITimelinePlaybackSession StartPlayback(TimelineNodeConfig config) => null;
        }

        private sealed class EmptyResolver : IQuestBindingResolver
        {
            public QuestBindingResolution Resolve(string bindingId) => QuestBindingResolution.Failure(null);
        }

        private sealed class FixedClock : INodeClock
        {
            public double ElapsedSeconds => 0d;
            public System.Threading.Tasks.Task Delay(float seconds, System.Threading.CancellationToken cancellationToken) =>
                System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        }

        private static IEnumerator CompleteWithinFrames(System.Threading.Tasks.Task task)
        {
            for (var frame = 0; frame < 20 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Runner task did not complete within 20 EditMode frames.");
        }
    }
}
