using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    /// <summary>
    /// EditMode NUnit tests for LessonGraphValidator.
    /// No scene must be loaded. All graphs constructed in memory using Editor_Set* helpers.
    ///
    /// Test inventory (40 tests):
    ///   1-15   original AC coverage
    ///   16-28  review round 1 findings
    ///   29-38  review round 2 findings
    /// </summary>
    [TestFixture]
    public sealed class LessonGraphValidatorTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static LessonGraph MakeGraph(
            string entryNodeId,
            List<LessonNodeData> nodes,
            List<LessonEdgeData> edges = null,
            int schemaVersion = 1)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.Editor_SetSchemaVersion(schemaVersion);
            graph.Editor_SetEntryNodeId(entryNodeId);
            graph.Editor_SetNodes(nodes);
            graph.Editor_SetEdges(edges ?? new List<LessonEdgeData>());
            return graph;
        }

        private static LessonNodeData QuestNode(
            string id,
            List<string> bindingIds = null,
            float timeout = -1f)
        {
            var cfg = new QuestNodeConfig(
                bindingIds ?? new List<string> { id + "-binding" }, timeout);
            return new LessonNodeData(id, NodeType.Quest, cfg);
        }

        private static LessonNodeData WaitNode(string id, float duration = 1f) =>
            new LessonNodeData(id, NodeType.Wait, new WaitNodeConfig(duration));

        private static LessonNodeData DialogueNode(
            string id,
            string seqId = null,
            string text  = null,
            float timeout = 30f) =>
            new LessonNodeData(id, NodeType.Dialogue,
                new DialogueNodeConfig(seqId ?? id + "-seq", text ?? id + "-text",
                    blocking: true, timeoutSeconds: timeout));

        private static LessonNodeData CheckpointNode(string id, string cpId = null,
            bool emitTelemetry = true) =>
            new LessonNodeData(id, NodeType.Checkpoint,
                new CheckpointNodeConfig(cpId ?? id + "-cp", emitTelemetry));

        private static LessonEdgeData Always(string from, string to, int priority = 0) =>
            new LessonEdgeData(from, to, new AlwaysCondition(), priority);

        private static LessonEdgeData StatusEdge(string from, string to,
            string status, int priority = 0) =>
            new LessonEdgeData(from, to, new StatusCondition(status), priority);

        private static bool HasError(GraphValidationResult r, GraphValidationErrorCode code)
        {
            foreach (var e in r.Errors)
                if (e.ErrorCode == code) return true;
            return false;
        }

        private static void Destroy(params LessonGraph[] graphs)
        {
            foreach (var g in graphs)
                if (g != null) Object.DestroyImmediate(g);
        }

        private static void SetPrivateGraphField<T>(LessonGraph graph, string fieldName, T value)
        {
            var field = typeof(LessonGraph).GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field: {fieldName}");
            field.SetValue(graph, value);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Original 15 tests
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void T01_ValidMinimalGraph_Passes()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsTrue(r.IsValid, r.ToString());
            Destroy(g);
        }

        [Test]
        public void T02_MissingEntryNodeId_Fails()
        {
            var g = MakeGraph("", new List<LessonNodeData> { QuestNode("n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.MissingEntryNodeId));
            Destroy(g);
        }

        [Test]
        public void T03_EntryNodeIdNotInNodes_Fails()
        {
            var g = MakeGraph("ghost", new List<LessonNodeData> { QuestNode("n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.EntryNodeNotFound));
            Destroy(g);
        }

        [Test]
        public void T04_DuplicateNodeIds_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1"), QuestNode("n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.DuplicateNodeId));
            Destroy(g);
        }

        [Test]
        public void T05_NullBindingId_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest,
                    new QuestNodeConfig(new List<string> { null })),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NullBindingId));
            Destroy(g);
        }

        [Test]
        public void T06_WhitespaceBindingId_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "   " })),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.WhitespaceBindingId));
            Destroy(g);
        }

        [Test]
        public void T07_DuplicateBindingIdAcrossNodes_Fails()
        {
            const string shared = "shared-bid";
            var g = MakeGraph("n1",
                new List<LessonNodeData>
                {
                    new LessonNodeData("n1", NodeType.Quest,
                        new QuestNodeConfig(new List<string> { shared })),
                    new LessonNodeData("n2", NodeType.Quest,
                        new QuestNodeConfig(new List<string> { shared })),
                },
                new List<LessonEdgeData> { Always("n1", "n2") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.DuplicateBindingId));
            Destroy(g);
        }

        [Test]
        public void T08_EdgeTargetMissing_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1") },
                new List<LessonEdgeData> { Always("n1", "ghost") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.MissingEdgeTarget));
            Destroy(g);
        }

        [Test]
        public void T09_EmptyBindingIds_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest,
                    new QuestNodeConfig(new List<string>())),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.EmptyBindingIds));
            Destroy(g);
        }

        [Test]
        public void T10_NullNodeConfig_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest, null),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NullNodeConfig));
            Destroy(g);
        }

        [Test]
        public void T11_ConnectedCycle_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1"), WaitNode("n2") },
                new List<LessonEdgeData> { Always("n1", "n2"), Always("n2", "n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.CycleDetected));
            Destroy(g);
        }

        [Test]
        public void T12_DisconnectedSubgraphCycle_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1"), WaitNode("nA"), WaitNode("nB") },
                new List<LessonEdgeData> { Always("nA", "nB"), Always("nB", "nA") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.CycleDetected));
            Destroy(g);
        }

        [Test]
        public void T13_Phase2NodeType_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Timeline, new WaitNodeConfig(1f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.Phase2NodeType));
            Destroy(g);
        }

        [Test]
        public void T14_Phase2EdgeCondition_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1"), WaitNode("n2") },
                new List<LessonEdgeData>
                {
                    new LessonEdgeData("n1", "n2", new FakePhase2Condition()),
                });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.Phase2EdgeCondition));
            Destroy(g);
        }

        /// <summary>
        /// Round-trip test — strengthened (review round 2):
        /// verifies voice prompt, edge from/to/priority, CheckpointNodeConfig.EmitTelemetry.
        /// </summary>
        [Test]
        public void T15_ValidGraph_RoundTripPreservesAllFields()
        {
            const string entryId  = "node-quest";
            const string cpId     = "node-cp";
            const string binding  = "binding-abc";
            const string prompt   = "Say hello";
            var nodePos           = new Vector2(100f, 200f);
            const int edgePrio    = 5;

            var graph = MakeGraph(
                entryNodeId: entryId,
                nodes: new List<LessonNodeData>
                {
                    new LessonNodeData(entryId, NodeType.Quest,
                        new QuestNodeConfig(new List<string> { binding }, 30f, prompt), nodePos),
                    new LessonNodeData(cpId, NodeType.Checkpoint,
                        new CheckpointNodeConfig("cp-001", emitTelemetry: true)),
                },
                edges: new List<LessonEdgeData>
                {
                    StatusEdge(entryId, cpId, StatusCondition.Success, edgePrio),
                });

            // Pre-serialize validation.
            var pre = LessonGraphValidator.Validate(graph);
            Assert.IsTrue(pre.IsValid, "Pre: " + pre);

            // Round-trip.
            string json = JsonUtility.ToJson(graph, prettyPrint: false);
            Assert.IsFalse(string.IsNullOrEmpty(json));
            var copy = ScriptableObject.CreateInstance<LessonGraph>();
            JsonUtility.FromJsonOverwrite(json, copy);

            // Schema & entry.
            Assert.AreEqual(1,        copy.SchemaVersion, "SchemaVersion");
            Assert.AreEqual(entryId,  copy.EntryNodeId,   "EntryNodeId");

            // Node count.
            Assert.AreEqual(2, copy.Nodes.Count, "Node count");

            // Node[0] — Quest.
            Assert.AreEqual(entryId,       copy.Nodes[0].Id,       "N0.Id");
            Assert.AreEqual(NodeType.Quest, copy.Nodes[0].NodeType, "N0.NodeType");
            Assert.AreEqual(nodePos,        copy.Nodes[0].Position, "N0.Position");
            var qc = copy.Nodes[0].Config as QuestNodeConfig;
            Assert.IsNotNull(qc,                                  "N0 config type");
            Assert.AreEqual(1,       qc.CompletionBindingIds.Count, "BindingIds.Count");
            Assert.AreEqual(binding, qc.CompletionBindingIds[0],    "BindingIds[0]");
            Assert.AreEqual(30f,     qc.TimeoutSeconds,             "Quest.TimeoutSeconds");
            Assert.AreEqual(prompt,  qc.VoicePrompt,                "Quest.VoicePrompt");

            // Node[1] — Checkpoint.
            Assert.AreEqual(cpId,                copy.Nodes[1].Id,       "N1.Id");
            Assert.AreEqual(NodeType.Checkpoint,  copy.Nodes[1].NodeType, "N1.NodeType");
            var cpc = copy.Nodes[1].Config as CheckpointNodeConfig;
            Assert.IsNotNull(cpc,                            "N1 config type");
            Assert.AreEqual("cp-001", cpc.CheckpointId,     "Checkpoint.CheckpointId");
            Assert.IsTrue(cpc.EmitTelemetry,                 "Checkpoint.EmitTelemetry");

            // Edge.
            Assert.AreEqual(1, copy.Edges.Count, "Edge count");
            Assert.AreEqual(entryId,  copy.Edges[0].FromNodeId, "Edge.FromNodeId");
            Assert.AreEqual(cpId,     copy.Edges[0].ToNodeId,   "Edge.ToNodeId");
            Assert.AreEqual(edgePrio, copy.Edges[0].Priority,   "Edge.Priority");
            var sc = copy.Edges[0].Condition as StatusCondition;
            Assert.IsNotNull(sc,                                 "Edge condition type");
            Assert.AreEqual(StatusCondition.Success, sc.RequiredStatus, "Edge.RequiredStatus");

            // Post-deserialize validation.
            var post = LessonGraphValidator.Validate(copy);
            Assert.IsTrue(post.IsValid, "Post: " + post);

            Destroy(graph, copy);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Review round 1 — tests 16-28
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void T16_NodeTypeConfigMismatch_QuestWithWaitConfig_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest, new WaitNodeConfig(5f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NodeTypeConfigMismatch));
            Destroy(g);
        }

        [Test]
        public void T17_NodeTypeConfigMismatch_DialogueWithQuestConfig_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Dialogue,
                    new QuestNodeConfig(new List<string> { "bid" })),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NodeTypeConfigMismatch));
            Destroy(g);
        }

        [Test]
        public void T18_DialogueConfig_EmptySequenceId_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Dialogue,
                    new DialogueNodeConfig("", "Hello")),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidDialogueConfig));
            Destroy(g);
        }

        [Test]
        public void T19_DialogueConfig_EmptyText_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Dialogue,
                    new DialogueNodeConfig("seq-01", "")),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidDialogueConfig));
            Destroy(g);
        }

        [Test]
        public void T20_WaitConfig_NegativeDuration_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Wait, new WaitNodeConfig(-1f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidWaitConfig));
            Destroy(g);
        }

        [Test]
        public void T21_WaitConfig_NaNDuration_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Wait, new WaitNodeConfig(float.NaN)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidWaitConfig));
            Destroy(g);
        }

        [Test]
        public void T22_WaitConfig_ZeroDuration_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Wait, new WaitNodeConfig(0f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidWaitConfig));
            Destroy(g);
        }

        [Test]
        public void T23_CheckpointConfig_EmptyId_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Checkpoint,
                    new CheckpointNodeConfig("")),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidCheckpointConfig));
            Destroy(g);
        }

        [Test]
        public void T24_EdgeFromNodeId_NotInNodes_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1") },
                new List<LessonEdgeData> { Always("ghost", "n1") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.DanglingEdgeSource));
            Destroy(g);
        }

        [Test]
        public void T25_NullEdgeCondition_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1"), WaitNode("n2") },
                new List<LessonEdgeData>
                {
                    new LessonEdgeData("n1", "n2", null),
                });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NullEdgeCondition));
            Destroy(g);
        }

        [Test]
        public void T26_NullNodeId_DoesNotThrow_ReportsInvalidNodeId()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData>
                {
                    new LessonNodeData(null, NodeType.Quest,
                        new QuestNodeConfig(new List<string> { "bid" })),
                    QuestNode("n1"),
                });
            GraphValidationResult r = null;
            Assert.DoesNotThrow(() => r = LessonGraphValidator.Validate(g));
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidNodeId));
            Destroy(g);
        }

        [Test]
        public void T27_StatusCondition_InvalidValue_Fails()
        {
            var g = MakeGraph("n1",
                new List<LessonNodeData> { QuestNode("n1"), WaitNode("n2") },
                new List<LessonEdgeData>
                {
                    new LessonEdgeData("n1", "n2", new StatusCondition("WINNING")),
                });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidStatusValue));
            Destroy(g);
        }

        [Test]
        public void T28_SchemaVersionZero_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") },
                schemaVersion: 0);
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidSchemaVersion));
            Destroy(g);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Review round 2 — tests 29-38
        // ══════════════════════════════════════════════════════════════════════

        // ── Test 29: Null node entry in list reports NullNodeEntry ─────────────
        [Test]
        public void T29_NullNodeEntry_Fails()
        {
            var nodes = new List<LessonNodeData> { null, QuestNode("n1") };
            var g = MakeGraph("n1", nodes);
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NullNodeEntry));
            Destroy(g);
        }

        // ── Test 30: Null edge entry in list reports NullEdgeEntry ────────────
        [Test]
        public void T30_NullEdgeEntry_Fails()
        {
            var edges = new List<LessonEdgeData> { null };
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") }, edges);
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.NullEdgeEntry));
            Destroy(g);
        }

        // ── Test 31: Duplicate DialogueNodeConfig.SequenceId fails ────────────
        [Test]
        public void T31_DuplicateDialogueSequenceId_Fails()
        {
            var g = MakeGraph("d1",
                new List<LessonNodeData>
                {
                    DialogueNode("d1", seqId: "greeting-01", text: "Hello"),
                    DialogueNode("d2", seqId: "greeting-01", text: "Hi"),  // duplicate seqId
                },
                new List<LessonEdgeData> { Always("d1", "d2") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.DuplicateSequenceId));
            Destroy(g);
        }

        // ── Test 32: Unique SequenceIds across Dialogue nodes passes ──────────
        [Test]
        public void T32_UniqueDialogueSequenceIds_Passes()
        {
            var g = MakeGraph("d1",
                new List<LessonNodeData>
                {
                    DialogueNode("d1", seqId: "seq-a", text: "Hello"),
                    DialogueNode("d2", seqId: "seq-b", text: "World"),
                },
                new List<LessonEdgeData> { Always("d1", "d2") });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsTrue(r.IsValid, r.ToString());
            Destroy(g);
        }

        // ── Test 33: Quest timeout zero is invalid ─────────────────────────────
        [Test]
        public void T33_QuestTimeout_Zero_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "bid" }, timeoutSeconds: 0f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidTimeoutValue));
            Destroy(g);
        }

        // ── Test 34: Quest timeout -1 (no timeout) is valid ───────────────────
        [Test]
        public void T34_QuestTimeout_MinusOne_Passes()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Quest,
                    new QuestNodeConfig(new List<string> { "bid" }, timeoutSeconds: -1f)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsTrue(r.IsValid, r.ToString());
            Destroy(g);
        }

        // ── Test 35: Dialogue timeout infinite fails ───────────────────────────
        [Test]
        public void T35_DialogueTimeout_Infinite_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Dialogue,
                    new DialogueNodeConfig("seq-01", "Hello",
                        timeoutSeconds: float.PositiveInfinity)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidTimeoutValue));
            Destroy(g);
        }

        // ── Test 36: Wait duration infinite fails ─────────────────────────────
        [Test]
        public void T36_WaitDuration_Infinite_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData>
            {
                new LessonNodeData("n1", NodeType.Wait,
                    new WaitNodeConfig(float.PositiveInfinity)),
            });
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.InvalidWaitConfig));
            Destroy(g);
        }

        // ── Test 37: Schema version above CurrentSchemaVersion fails ──────────
        [Test]
        public void T37_SchemaVersion_AboveCurrent_Fails()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") },
                schemaVersion: LessonGraphValidator.CurrentSchemaVersion + 1);
            var r = LessonGraphValidator.Validate(g);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(HasError(r, GraphValidationErrorCode.UnsupportedSchemaVersion));
            Destroy(g);
        }

        // ── Test 38: GraphValidationResult.Fail with empty list throws ─────────
        [Test]
        public void T38_GraphValidationResult_Fail_EmptyErrors_Throws()
        {
            var source = new List<GraphValidationError>
            {
                new GraphValidationError(GraphValidationErrorCode.InvalidSchemaVersion, "invalid"),
            };
            var result = GraphValidationResult.Fail(source);
            source.Clear();

            Assert.AreEqual(1, result.Errors.Count, "Fail must retain an immutable snapshot.");
            Assert.Throws<System.ArgumentException>(() =>
                GraphValidationResult.Fail(new List<GraphValidationError>()));
            Assert.Throws<System.ArgumentNullException>(() => GraphValidationResult.Fail(null));
        }

        // ── Test 39: Null Nodes collection fails without throwing ─────────────
        [Test]
        public void T39_NullNodesCollection_FailsWithoutThrowing()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") });
            SetPrivateGraphField<List<LessonNodeData>>(g, "_nodes", null);
            GraphValidationResult result = null;

            Assert.DoesNotThrow(() => result = LessonGraphValidator.Validate(g));
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, GraphValidationErrorCode.NullCollection));
            Destroy(g);
        }

        // ── Test 40: Null Edges collection fails without throwing ─────────────
        [Test]
        public void T40_NullEdgesCollection_FailsWithoutThrowing()
        {
            var g = MakeGraph("n1", new List<LessonNodeData> { QuestNode("n1") });
            SetPrivateGraphField<List<LessonEdgeData>>(g, "_edges", null);
            GraphValidationResult result = null;

            Assert.DoesNotThrow(() => result = LessonGraphValidator.Validate(g));
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, GraphValidationErrorCode.NullCollection));
            Destroy(g);
        }

        // ── Fake Phase 2 condition ─────────────────────────────────────────────
        [System.Serializable]
        private sealed class FakePhase2Condition : IEdgeCondition { }
    }
}
