using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class AdvancedGraphValidatorTests
    {
        private static LessonGraph Graph(string entry, params LessonNodeData[] nodes)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.Editor_SetSchemaVersion(2);
            graph.Editor_SetEntryNodeId(entry);
            graph.Editor_SetNodes(nodes.ToList());
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            return graph;
        }

        [Test]
        public void SchemaTwoSupportsValidTypedParallelGateStructure()
        {
            var graph = Graph("parallel",
                new LessonNodeData("parallel", NodeType.Parallel,
                    new ParallelNodeConfig(new List<ParallelBranch>
                    {
                        new ParallelBranch("a", "child-a"), new ParallelBranch("b", "child-b")
                    }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("child-a", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("child-b", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", new List<string> { "a", "b" }, GateConditionMode.And)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition())
            });
            try { Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid, LessonGraphValidator.Validate(graph).ToString()); }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoValidationAllowsAdvancedAuthoringButExecutionPreflightRejectsIt()
        {
            var graph = Graph("loop",
                new LessonNodeData("loop", NodeType.Loop,
                    new LoopNodeConfig("body", "exit", 2,
                        new VariableCondition("ready", VariableValueType.Boolean,
                            VariableComparisonOperator.Equal, true))),
                new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)));
            try
            {
                Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid,
                    LessonGraphValidator.Validate(graph).ToString());
                Assert.IsFalse(LessonGraphValidator.ValidateForExecution(graph).IsValid);
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void FirstCompletedParallelRejectsAndGateDeadlock()
        {
            var graph = Graph("parallel",
                new LessonNodeData("parallel", NodeType.Parallel,
                    new ParallelNodeConfig(new List<ParallelBranch>
                    {
                        new ParallelBranch("a", "child-a"), new ParallelBranch("b", "child-b")
                    }, "gate", ParallelJoinPolicy.FirstCompleted)),
                new LessonNodeData("child-a", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("child-b", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", new List<string> { "a", "b" }, GateConditionMode.And)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition())
            });
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsFalse(result.IsValid);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.ParallelGateDeadlock));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void ParallelMissingChildReferencesFailValidationWithoutThrowing()
        {
            var graph = Graph("parallel",
                new LessonNodeData("parallel", NodeType.Parallel,
                    new ParallelNodeConfig(new List<ParallelBranch>
                    {
                        new ParallelBranch("a", "missing-a"), new ParallelBranch("b", "missing-b")
                    }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", new List<string> { "a", "b" }, GateConditionMode.And)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition())
            });
            try
            {
                GraphValidationResult result = null;
                Assert.DoesNotThrow(() => result = LessonGraphValidator.Validate(graph));
                Assert.IsFalse(result.IsValid);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidParallelConfig));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaOneCheckpointDuplicateIdsRemainCompatible()
        {
            var graph = Graph("a",
                new LessonNodeData("a", NodeType.Checkpoint, new CheckpointNodeConfig("legacy")),
                new LessonNodeData("b", NodeType.Checkpoint, new CheckpointNodeConfig("legacy")));
            graph.Editor_SetSchemaVersion(1);
            graph.Editor_SetEdges(new List<LessonEdgeData> { new LessonEdgeData("a", "b", new AlwaysCondition()) });
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsTrue(result.IsValid, result.ToString());
                Assert.IsFalse(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.DuplicateCheckpointId));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GateRejectsMissingOrDuplicateInputs(bool duplicate)
        {
            var inputs = duplicate ? new List<string> { "a", "a" } : new List<string> { "a" };
            var graph = Graph("parallel",
                new LessonNodeData("parallel", NodeType.Parallel,
                    new ParallelNodeConfig(new List<ParallelBranch>
                    {
                        new ParallelBranch("a", "child-a"), new ParallelBranch("b", "child-b")
                    }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("child-a", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("child-b", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", inputs, GateConditionMode.And)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition())
            });
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidGateConfig));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [TestCase(0, "exit")]
        [TestCase(1, "missing-exit")]
        public void LoopRejectsInvalidBoundsOrMissingExit(int maximumIterations, string exitNodeId)
        {
            var graph = Graph("loop",
                new LessonNodeData("loop", NodeType.Loop,
                    new LoopNodeConfig("body", exitNodeId, maximumIterations,
                        new AlwaysCondition())),
                new LessonNodeData("body", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("exit", NodeType.Wait, new WaitNodeConfig(1)));
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidLoopConfig));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoRejectsCheckpointInsideParallelJoinBoundary()
        {
            var graph = Graph("parallel",
                new LessonNodeData("parallel", NodeType.Parallel,
                    new ParallelNodeConfig(new List<ParallelBranch>
                    {
                        new ParallelBranch("a", "checkpoint"), new ParallelBranch("b", "child-b")
                    }, "gate", ParallelJoinPolicy.AllSuccess)),
                new LessonNodeData("checkpoint", NodeType.Checkpoint,
                    new CheckpointNodeConfig("cp", true, "resume-v1")),
                new LessonNodeData("child-b", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("gate", NodeType.Gate,
                    new GateNodeConfig("parallel", new List<string> { "a", "b" }, GateConditionMode.And)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("parallel", "gate", new AlwaysCondition())
            });
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidCheckpointPlacement));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoCheckpointIdsMustBeUnique()
        {
            var graph = Graph("a",
                new LessonNodeData("a", NodeType.Checkpoint, new CheckpointNodeConfig("duplicate", true, "resume-v1")),
                new LessonNodeData("b", NodeType.Checkpoint, new CheckpointNodeConfig("duplicate", true, "resume-v1")));
            graph.Editor_SetEdges(new List<LessonEdgeData> { new LessonEdgeData("a", "b", new AlwaysCondition()) });
            try
            {
                var result = LessonGraphValidator.Validate(graph);
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.DuplicateCheckpointId));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoVariableConditionsAcceptAllPrimitiveKindsAndTypedOperators()
        {
            var graph = Graph("a",
                new LessonNodeData("a", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("b", NodeType.Wait, new WaitNodeConfig(1)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("a", "b", new CompositeCondition(CompositeConditionOperator.And,
                    new List<IEdgeCondition>
                    {
                        new VariableCondition("flag", VariableValueType.Boolean, VariableComparisonOperator.Equal, true),
                        new VariableCondition("count", VariableValueType.Integer, VariableComparisonOperator.GreaterThan, 2),
                        new VariableCondition("ratio", VariableValueType.Float, VariableComparisonOperator.LessThanOrEqual, 0.5f),
                        new VariableCondition("name", VariableValueType.String, VariableComparisonOperator.NotEqual, "")
                    }))
            });
            try { Assert.IsTrue(LessonGraphValidator.Validate(graph).IsValid, LessonGraphValidator.Validate(graph).ToString()); }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoRejectsInvalidTimelineAndVariableFields()
        {
            var graph = Graph("timeline",
                new LessonNodeData("timeline", NodeType.Timeline,
                    new TimelineNodeConfig(null, " ", float.PositiveInfinity, (TimelineTimeoutOutcome)99)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("timeline", "timeline", new VariableCondition("flag",
                    VariableValueType.Boolean, VariableComparisonOperator.GreaterThan, true))
            });
            var result = LessonGraphValidator.Validate(graph);
            try
            {
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidTimelineConfig));
                Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidVariableCondition));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Test]
        public void SchemaTwoRejectsEmptyAndOverdeepCompositeConditions()
        {
            IEdgeCondition condition = new CompositeCondition();
            for (var depth = 0; depth < 17; depth++)
                condition = new CompositeCondition(CompositeConditionOperator.And, new List<IEdgeCondition> { condition });
            var graph = Graph("a",
                new LessonNodeData("a", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("b", NodeType.Wait, new WaitNodeConfig(1)));
            graph.Editor_SetEdges(new List<LessonEdgeData>
            {
                new LessonEdgeData("a", "b", condition),
                new LessonEdgeData("b", "a", new CompositeCondition())
            });
            var result = LessonGraphValidator.Validate(graph);
            try { Assert.IsTrue(result.Errors.Any(e => e.ErrorCode == GraphValidationErrorCode.InvalidCompositeCondition)); }
            finally { Object.DestroyImmediate(graph); }
        }
    }
}
