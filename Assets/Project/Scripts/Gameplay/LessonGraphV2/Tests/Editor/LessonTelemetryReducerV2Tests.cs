using System;
using System.Linq;
using NUnit.Framework;
using VRAutism.Cloud.Models;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonTelemetryReducerV2Tests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

        private static LessonTelemetryContextV2 Context(string sessionId = "session-1", string runId = "run-1") =>
            new LessonTelemetryContextV2(sessionId, runId, "graph-1", "lesson-1", "launch-1", 4, 7);

        private static LessonTelemetryNodeV2 Node(string activationId = "activation-1", string nodeId = "quest-1") =>
            new LessonTelemetryNodeV2(nodeId, "Quest", "Wash hands", 2, activationId);

        private static LessonStateV2 State(string status, string activationId = "activation-1",
            string sessionId = "session-1", string runId = "run-1", string nodeId = "quest-1",
            string nodeType = "Quest")
        {
            return new LessonStateV2
            {
                contract_version = 2,
                session_id = sessionId,
                run_id = runId,
                graph_id = "graph-1",
                lesson_id = "lesson-1",
                launch_token = "launch-1",
                lesson_voice_revision = 4,
                child_phrase_revision = 7,
                node_id = nodeId,
                node_type = nodeType,
                node_index = 2,
                activation_id = activationId,
                status = status,
                updated_at_utc = T0.ToString("O"),
                state_revision = 1,
                active_node_ids = status == "running" ? new[] { nodeId } : new string[0],
                parallel_group_id = string.Empty,
                bindings = new LessonBindingV2[0]
            };
        }

        [Test]
        public void Observe_NodeCompletion_UsesMonotonicDurationAndCapturedUtcTimestamps()
        {
            var reducer = new LessonTelemetryReducerV2();
            var entered = LessonLifecycleEventV2.NodeEntered(
                Context(), Node(), T0, 10d, State("running"));
            var completed = LessonLifecycleEventV2.NodeCompleted(
                Context(), Node(), "success", "touch", T0.AddSeconds(9), 14d, State("completed"));

            reducer.Observe(entered);
            var batch = reducer.Observe(completed);

            Assert.That(batch.node_logs_by_id.Count, Is.EqualTo(1));
            NodeLogData log = null;
            foreach (var value in batch.node_logs_by_id.Values) log = value;
            Assert.That(log.duration_seconds, Is.EqualTo(4d));
            Assert.That(log.entered_at_utc, Is.EqualTo(T0.ToString("O")));
            Assert.That(log.exited_at_utc, Is.EqualTo(T0.AddSeconds(9).ToString("O")));
            Assert.That(log.status, Is.EqualTo("success"));
            Assert.That(log.completion_channel, Is.EqualTo("touch"));
            Assert.That(log.elapsed_seconds, Is.EqualTo(14d));
        }

        [Test]
        public void Observe_NodeLogKeepsLaunchContextCapturedAtEntry()
        {
            var reducer = new LessonTelemetryReducerV2();
            var entryContext = new LessonTelemetryContextV2(
                "session-1", "run-1", "graph-entry", "lesson-entry", "launch-entry", 4, 7);
            var terminalContext = new LessonTelemetryContextV2(
                "session-1", "run-1", "graph-current", "lesson-current", "launch-current", 9, 11);
            var node = Node();
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                entryContext, node, T0, 10d, State("running")));

            var terminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                terminalContext, node, "success", "touch", T0.AddSeconds(4), 14d, State("completed")));
            NodeLogData log = terminal.node_logs_by_id.Values.Single();

            Assert.That(log.graph_id, Is.EqualTo("graph-entry"));
            Assert.That(log.lesson_id, Is.EqualTo("lesson-entry"));
            Assert.That(log.launch_token, Is.EqualTo("launch-entry"));
            Assert.That(log.lesson_voice_revision, Is.EqualTo(4));
            Assert.That(log.child_phrase_revision, Is.EqualTo(7));
        }

        [Test]
        public void Observe_ReplayedLifecycleEvents_KeepStableIdsAndDoNotDuplicateWrites()
        {
            var context = Context();
            var node = Node();
            var entered = LessonLifecycleEventV2.NodeEntered(context, node, T0, 10d, State("running"));
            var completed = LessonLifecycleEventV2.NodeCompleted(
                context, node, "success", "touch", T0.AddSeconds(9), 14d, State("completed"));
            var firstReducer = new LessonTelemetryReducerV2();
            firstReducer.Observe(entered);
            var first = firstReducer.Observe(completed);

            var replay = firstReducer.Observe(completed);
            var replayReducer = new LessonTelemetryReducerV2();
            replayReducer.Observe(entered);
            var second = replayReducer.Observe(completed);

            Assert.That(first.audit_events_by_id.Count, Is.EqualTo(1));
            Assert.That(first.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(replay.audit_events_by_id.Count, Is.Zero);
            Assert.That(replay.node_logs_by_id.Count, Is.Zero);
            CollectionAssert.AreEquivalent(first.audit_events_by_id.Keys, second.audit_events_by_id.Keys);
            CollectionAssert.AreEquivalent(first.node_logs_by_id.Keys, second.node_logs_by_id.Keys);
        }

        [Test]
        public void Observe_PauseResume_AggregatesAttemptsAndCountsOnlyUniqueAcceptedHints()
        {
            var reducer = new LessonTelemetryReducerV2();
            var context = Context();
            var firstNode = Node("activation-1");
            var secondNode = Node("activation-2");

            reducer.Observe(LessonLifecycleEventV2.NodeEntered(context, firstNode, T0, 10d, State("running")));
            var paused = reducer.Observe(LessonLifecycleEventV2.NodeCancelled(
                context, firstNode, "pause", T0.AddSeconds(2), 12d, State("paused")));
            Assert.That(paused.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(paused.quest_logs_by_id.Count, Is.Zero);

            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                context, secondNode, T0.AddSeconds(10), 20d, State("running", "activation-2")));
            var hint = LessonLifecycleEventV2.CommandDisposition(
                context, secondNode, "cmd-visual-1", "VISUAL_HINT", "soap-touch",
                true, "NONE", T0.AddSeconds(11), 21d, State("running", "activation-2"));
            var accepted = reducer.Observe(hint);
            var duplicate = reducer.Observe(LessonLifecycleEventV2.CommandDisposition(
                context, secondNode, "cmd-visual-1", "VISUAL_HINT", "soap-touch",
                false, "DUPLICATE", T0.AddSeconds(12), 22d, State("running", "activation-2")));
            var terminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                context, secondNode, "success", "touch", T0.AddSeconds(14), 24d,
                State("completed", "activation-2")));

            Assert.That(accepted.audit_events_by_id.Count, Is.EqualTo(1));
            Assert.That(duplicate.audit_events_by_id.Count, Is.EqualTo(1));
            Assert.That(terminal.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(terminal.quest_logs_by_id.Count, Is.EqualTo(1));
            QuestLogData quest = null;
            foreach (var value in terminal.quest_logs_by_id.Values) quest = value;
            Assert.That(quest.response_time, Is.EqualTo(6d));
            Assert.That(quest.completion_status, Is.EqualTo("assisted"));
            Assert.That(quest.hints_visual, Is.EqualTo(1));
            Assert.That(quest.hints_verbal, Is.Zero);
            Assert.That(quest.response_time_from_hint, Is.EqualTo(3d));
        }

        [Test]
        public void Observe_RejectedHintDoesNotCountAndCommandRejectionDoesNotProjectState()
        {
            var reducer = new LessonTelemetryReducerV2();
            var context = Context();
            var node = Node();
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(context, node, T0, 10d, State("running")));

            var rejected = reducer.Observe(LessonLifecycleEventV2.CommandDisposition(
                context, node, "cmd-verbal-1", "VERBAL_HINT", "npc-1",
                false, "UNSUPPORTED_CAPABILITY", T0.AddSeconds(1), 11d, State("running")));

            Assert.That(rejected.state_projection, Is.Null);
            Assert.That(rejected.audit_events_by_id.Count, Is.EqualTo(1));

            var terminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                context, node, "success", "touch", T0.AddSeconds(2), 12d, State("completed")));
            QuestLogData quest = null;
            foreach (var value in terminal.quest_logs_by_id.Values) quest = value;
            Assert.That(quest.hints_verbal, Is.Zero);
            Assert.That(quest.hints_visual, Is.Zero);
            Assert.That(quest.completion_status, Is.EqualTo("success"));
            Assert.That(quest.response_time_from_hint, Is.EqualTo(-1d));
        }

        [Test]
        public void Observe_NonQuestNodeCompletionWritesNodeLogWithoutQuestCompatibilityLog()
        {
            var reducer = new LessonTelemetryReducerV2();
            var context = Context();
            var node = new LessonTelemetryNodeV2("dialogue-1", "Dialogue", "Opening", 0, "activation-1");
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                context, node, T0, 10d, State("running", nodeId: "dialogue-1", nodeType: "Dialogue")));

            var terminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                context, node, "success", "dialogue", T0.AddSeconds(2), 12d,
                State("completed", nodeId: "dialogue-1", nodeType: "Dialogue")));

            Assert.That(terminal.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(terminal.quest_logs_by_id, Is.Empty);
        }

        [Test]
        public void Observe_LessonCancellationAfterNodeCancellation_FinalizesOneFailedQuestLog()
        {
            var reducer = new LessonTelemetryReducerV2();
            var context = Context();
            var node = Node();
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(context, node, T0, 10d, State("running")));
            var cancelled = reducer.Observe(LessonLifecycleEventV2.NodeCancelled(
                context, node, "lesson-abort", T0.AddSeconds(3), 13d, State("cancelled")));
            Assert.That(cancelled.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(cancelled.quest_logs_by_id, Is.Empty);

            var terminal = reducer.Observe(LessonLifecycleEventV2.LessonTerminated(
                context, "cancelled", T0.AddSeconds(4), 14d, State("cancelled")));

            Assert.That(terminal.quest_logs_by_id.Count, Is.EqualTo(1));
            QuestLogData quest = null;
            foreach (var value in terminal.quest_logs_by_id.Values) quest = value;
            Assert.That(quest.completion_status, Is.EqualTo("failed"));
            Assert.That(quest.response_time, Is.EqualTo(3d));
        }

        [Test]
        public void Observe_LessonTerminated_ClosesOnlyMatchingSessionAndRun()
        {
            var reducer = new LessonTelemetryReducerV2();
            var sameSessionRun = Context("session-1", "run-1");
            var differentSession = Context("session-2", "run-1");
            var differentRun = Context("session-1", "run-2");
            var node1 = Node("activation-1");
            var node2 = Node("activation-1");
            var node3 = Node("activation-1");

            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                sameSessionRun, node1, T0, 10d, State("running")));
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                differentSession, node2, T0.AddSeconds(1), 20d,
                State("running", "activation-1", "session-2", "run-1")));
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                differentRun, node3, T0.AddSeconds(2), 30d,
                State("running", "activation-1", "session-1", "run-2")));

            var firstTerminal = reducer.Observe(LessonLifecycleEventV2.LessonTerminated(
                sameSessionRun, "cancelled", T0.AddSeconds(3), 13d,
                State("cancelled", "activation-1", "session-1", "run-1")));

            Assert.That(firstTerminal.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(firstTerminal.quest_logs_by_id.Count, Is.EqualTo(1));

            var secondTerminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                differentSession, node2, "success", "touch", T0.AddSeconds(5), 24d,
                State("completed", "activation-1", "session-2", "run-1")));
            var thirdTerminal = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                differentRun, node3, "success", "touch", T0.AddSeconds(6), 34d,
                State("completed", "activation-1", "session-1", "run-2")));

            Assert.That(secondTerminal.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(secondTerminal.quest_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(thirdTerminal.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(thirdTerminal.quest_logs_by_id.Count, Is.EqualTo(1));
        }

        [Test]
        public void Observe_IdentityValuesContainingOldSeparator_RemainDistinct()
        {
            var reducer = new LessonTelemetryReducerV2();
            var firstContext = Context("session-1", "run\u001fnode");
            var secondContext = Context("session-1", "run");
            var firstNode = Node("activation", "id");
            var secondNode = Node("node\u001factivation", "node\u001fid");

            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                firstContext, firstNode, T0, 10d, State("running", "activation", "session-1", "run\u001fnode", "id")));
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                secondContext, secondNode, T0.AddSeconds(1), 20d,
                State("running", "node\u001factivation", "session-1", "run", "node\u001fid")));

            var first = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                firstContext, firstNode, "success", "touch", T0.AddSeconds(2), 12d,
                State("completed", "activation", "session-1", "run\u001fnode", "id")));
            var second = reducer.Observe(LessonLifecycleEventV2.NodeCompleted(
                secondContext, secondNode, "success", "touch", T0.AddSeconds(5), 24d,
                State("completed", "node\u001factivation", "session-1", "run", "node\u001fid")));

            Assert.That(first.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(second.node_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(first.quest_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(second.quest_logs_by_id.Count, Is.EqualTo(1));
            Assert.That(first.node_logs_by_id.Values.Single().duration_seconds, Is.EqualTo(2d));
            Assert.That(second.node_logs_by_id.Values.Single().duration_seconds, Is.EqualTo(4d));
        }

        [TestCase("timeout", "timeout")]
        [TestCase("skip", "skipped")]
        [TestCase("lesson-abort", "cancelled")]
        public void Observe_NodeCancelledReasonDeterminesNodeLogStatus(string reason, string expectedStatus)
        {
            var reducer = new LessonTelemetryReducerV2();
            var context = Context();
            var node = Node();
            reducer.Observe(LessonLifecycleEventV2.NodeEntered(
                context, node, T0, 10d, State("running")));

            var cancelled = reducer.Observe(LessonLifecycleEventV2.NodeCancelled(
                context, node, reason, T0.AddSeconds(2), 12d, State("cancelled")));
            NodeLogData log = cancelled.node_logs_by_id.Values.Single();

            Assert.That(log.status, Is.EqualTo(expectedStatus));
        }

        [TestCase("skipped", "skipped")]
        [TestCase("timeout", "failed")]
        [TestCase("failed", "failed")]
        [TestCase("cancelled", "failed")]
        public void CompatibilityMapper_MapsTerminalStatusToLegacyStatus(string nodeStatus, string expectedStatus)
        {
            var log = new NodeLogData
            {
                node_id = "quest-1",
                node_type = "Quest",
                node_name = "Wash hands",
                node_index = 2,
                status = nodeStatus
            };

            QuestLogData quest = QuestLogCompatibilityMapperV2.Map(log, new QuestHintSummaryV2());

            Assert.That(quest.completion_status, Is.EqualTo(expectedStatus));
        }
    }
}
