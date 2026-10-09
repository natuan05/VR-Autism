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
            public sealed class SentPacket
            {
                public string Json;
                public string Topic;
                public bool Reliable;
            }

            public bool IsConnectedV2 { get; set; } = true;
            public event Action<byte[], string> DataReceivedV2;
            public event Action ReconnectedV2;
            public readonly List<string> Sent = new List<string>();
            public readonly List<SentPacket> SentPackets = new List<SentPacket>();
            public void PublishDataV2(byte[] data, string topic, bool reliable)
            {
                var json = Encoding.UTF8.GetString(data);
                Sent.Add(json);
                SentPackets.Add(new SentPacket { Json = json, Topic = topic, Reliable = reliable });
            }
            public void Reconnect() => ReconnectedV2?.Invoke();
            public void Receive(string packet) => DataReceivedV2?.Invoke(Encoding.UTF8.GetBytes(packet), VoiceQuestTransportV2Constants.Topic);
            public void Receive(byte[] packet) => DataReceivedV2?.Invoke(packet, VoiceQuestTransportV2Constants.Topic);
        }
        private sealed class MicrophoneControl : ILiveKitMicrophoneControlV2
        {
            public readonly List<bool> States = new List<bool>();
            public void EnableMicrophone(bool enable) => States.Add(enable);
        }
        private sealed class Router : INpcAudioRouterV2
        {
            public bool CanSetActiveRoute { get; set; } = true;
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
                if (!CanSetActiveRoute) return false;
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
        public void ActivateAsync_SerializesEffectiveSilenceTimeout()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc", 2.5f), CancellationToken.None)
                    .GetAwaiter().GetResult();

                StringAssert.Contains("\"speech_silence_timeout_seconds\":2.5", client.Sent[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ActivateAsync_UsesFiveSecondSilenceTimeoutByDefault()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation("activation-1", "goal", new[] { "phrase" }), CancellationToken.None)
                    .GetAwaiter().GetResult();

                StringAssert.Contains("\"speech_silence_timeout_seconds\":5", client.Sent[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PublishSpeakScriptUsesTypedCorrelatedPacketAndActiveNpcRoute()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                var router = new Router();
                transport.Configure(client, router);
                transport.ActivateAsync(new VoiceQuestActivation("activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();

                Assert.That(transport.PublishSpeakScript("activation-1", "script-1", "teacher-npc", "Ask for help."), Is.True);
                Assert.That(transport.PublishSpeakScript("stale", "script-2", "teacher-npc", "Ignored."), Is.False);
                Assert.That(client.Sent, Has.Count.EqualTo(2));
                StringAssert.Contains("\"event\":\"SPEAK_SCRIPT\"", client.Sent[1]);
                StringAssert.Contains("\"sequence_id\":\"script-1\"", client.Sent[1]);
                StringAssert.Contains("\"activation_id\":\"activation-1\"", client.Sent[1]);
                StringAssert.Contains("\"npc_binding_id\":\"teacher-npc\"", client.Sent[1]);
                StringAssert.Contains("\"text\":\"Ask for help.\"", client.Sent[1]);
                Assert.That(router.ActiveNpcBindingId, Is.EqualTo("teacher-npc"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CancelSpeakScriptPublishesOnlyForTheExactTrackedScript()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client, new Router());
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(transport.PublishSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "Ask for help."), Is.True);

                Assert.That(transport.CancelSpeakScript(
                    "old-activation", "script-1", "teacher-npc", "lesson_scope_changed"), Is.False);
                Assert.That(client.Sent, Has.Count.EqualTo(2));
                Assert.That(transport.CancelSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "lesson_scope_changed"), Is.True);
                Assert.That(client.Sent, Has.Count.EqualTo(3));

                var sent = client.SentPackets[2];
                var packet = JsonUtility.FromJson<VoiceQuestCancelSpeakScriptV2>(sent.Json);
                Assert.That(sent.Topic, Is.EqualTo(VoiceQuestTransportV2Constants.Topic));
                Assert.That(sent.Reliable, Is.True);
                Assert.That(packet.contract_version, Is.EqualTo(2));
                Assert.That(packet.@event, Is.EqualTo("CANCEL_SPEAK_SCRIPT"));
                Assert.That(packet.activation_id, Is.EqualTo("activation-1"));
                Assert.That(packet.sequence_id, Is.EqualTo("script-1"));
                Assert.That(packet.npc_binding_id, Is.EqualTo("teacher-npc"));
                Assert.That(packet.reason, Is.EqualTo("lesson_scope_changed"));
                Assert.That(transport.CancelSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "lesson_scope_changed"), Is.False);
                Assert.That(client.Sent, Has.Count.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PublishSpeakScriptRejectsAnUnselectableNpcRouteBeforePublishing()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                var router = new Router();
                transport.Configure(client, router);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                router.CanSetActiveRoute = false;
                var sentBefore = client.Sent.Count;

                Assert.That(transport.PublishSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "Ask for help."), Is.False);
                Assert.That(transport.CancelSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "lesson_scope_changed"), Is.False,
                    "A rejected script must not become the active cancellation target.");
                Assert.That(client.Sent, Has.Count.EqualTo(sentBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PublishSpeakScriptRejectsWhenNoNpcRouterIsConfigured()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                var sentBefore = client.Sent.Count;

                Assert.That(transport.PublishSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "Ask for help."), Is.False);
                Assert.That(client.Sent, Has.Count.EqualTo(sentBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ReminderEvidenceRequiresTheExactTypedContract()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                var signals = new List<VoiceQuestSignal>();
                transport.SignalReceived += signals.Add;

                client.Receive("{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"extra\",\"status\":\"FAILED\"}");
                client.Receive("{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"first\",\"command\\u005fid\":\"second\"}");
                client.Receive("{\"contract_version\":\"2\",\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"wrong-type\"}");
                client.Receive("{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"valid\"}");
                Pump(transport);

                Assert.That(signals, Has.Count.EqualTo(1));
                Assert.That(signals[0].Type, Is.EqualTo(VoiceQuestSignalType.ReminderAccepted));
                Assert.That(signals[0].CommandId, Is.EqualTo("valid"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void EmptyInitialConfigureAndLifecycleReconfigurePreserveExactPendingCancellation()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client, new Router());
                Assert.IsEmpty(client.Sent, "Configuring before any script must not publish an empty cancellation.");
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(transport.PublishSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "Ask for help."), Is.True);

                client.IsConnectedV2 = false;
                Assert.That(transport.CancelSpeakScript(
                    "activation-1", "script-1", "teacher-npc", "lesson_scope_changed"), Is.True);
                var lifecycleFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnDisable", lifecycleFlags).Invoke(transport, null);
                transport.Configure(client);

                client.IsConnectedV2 = true;
                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnEnable", lifecycleFlags).Invoke(transport, null);

                Assert.That(client.SentPackets, Has.Count.EqualTo(4));
                var cancellation = JsonUtility.FromJson<VoiceQuestCancelSpeakScriptV2>(client.SentPackets[2].Json);
                Assert.That(cancellation.@event, Is.EqualTo("CANCEL_SPEAK_SCRIPT"));
                Assert.That(cancellation.activation_id, Is.EqualTo("activation-1"));
                Assert.That(cancellation.sequence_id, Is.EqualTo("script-1"));
                Assert.That(cancellation.npc_binding_id, Is.EqualTo("teacher-npc"));
                Assert.That(cancellation.reason, Is.EqualTo("lesson_scope_changed"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void NullAndEmptyIncomingPacketsAreIgnoredWithoutSignalsOrPublications()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                var signals = new List<VoiceQuestSignal>();
                transport.SignalReceived += signals.Add;
                var sentBefore = client.Sent.Count;

                Assert.DoesNotThrow(() => client.Receive((byte[])null));
                Assert.DoesNotThrow(() => client.Receive(Array.Empty<byte>()));
                Pump(transport);

                Assert.IsEmpty(signals);
                Assert.That(client.Sent, Has.Count.EqualTo(sentBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void AcceptedSilenceReminderIsCorrelatedDeduplicatedAndNeverRepublished()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                transport.Configure(client);
                transport.ActivateAsync(new VoiceQuestActivation(
                    "activation-1", "goal", new[] { "phrase" }, "teacher-npc"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                var signals = new List<VoiceQuestSignal>();
                transport.SignalReceived += signals.Add;
                const string reminder = "{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"reminder-1\"}";

                client.Receive(reminder);
                client.Receive(reminder);
                client.Receive("{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"agent_to_unity\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"other-npc\",\"command_id\":\"reminder-2\"}");
                client.Receive("{\"contract_version\":2,\"event\":\"ON_REMINDER\",\"direction\":\"unity_to_agent\",\"result\":\"accepted\",\"activation_id\":\"activation-1\",\"npc_binding_id\":\"teacher-npc\",\"command_id\":\"reminder-3\"}");
                Pump(transport);

                Assert.That(signals, Has.Count.EqualTo(1));
                Assert.That(signals[0].Type, Is.EqualTo(VoiceQuestSignalType.ReminderAccepted));
                Assert.That(signals[0].ActivationId, Is.EqualTo("activation-1"));
                Assert.That(signals[0].NpcBindingId, Is.EqualTo("teacher-npc"));
                Assert.That(signals[0].CommandId, Is.EqualTo("reminder-1"));
                Assert.That(client.Sent, Has.Count.EqualTo(1), "Reminder evidence is incoming and must not produce another prompt packet.");
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

        [Test]
        public void ActivationEnablesMicrophoneAndTerminalSignalsReleaseBeforeCallbacks()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client();
                var microphone = new MicrophoneControl();
                transport.Configure(client, microphone: microphone);
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();

                CollectionAssert.AreEqual(new[] { true }, microphone.States);
                transport.SignalReceived += signal =>
                {
                    if (signal.Type == VoiceQuestSignalType.Matched)
                    {
                        CollectionAssert.AreEqual(new[] { true, false }, microphone.States);
                        transport.ActivateAsync(new VoiceQuestActivation("b", "next", new[] { "next phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                    }
                };
                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_MATCHED\",\"activation_id\":\"a\"}");
                Pump(transport);

                CollectionAssert.AreEqual(new[] { true, false, true }, microphone.States);
                Assert.AreEqual("b", transport.CurrentActivationId);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void OfflineActivationEnablesOnFirstConnectionAndTerminalQuestDoesNotReopen()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var client = new Client { IsConnectedV2 = false };
                var microphone = new MicrophoneControl();
                transport.Configure(client, microphone: microphone);
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsEmpty(microphone.States);

                client.IsConnectedV2 = true;
                client.Reconnect();
                Pump(transport);
                CollectionAssert.AreEqual(new[] { true }, microphone.States);

                client.Receive("{\"contract_version\":2,\"event\":\"QUEST_MATCHED\",\"activation_id\":\"a\"}");
                Pump(transport);
                client.Reconnect();
                Pump(transport);
                CollectionAssert.AreEqual(new[] { true, false }, microphone.States);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CancelAndReconfigureReleaseMicrophoneAndOldQueuedPacketsAreIgnored()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var oldClient = new Client();
                var oldMic = new MicrophoneControl();
                transport.Configure(oldClient, microphone: oldMic);
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                oldClient.Receive("{\"contract_version\":2,\"event\":\"QUEST_MATCHED\",\"activation_id\":\"a\"}");

                var newClient = new Client();
                var newMic = new MicrophoneControl();
                transport.Configure(newClient, microphone: newMic);
                transport.ActivateAsync(new VoiceQuestActivation("b", "next", new[] { "next phrase" }), CancellationToken.None).GetAwaiter().GetResult();
                Pump(transport);

                CollectionAssert.AreEqual(new[] { true, false }, oldMic.States);
                CollectionAssert.AreEqual(new[] { true }, newMic.States);
                Assert.AreEqual("b", transport.CurrentActivationId);

                transport.CancelAsync("b", "cancelled", CancellationToken.None).GetAwaiter().GetResult();
                CollectionAssert.AreEqual(new[] { true, false }, newMic.States);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ActivationAndCancellationWhileDisabledAreDeferredUntilReenabled()
        {
            var go = new GameObject("voice-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitVoiceQuestTransportV2>();
                var initialClient = new Client();
                var initialMic = new MicrophoneControl();
                transport.Configure(initialClient, microphone: initialMic);
                transport.ActivateAsync(new VoiceQuestActivation("a", "goal", new[] { "phrase" }), CancellationToken.None).GetAwaiter().GetResult();

                var lifecycleFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnDisable", lifecycleFlags).Invoke(transport, null);
                var replacementClient = new Client();
                var replacementMic = new MicrophoneControl();
                transport.Configure(replacementClient, microphone: replacementMic);
                transport.ActivateAsync(new VoiceQuestActivation("b", "next", new[] { "next phrase" }), CancellationToken.None).GetAwaiter().GetResult();

                Assert.IsEmpty(replacementMic.States);
                Assert.IsEmpty(replacementClient.Sent);

                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnEnable", lifecycleFlags).Invoke(transport, null);
                CollectionAssert.AreEqual(new[] { true }, replacementMic.States);
                Assert.That(replacementClient.Sent, Has.Count.EqualTo(1));
                StringAssert.Contains("SET_ACTIVE_QUEST", replacementClient.Sent[0]);

                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnDisable", lifecycleFlags).Invoke(transport, null);
                transport.CancelAsync("b", "cancelled", CancellationToken.None).GetAwaiter().GetResult();
                var sentBeforeResume = replacementClient.Sent.Count;
                replacementClient.Reconnect();
                Pump(transport);
                Assert.AreEqual(sentBeforeResume, replacementClient.Sent.Count);

                typeof(LiveKitVoiceQuestTransportV2).GetMethod("OnEnable", lifecycleFlags).Invoke(transport, null);
                Assert.That(replacementClient.Sent, Has.Count.EqualTo(sentBeforeResume + 1));
                StringAssert.Contains("CANCEL_ACTIVE_QUEST", replacementClient.Sent[sentBeforeResume]);
                CollectionAssert.AreEqual(new[] { true, false }, replacementMic.States);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
