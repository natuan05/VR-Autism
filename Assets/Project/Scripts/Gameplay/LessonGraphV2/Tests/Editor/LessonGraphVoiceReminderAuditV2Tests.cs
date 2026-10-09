using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphVoiceReminderAuditV2Tests
    {
        [Test]
        public void AcceptedReminderEmitsOneCorrelatedAuditDecisionForTheExactVoiceBinding()
        {
            var gameObject = new GameObject("voice-reminder-audit-test");
            gameObject.SetActive(false);
            try
            {
                var runner = gameObject.AddComponent<LessonGraphRunner>();
                ConfigureActiveRunner(runner, "running", "activation-1");
                LessonCommandResultV2 observed = null;
                var emitted = 0;
                runner.CommandEvaluated += result => { observed = result; emitted++; };

                Assert.That(runner.RecordAcceptedVoiceReminder("activation-1", "teacher-npc", "reminder-1"), Is.True);
                Assert.That(runner.RecordAcceptedVoiceReminder("activation-1", "teacher-npc", "reminder-1"), Is.False);

                Assert.That(emitted, Is.EqualTo(1));
                Assert.That(observed.command, Is.EqualTo(LessonCommandKindV2.VerbalHint));
                Assert.That(observed.command_id, Is.EqualTo("reminder-1"));
                Assert.That(observed.activation_id, Is.EqualTo("activation-1"));
                Assert.That(observed.binding_id, Is.EqualTo("voice-binding"));
                Assert.That(observed.accepted, Is.True);
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        [TestCase("paused", "activation-1", "teacher-npc")]
        [TestCase("running", "stale-activation", "teacher-npc")]
        [TestCase("running", "activation-1", "other-npc")]
        public void ReminderEvidenceIsRejectedWhenPausedStaleOrWrongNpc(string status, string activationId, string npcBindingId)
        {
            var gameObject = new GameObject("voice-reminder-audit-test");
            gameObject.SetActive(false);
            try
            {
                var runner = gameObject.AddComponent<LessonGraphRunner>();
                ConfigureActiveRunner(runner, status, "activation-1");
                var emitted = 0;
                runner.CommandEvaluated += _ => emitted++;

                Assert.That(runner.RecordAcceptedVoiceReminder(activationId, npcBindingId, "reminder-1"), Is.False);
                Assert.That(emitted, Is.Zero);
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        private static void ConfigureActiveRunner(LessonGraphRunner runner, string status, string activationId)
        {
            Set(runner, "_sessionContext", new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 0, 0));
            Set(runner, "_activeRunId", "run-1");
            Set(runner, "_activeActivationId", activationId);
            Set(runner, "_executorReady", true);
            Set(runner, "_nodeResultCommitted", false);
            Set(runner, "_currentState", new LessonStateV2
            {
                contract_version = 2,
                session_id = "session-1",
                run_id = "run-1",
                node_id = "quest-1",
                activation_id = activationId,
                status = status,
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

        private static void Set(object target, string field, object value) =>
            typeof(LessonGraphRunner).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
