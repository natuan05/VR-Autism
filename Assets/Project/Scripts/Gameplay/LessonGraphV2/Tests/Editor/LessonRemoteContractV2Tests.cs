using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonRemoteContractV2Tests
    {
        private const string ValidCommandPrefix = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"cmd-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"";
        private const string StateFixture = "{\"contract_version\":2,\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"graph_id\":\"graph-1\",\"lesson_id\":\"lesson-1\",\"launch_token\":\"launch-1\",\"lesson_voice_revision\":3,\"child_phrase_revision\":4,\"node_id\":\"quest-1\",\"node_type\":\"Quest\",\"node_index\":0,\"activation_id\":\"activation-1\",\"status\":\"running\",\"checkpoint_id\":\"\",\"updated_at_utc\":\"2026-09-24T00:00:00Z\",\"state_revision\":1,\"active_node_ids\":[\"quest-1\"],\"parallel_group_id\":\"\",\"bindings\":[{\"binding_id\":\"soap-touch\",\"npc_binding_id\":\"teacher-npc\",\"can_verbal_hint\":true,\"can_visual_hint\":true}]}";

        [TestCase("SKIP")]
        [TestCase("PAUSE")]
        [TestCase("RESUME")]
        [TestCase("VERBAL_HINT")]
        [TestCase("VISUAL_HINT")]
        public void TryParse_AcceptsEachCommandKind(string kind)
        {
            LessonCommandV2 command;
            string reason;
            var hint = kind == LessonCommandKindV2.VerbalHint || kind == LessonCommandKindV2.VisualHint;
            var json = ValidCommandPrefix + kind + "\",\"binding_id\":\"" + (hint ? "soap-touch" : "") + "\"}";

            Assert.IsTrue(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
            Assert.AreEqual(LessonCommandReasonV2.None, reason);
            Assert.AreEqual(kind, command.command);
            Assert.AreEqual("session-1", command.session_id);
            Assert.AreEqual(hint ? "soap-touch" : "", command.binding_id);
        }

        [Test]
        public void TryParse_AcceptsSharedLiteralCommandFixture()
        {
            const string fixture = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"cmd-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"VISUAL_HINT\",\"binding_id\":\"soap-touch\"}";
            LessonCommandV2 command;
            string reason;
            Assert.IsTrue(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(fixture), out command, out reason));
            Assert.AreEqual("cmd-1", command.command_id);
            Assert.AreEqual(LessonCommandKindV2.VisualHint, command.command);
            Assert.AreEqual(fixture, JsonUtility.ToJson(command));
        }

        [Test]
        public void TryParse_AcceptsBoundedTherapistVolumeAndScriptPayloads()
        {
            const string volumeJson = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"volume-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SET_VOLUME\",\"binding_id\":\"\",\"volume\":0.65}";
            const string scriptJson = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"script-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SPEAK_SCRIPT\",\"binding_id\":\"\",\"npc_binding_id\":\"teacher-npc\",\"text\":\"Ask for help.\"}";
            LessonCommandV2 command;
            string reason;

            Assert.IsTrue(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(volumeJson), out command, out reason));
            Assert.AreEqual(LessonCommandKindV2.SetVolume, command.command);
            Assert.AreEqual(0.65f, command.volume, 0.0001f);
            Assert.IsTrue(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(scriptJson), out command, out reason));
            Assert.AreEqual(LessonCommandKindV2.SpeakScript, command.command);
            Assert.AreEqual("teacher-npc", command.npc_binding_id);
            Assert.AreEqual("Ask for help.", command.text);
        }

        [TestCase("volume", "-0.01")]
        [TestCase("volume", "1.01")]
        [TestCase("volume", "NaN")]
        public void TryParse_RejectsInvalidVolumePayload(string field, string value)
        {
            var json = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"volume-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SET_VOLUME\",\"binding_id\":\"\",\"volume\":0.5}";
            json = json.Replace("\"volume\":0.5", "\"" + field + "\":" + value);
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
        }

        [Test]
        public void TryParse_RejectsBlankAndOversizedScriptPayload()
        {
            var prefix = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"script-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SPEAK_SCRIPT\",\"binding_id\":\"\",\"npc_binding_id\":\"teacher-npc\",\"text\":\"";
            var suffix = "\"}";
            var blank = prefix + " \"" + suffix;
            var oversized = prefix + new string('x', 501) + suffix;
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(blank), out command, out reason));
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(oversized), out command, out reason));
        }

        [TestCase("{\"event\":\"LESSON_COMMAND\"}")]
        [TestCase("{\"contract_version\":\"2\"}")]
        [TestCase("{\"contract_version\":1}")]
        [TestCase("{\"contract_version\":2.0}")]
        [TestCase("{\"contract_version\":2,\"event\":\"LESSON_COMMAND\"")]
        [TestCase("{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"event\":\"LESSON_COMMAND\"}")]
        public void TryParse_RejectsInvalidEnvelope(string json)
        {
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
            Assert.IsNull(command);
            Assert.AreEqual(LessonCommandReasonV2.Malformed, reason);
        }

        [TestCase("command_id", " ")]
        [TestCase("session_id", " ")]
        [TestCase("run_id", " ")]
        [TestCase("node_id", " ")]
        [TestCase("activation_id", " ")]
        [TestCase("session_id", "session/1")]
        [TestCase("session_id", "session.1")]
        [TestCase("session_id", "session#1")]
        [TestCase("session_id", "session$1")]
        [TestCase("session_id", "session[1")]
        [TestCase("session_id", "session]1")]
        public void TryParse_RejectsBlankCorrelationAndUnsafeSessionPath(string field, string value)
        {
            var json = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"cmd-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SKIP\",\"binding_id\":\"\"}";
            json = json.Replace("\"" + field + "\":\"" + (field == "session_id" ? "session-1" : field == "command_id" ? "cmd-1" : field == "run_id" ? "run-1" : field == "node_id" ? "quest-1" : "activation-1") + "\"", "\"" + field + "\":\"" + value + "\"");
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
        }

        [TestCase("NOT_A_COMMAND", "soap-touch")]
        [TestCase("VERBAL_HINT", "")]
        [TestCase("VISUAL_HINT", "")]
        public void TryParse_RejectsUnknownCommandAndMissingHintBinding(string kind, string binding)
        {
            var json = ValidCommandPrefix + kind + "\",\"binding_id\":\"" + binding + "\"}";
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
        }

        [TestCase("contract_version", "\"2\"")]
        [TestCase("event", "2")]
        [TestCase("command_id", "true")]
        [TestCase("command", "null")]
        [TestCase("binding_id", "[]")]
        public void TryParse_RejectsWrongPropertyTypes(string field, string value)
        {
            var json = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"cmd-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SKIP\",\"binding_id\":\"\"}";
            var expected = field == "contract_version" ? "\"contract_version\":2" : "\"" + field + "\":\"" + (field == "event" ? "LESSON_COMMAND" : field == "command_id" ? "cmd-1" : field == "command" ? "SKIP" : "") + "\"";
            json = json.Replace(expected, "\"" + field + "\":" + value);
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
        }

        [Test]
        public void TryParse_RejectsInvalidUtf8AndNonObjectJson()
        {
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(new byte[] { 0xC3, 0x28 }, out command, out reason));
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes("[]"), out command, out reason));
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes("{"), out command, out reason));
        }

        [Test]
        public void TryParse_RejectsWhitespaceInsideUnicodeEscape()
        {
            const string json = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND\",\"command_id\":\"cmd\\u 041\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"SKIP\",\"binding_id\":\"\"}";
            LessonCommandV2 command;
            string reason;
            Assert.IsFalse(LessonCommandCodecV2.TryParse(Encoding.UTF8.GetBytes(json), out command, out reason));
            Assert.IsNull(command);
            Assert.AreEqual(LessonCommandReasonV2.Malformed, reason);
        }

        [Test]
        public void StateSerialization_MatchesSharedLiteralFixture()
        {
            var state = new LessonStateV2
            {
                contract_version = 2,
                session_id = "session-1",
                run_id = "run-1",
                graph_id = "graph-1",
                lesson_id = "lesson-1",
                launch_token = "launch-1",
                lesson_voice_revision = 3,
                child_phrase_revision = 4,
                node_id = "quest-1",
                node_type = "Quest",
                node_index = 0,
                activation_id = "activation-1",
                status = "running",
                checkpoint_id = "",
                updated_at_utc = "2026-09-24T00:00:00Z",
                state_revision = 1,
                active_node_ids = new[] { "quest-1" },
                parallel_group_id = "",
                bindings = new[] { new LessonBindingV2 { binding_id = "soap-touch", npc_binding_id = "teacher-npc", can_verbal_hint = true, can_visual_hint = true } }
            };
            Assert.AreEqual(StateFixture, JsonUtility.ToJson(state));
        }

        [Test]
        public void CommandResultSerialization_MatchesSharedLiteralFixtureWithNestedState()
        {
            const string resultFixture = "{\"contract_version\":2,\"event\":\"LESSON_COMMAND_RESULT\",\"command_id\":\"cmd-1\",\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"node_id\":\"quest-1\",\"activation_id\":\"activation-1\",\"command\":\"VISUAL_HINT\",\"binding_id\":\"soap-touch\",\"accepted\":true,\"reason\":\"NONE\",\"state\":{\"contract_version\":2,\"session_id\":\"session-1\",\"run_id\":\"run-1\",\"graph_id\":\"graph-1\",\"lesson_id\":\"lesson-1\",\"launch_token\":\"launch-1\",\"lesson_voice_revision\":3,\"child_phrase_revision\":4,\"node_id\":\"quest-1\",\"node_type\":\"Quest\",\"node_index\":0,\"activation_id\":\"activation-1\",\"status\":\"running\",\"checkpoint_id\":\"\",\"updated_at_utc\":\"2026-09-24T00:00:00Z\",\"state_revision\":1,\"active_node_ids\":[\"quest-1\"],\"parallel_group_id\":\"\",\"bindings\":[{\"binding_id\":\"soap-touch\",\"npc_binding_id\":\"teacher-npc\",\"can_verbal_hint\":true,\"can_visual_hint\":true}]}}";
            var result = new LessonCommandResultV2
            {
                contract_version = 2,
                @event = LessonRemoteContractV2.CommandResultEvent,
                command_id = "cmd-1",
                session_id = "session-1",
                run_id = "run-1",
                node_id = "quest-1",
                activation_id = "activation-1",
                command = LessonCommandKindV2.VisualHint,
                binding_id = "soap-touch",
                accepted = true,
                reason = LessonCommandReasonV2.None,
                state = JsonUtility.FromJson<LessonStateV2>(StateFixture)
            };

            Assert.AreEqual(resultFixture, JsonUtility.ToJson(result));
        }

        [Test]
        public void LessonSessionContext_IsImmutableAndValidatesSessionPathKey()
        {
            var context = new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 3, 4);
            Assert.AreEqual("session-1", context.SessionId);
            Assert.AreEqual("lesson-1", context.LessonId);
            Assert.AreEqual("launch-1", context.LaunchToken);
            Assert.AreEqual(3, context.LessonVoiceRevision);
            Assert.AreEqual(4, context.ChildPhraseRevision);
            Assert.Throws<ArgumentException>(() => new LessonSessionContextV2("session/1", "lesson-1", "launch-1", 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LessonSessionContextV2("session-1", "lesson-1", "launch-1", -1, 0));
        }
    }
}
