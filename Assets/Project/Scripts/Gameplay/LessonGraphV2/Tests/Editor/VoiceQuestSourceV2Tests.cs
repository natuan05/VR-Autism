using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Questing.Sources;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class VoiceQuestSourceV2Tests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            VoicePhraseSnapshotStoreV2.Clear();
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }

        [Test]
        public void MatchedSignal_CompletesSourceOnce()
        {
            var source = SourceWithSnapshot();
            var completions = 0;
            source.Terminated += _ => completions++;

            Assert.IsTrue(source.TryActivate(Activation("voice-1")));
            Invoke(source, "OnSignal", new VoiceQuestSignal("voice-1", VoiceQuestSignalType.Matched));
            Invoke(source, "OnSignal", new VoiceQuestSignal("voice-1", VoiceQuestSignalType.Matched));

            Assert.AreEqual(QuestSourceState.Completed, source.State);
            Assert.AreEqual(1, completions);
        }

        [Test]
        public void FailedSignal_EmitsFailedResultWithReason()
        {
            var source = SourceWithSnapshot();
            QuestSourceResult result = null;
            source.Terminated += emitted => result = emitted;

            Assert.IsTrue(source.TryActivate(Activation("voice-2")));
            Invoke(source, "OnSignal", new VoiceQuestSignal("voice-2", VoiceQuestSignalType.Failed, "agent_error"));

            Assert.NotNull(result);
            Assert.AreEqual(QuestSourceTerminalStatus.Failed, result.Status);
            Assert.AreEqual("agent_error", result.FailureCode);
        }

        [Test]
        public void MissingSnapshot_FailsActivation()
        {
            var source = SourceWithoutSnapshot();
            Assert.IsTrue(source.TryActivate(Activation("voice-3")));
            Assert.AreEqual(QuestSourceState.Failed, source.State);
        }

        [Test]
        public void NpcBindingId_DefaultsToTeacherNpc_AndCanBeConfigured()
        {
            var source = SourceWithoutSnapshot();
            Assert.AreEqual("teacher-npc", source.NpcBindingId);
            source.ConfigureNpcBindingId("peer-npc");
            Assert.AreEqual("peer-npc", source.NpcBindingId);
        }

        [Test]
        public void VerbalHintUsesInjectedTransportAndAuthoritativeNpcBinding_AndRejectsStaleActivation()
        {
            var source = SourceWithSnapshot();
            var transport = new FakeTransport();
            source.ConfigureTransport(transport);
            source.ConfigureNpcBindingId("assistant-npc");
            Assert.IsTrue(source.TryActivate(Activation("voice-hint-1")));

            var sent = source.SendVerbalHintAsync("voice-hint-1", "hint-1", CancellationToken.None).GetAwaiter().GetResult();
            var stale = source.SendVerbalHintAsync("old-activation", "hint-stale", CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(sent);
            Assert.IsFalse(stale);
            Assert.AreEqual(1, transport.VerbalHints.Count);
            Assert.AreEqual("voice-hint-1", transport.VerbalHints[0].activation_id);
            Assert.AreEqual("hint-1", transport.VerbalHints[0].command_id);
            Assert.AreEqual("assistant-npc", transport.VerbalHints[0].npc_binding_id);
        }

        private VoiceQuestSourceV2 SourceWithSnapshot()
        {
            VoicePhraseSnapshotStoreV2.Replace(new Dictionary<string, VoiceQuestPhraseSnapshotV2>
            {
                ["voice"] = new VoiceQuestPhraseSnapshotV2("voice", "Ask", new[] { "Please" })
            });
            return SourceWithoutSnapshot();
        }

        private VoiceQuestSourceV2 SourceWithoutSnapshot()
        {
            var go = new GameObject("voice-source-test");
            go.SetActive(false);
            _objects.Add(go);
            var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
            var source = go.AddComponent<VoiceQuestSourceV2>();
            Set(typeof(QuestSourceV2), source, "_bindingId", "voice");
            Set(typeof(VoiceQuestSourceV2), source, "_transport", transport);
            typeof(VoiceQuestSourceV2).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);
            go.SetActive(true);
            return source;
        }

        private static QuestSourceActivation Activation(string id) =>
            new QuestSourceActivation(id, DateTimeOffset.UtcNow, 0d);

        private static void Set(Type type, object target, string field, object value) =>
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Invoke(object target, string method, object argument) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });

        private sealed class FakeTransport : IVoiceQuestTransport
        {
            public event Action<VoiceQuestSignal> SignalReceived { add { } remove { } }
            public readonly List<VoiceQuestVerbalHint> VerbalHints = new List<VoiceQuestVerbalHint>();

            public Task ActivateAsync(VoiceQuestActivation request, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> SendVerbalHintAsync(VoiceQuestVerbalHint request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerbalHints.Add(request);
                return Task.FromResult(true);
            }
        }
    }
}
