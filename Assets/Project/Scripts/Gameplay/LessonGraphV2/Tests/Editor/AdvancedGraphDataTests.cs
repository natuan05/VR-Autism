using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphDataTests
    {
        [Test]
        public void ExistingNodeTypeValuesRemainStableAndLoopIsAppended()
        {
            Assert.AreEqual(0, (int)NodeType.Quest);
            Assert.AreEqual(1, (int)NodeType.Dialogue);
            Assert.AreEqual(2, (int)NodeType.Wait);
            Assert.AreEqual(3, (int)NodeType.Checkpoint);
            Assert.AreEqual(4, (int)NodeType.Timeline);
            Assert.AreEqual(5, (int)NodeType.Parallel);
            Assert.AreEqual(6, (int)NodeType.Gate);
            Assert.AreEqual(7, (int)NodeType.Loop);
        }

        [Test]
        public void CheckpointConstructorRetainsPhaseOneFieldsAndAddsResumeKey()
        {
            var checkpoint = new CheckpointNodeConfig("stable-id", false);
            Assert.AreEqual("stable-id", checkpoint.CheckpointId);
            Assert.IsFalse(checkpoint.EmitTelemetry);
            Assert.IsEmpty(checkpoint.ResumeCompatibilityKey);
        }

        [Test]
        public void TypedConfigsAndConditionsRoundTripThroughUnityManagedReferences()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            try
            {
                graph.Editor_SetSchemaVersion(2);
                graph.Editor_SetEntryNodeId("timeline");
                graph.Editor_SetNodes(new List<LessonNodeData>
                {
                    new LessonNodeData("timeline", NodeType.Timeline,
                        new TimelineNodeConfig(null, "done", 4f, TimelineTimeoutOutcome.Timeout)),
                    new LessonNodeData("parallel", NodeType.Parallel,
                        new ParallelNodeConfig(new List<ParallelBranch> { new ParallelBranch("left", "body") },
                            "gate", ParallelJoinPolicy.AllSuccess)),
                    new LessonNodeData("loop", NodeType.Loop,
                        new LoopNodeConfig("body", "exit", 3,
                            new VariableCondition("count", VariableValueType.Integer,
                                VariableComparisonOperator.GreaterThan, 2))),
                });
                var composite = new CompositeCondition(CompositeConditionOperator.And,
                    new List<IEdgeCondition> { new VariableCondition("ready", VariableValueType.Boolean,
                        VariableComparisonOperator.Equal, true) });
                graph.Editor_SetEdges(new List<LessonEdgeData>
                {
                    new LessonEdgeData("timeline", "parallel", composite)
                });

                var json = EditorJsonUtility.ToJson(graph);
                var copy = ScriptableObject.CreateInstance<LessonGraph>();
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(json, copy);
                    Assert.IsInstanceOf<TimelineNodeConfig>(copy.Nodes[0].Config);
                    Assert.IsInstanceOf<ParallelNodeConfig>(copy.Nodes[1].Config);
                    Assert.IsInstanceOf<LoopNodeConfig>(copy.Nodes[2].Config);
                    Assert.IsInstanceOf<CompositeCondition>(copy.Edges[0].Condition);
                }
                finally { Object.DestroyImmediate(copy); }
            }
            finally { Object.DestroyImmediate(graph); }
        }
    }
}
