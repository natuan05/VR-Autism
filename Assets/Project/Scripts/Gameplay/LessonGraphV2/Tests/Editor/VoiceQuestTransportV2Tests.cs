using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class VoiceQuestTransportV2Tests
    {
        private sealed class Client : ILiveKitDataPacketClientV2
        {
            public bool IsConnectedV2 { get; set; } = true;
            public event Action<byte[], string> DataReceivedV2;
            public event Action ReconnectedV2;
            public readonly List<string> Sent = new List<string>();
            public void PublishDataV2(byte[] data, string topic, bool reliable)
            {
                Assert.AreEqual(VoiceQuestTransportV2Constants.Topic, topic);
                Assert.IsTrue(reliable);
                Sent.Add(Encoding.UTF8.GetString(data));
            }
            public void Reconnect() => ReconnectedV2?.Invoke();
            public void Receive(string packet) => DataReceivedV2?.Invoke(Encoding.UTF8.GetBytes(packet), VoiceQuestTransportV2Constants.Topic);
        }
        private sealed class Router : INpcAudioRouterV2
        {
            public string ActiveNpcBindingId { get; private set; }
            public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source) { }
            public void UnregisterNpcAudioRoute(string npcBindingId) { }
            public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source)
            {
                source = null;
                return false;
            }
            public bool SetActiveNpcRoute(string npcBindingId)
            {
                ActiveNpcBindingId = npcBindingId;
                return true;
            }
        }

        private static void Pump(LiveKitVoiceQuestTransportV2 transport) => typeof(LiveKitVoiceQuestTransportV2)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(transport, null);

        [Test]
        public void OfflineCancellationResendsAndRejectsLateMatch()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                var signals = new List<VoiceQuestSignal>();
                transport.SignalReceived += signals.Add;
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsFalse(client.Sent[0].Contains("reason"));
                client.IsConnectedV2 = false;
                transport.CancelAsync("a", "physical_won", CancellationToken.None).GetAwaiter().GetResult();
                client.IsConnectedV2 = true;
                client.Reconnect();
                Assert.AreEqual(1, client.Sent.Count);
                Pump(transport);
                StringAssert.Contains("CANCEL_ACTIVE_QUEST", client.Sent[1]);
                Assert.IsFalse(client.Sent[1].Contains("phrases"));
                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_MATCHED\",\"activation_id\":\"a\"}");
                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_STATUS\",\"activation_id\":\"a\",\"status\":\"CANCELLED\"}");
                Pump(transport);
                Assert.AreEqual(1, signals.Count);
                Assert.AreEqual(VoiceQuestSignalType.Cancelled, signals[0].Type);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test]
        public void MatchWinsAndLaterTerminalStatusesAreIgnored()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                var signals = new List<VoiceQuestSignal>();
                transport.SignalReceived += signals.Add;
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_MATCHED\",\"activation_id\":\"a\"}");
                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_STATUS\",\"activation_id\":\"a\",\"status\":\"FAILED\"}");
                Assert.IsEmpty(signals);
                Pump(transport);
                Assert.AreEqual(1, signals.Count);
                Assert.AreEqual(VoiceQuestSignalType.Matched, signals[0].Type);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ActivateAsync_SetsActiveNpcRouteAndSerializesNpcBindingId()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                var router = new Router();
                transport.Configure(client, router);
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual("teacher-npc", router.ActiveNpcBindingId);
                Assert.AreEqual(1, client.Sent.Count);
                StringAssert.Contains("\"npc_binding_id\":\"teacher-npc\"", client.Sent[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void VerbalHintPublishesCorrelatedPacketOnVoiceTopic()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation("activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();

                var sent = transport.SendVerbalHintAsync(
                    new VoiceQuestVerbalHint("activation-1", "hint-1", "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();

                Assert.That(sent, Is.True);
                Assert.That(client.Sent, Has.Count.EqualTo(2));
                StringAssert.Contains("\"event\":\"VERBAL_HINT\"", client.Sent[1]);
                StringAssert.Contains("\"contract_version\":2", client.Sent[1]);
                StringAssert.Contains("\"activation_id\":\"activation-1\"", client.Sent[1]);
                StringAssert.Contains("\"command_id\":\"hint-1\"", client.Sent[1]);
                StringAssert.Contains("\"npc_binding_id\":\"teacher-npc\"", client.Sent[1]);
                Assert.That(client.Sent[1], Does.Not.Contain("phrases"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void DisconnectedVerbalHintReturnsFalseAndIsNotReplayedOnReconnect()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation("activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                client.IsConnectedV2 = false;

                var sent = transport.SendVerbalHintAsync(
                    new VoiceQuestVerbalHint("activation-1", "hint-offline", "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                client.IsConnectedV2 = true;
                client.Reconnect();
                Pump(transport);

                Assert.That(sent, Is.False);
                Assert.That(client.Sent, Has.Count.EqualTo(2), "Only desired activation state is reconciled after reconnect.");
                StringAssert.Contains("SET_ACTIVE_QUEST", client.Sent[1]);
                Assert.That(client.Sent[1], Does.Not.Contain("VERBAL_HINT"));
                Assert.That(client.Sent[1], Does.Not.Contain("hint-offline"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
