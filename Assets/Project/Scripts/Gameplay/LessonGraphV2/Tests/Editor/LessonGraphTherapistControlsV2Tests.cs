using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphTherapistControlsV2Tests
    {
        private SessionContext _previousSession;
        private GameObject _sessionObject;
        private GameObject _runnerObject;
        private LessonGraphRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _previousSession = SessionContext.Instance;
            _sessionObject = new GameObject("therapist-control-session");
            _sessionObject.SetActive(false);
            SessionContext.Instance = _sessionObject.AddComponent<SessionContext>();

            _runnerObject = new GameObject("therapist-control-runner");
            _runnerObject.SetActive(false);
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            Set("_sessionContext", new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 0, 0));
            Set("_activeRunId", "run-1");
            Set("_activeActivationId", "activation-1");
            Set("_executorReady", true);
            Set("_nodeResultCommitted", false);
            Set("_currentState", new LessonStateV2
            {
                contract_version = 2,
                session_id = "session-1",
                run_id = "run-1",
                node_id = "quest-1",
                activation_id = "activation-1",
                status = "running",
                state_revision = 7,
                bindings = new[]
                {
                    new LessonBindingV2
                    {
                        binding_id = "voice-binding",
                        npc_binding_id = "teacher-npc",
                        can_verbal_hint = true,
                    }
                }
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (_runnerObject != null) Object.DestroyImmediate(_runnerObject);
            if (_sessionObject != null) Object.DestroyImmediate(_sessionObject);
            SessionContext.Instance = _previousSession;
        }

        [Test]
        public void SetVolumeAppliesSupportedValueWithoutAdvancingLesson()
        {
            var before = _runner.CurrentState;
            var command = Command("volume-1", LessonCommandKindV2.SetVolume);
            command.volume = 0.65f;

            var result = _runner.ApplyCommandAsync(command).GetAwaiter().GetResult();

            Assert.That(result.accepted, Is.True);
            Assert.That(SessionContext.Instance.MaxVolume, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(result.state.run_id, Is.EqualTo(before.run_id));
            Assert.That(result.state.node_id, Is.EqualTo(before.node_id));
            Assert.That(result.state.activation_id, Is.EqualTo(before.activation_id));
            Assert.That(result.state.state_revision, Is.EqualTo(before.state_revision));
        }

        [Test]
        public void SpeakScriptRequiresExactVoiceBindingAndDoesNotAdvanceLesson()
        {
            var before = _runner.CurrentState;
            var command = Command("script-1", LessonCommandKindV2.SpeakScript);
            command.npc_binding_id = "teacher-npc";
            command.text = "Ask for help.";
            var publishes = 0;

            var result = _runner.ApplyCommandAsync(command, _ =>
            {
                publishes++;
                return true;
            }).GetAwaiter().GetResult();

            Assert.That(result.accepted, Is.True);
            Assert.That(result.state.run_id, Is.EqualTo(before.run_id));
            Assert.That(result.state.node_id, Is.EqualTo(before.node_id));
            Assert.That(result.state.activation_id, Is.EqualTo(before.activation_id));
            Assert.That(result.state.state_revision, Is.EqualTo(before.state_revision));
            var duplicateResult = _runner.ApplyCommandAsync(command, _ =>
            {
                publishes++;
                return true;
            }).GetAwaiter().GetResult();
            Assert.That(duplicateResult.accepted, Is.False);
            Assert.That(duplicateResult.reason, Is.EqualTo(LessonCommandReasonV2.Duplicate));
            Assert.That(publishes, Is.EqualTo(1));

            var duplicateNpc = _runner.CurrentState;
            duplicateNpc.bindings = new[]
            {
                before.bindings[0],
                new LessonBindingV2
                {
                    binding_id = "voice-binding-2",
                    npc_binding_id = "teacher-npc",
                    can_verbal_hint = true,
                }
            };
            Set("_currentState", duplicateNpc);
            var ambiguous = Command("script-2", LessonCommandKindV2.SpeakScript);
            ambiguous.npc_binding_id = "teacher-npc";
            ambiguous.text = "Please try again.";

            var rejected = _runner.ApplyCommandAsync(ambiguous).GetAwaiter().GetResult();
            Assert.That(rejected.accepted, Is.False);
            Assert.That(rejected.reason, Is.EqualTo(LessonCommandReasonV2.WrongBinding));
        }

        private LessonCommandV2 Command(string id, string kind) => new LessonCommandV2
        {
            contract_version = LessonRemoteContractV2.ContractVersion,
            @event = LessonRemoteContractV2.CommandEvent,
            command_id = id,
            session_id = "session-1",
            run_id = "run-1",
            node_id = "quest-1",
            activation_id = "activation-1",
            command = kind,
            binding_id = string.Empty
        };

        private void Set(string field, object value) =>
            typeof(LessonGraphRunner).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_runner, value);
    }
}
