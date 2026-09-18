using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LiveKitDialogueTransportV2Tests
    {
        private sealed class Client : ILiveKitDataPacketClientV2
        {
            public bool IsConnectedV2 { get; set; } = true;
            public event Action<byte[], string> DataReceivedV2;
            public event Action ReconnectedV2;
            public readonly List<string> Sent = new List<string>();

            public void PublishDataV2(byte[] data, string topic, bool reliable)
            {
                Assert.AreEqual(DialogueTransportV2Constants.Topic, topic);
                Assert.IsTrue(reliable);
                Sent.Add(Encoding.UTF8.GetString(data));
            }

            public void Reconnect() => ReconnectedV2?.Invoke();
            public void Receive(string packet, string topic = DialogueTransportV2Constants.Topic) =>
                DataReceivedV2?.Invoke(Encoding.UTF8.GetBytes(packet), topic);
        }

        private static void Pump(LiveKitDialogueTransportV2 transport) =>
            typeof(LiveKitDialogueTransportV2)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(transport, null);

        [Test]
        public void SpeakAsync_PublishesSpeakScriptPacket()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                transport.SpeakAsync(
                    new DialogueRequestV2("act-1", "seq-1", "Hello class!", "npc-teacher"),
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(1, client.Sent.Count);
                var packet = client.Sent[0];
                StringAssert.Contains("\"event\":\"SPEAK_SCRIPT\"", packet);
                StringAssert.Contains("\"contract_version\":2", packet);
                StringAssert.Contains("\"activation_id\":\"act-1\"", packet);
                StringAssert.Contains("\"sequence_id\":\"seq-1\"", packet);
                StringAssert.Contains("\"npc_binding_id\":\"npc-teacher\"", packet);
                StringAssert.Contains("\"text\":\"Hello class!\"", packet);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DataReceived_SpeakScriptDone_EmitsSignalOnPump()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                var signals = new List<DialogueSignalV2>();
                transport.SignalReceived += signals.Add;

                transport.SpeakAsync(
                    new DialogueRequestV2("act-1", "seq-1", "Hello class!", "npc-teacher"),
                    CancellationToken.None).GetAwaiter().GetResult();

                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-1\",\"sequence_id\":\"seq-1\",\"npc_binding_id\":\"npc-teacher\",\"status\":\"SUCCESS\"}");

                // Signal is queued until main-thread pump
                Assert.IsEmpty(signals);
                Pump(transport);

                Assert.AreEqual(1, signals.Count);
                Assert.AreEqual("act-1", signals[0].ActivationId);
                Assert.AreEqual("seq-1", signals[0].SequenceId);
                Assert.AreEqual("npc-teacher", signals[0].NpcBindingId);
                Assert.AreEqual(DialogueSignalType.Done, signals[0].Type);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DataReceived_WrongTopicOrMismatchedActivation_Ignored()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                var signals = new List<DialogueSignalV2>();
                transport.SignalReceived += signals.Add;

                transport.SpeakAsync(
                    new DialogueRequestV2("act-1", "seq-1", "Hello!", "npc-teacher"),
                    CancellationToken.None).GetAwaiter().GetResult();

                // Wrong topic
                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-1\",\"sequence_id\":\"seq-1\",\"npc_binding_id\":\"npc-teacher\",\"status\":\"SUCCESS\"}",
                    topic: "wrong.topic");

                // Mismatched activation
                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-other\",\"sequence_id\":\"seq-1\",\"npc_binding_id\":\"npc-teacher\",\"status\":\"SUCCESS\"}");

                Pump(transport);
                Assert.IsEmpty(signals);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Reconnect_ResendsActiveRequest()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                transport.SpeakAsync(
                    new DialogueRequestV2("act-1", "seq-1", "Hello reconnect!", "npc-teacher"),
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(1, client.Sent.Count);

                client.Reconnect();
                Pump(transport);

                Assert.AreEqual(2, client.Sent.Count);
                StringAssert.Contains("\"text\":\"Hello reconnect!\"", client.Sent[1]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DataReceived_SpeakScriptDone_CancelledAndFailed_EmitCorrectSignals()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                var signals = new List<DialogueSignalV2>();
                transport.SignalReceived += signals.Add;

                // Test CANCELLED
                transport.SpeakAsync(
                    new DialogueRequestV2("act-cancel", "seq-cancel", "Text", "npc-1"),
                    CancellationToken.None).GetAwaiter().GetResult();

                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-cancel\",\"sequence_id\":\"seq-cancel\",\"npc_binding_id\":\"npc-1\",\"status\":\"CANCELLED\",\"reason\":\"interrupted\"}");

                Pump(transport);
                Assert.AreEqual(1, signals.Count);
                Assert.AreEqual(DialogueSignalType.Cancelled, signals[0].Type);
                Assert.AreEqual("interrupted", signals[0].Reason);

                // Test FAILED on fresh speak request
                signals.Clear();
                transport.SpeakAsync(
                    new DialogueRequestV2("act-fail", "seq-fail", "Text", "npc-1"),
                    CancellationToken.None).GetAwaiter().GetResult();

                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-fail\",\"sequence_id\":\"seq-fail\",\"npc_binding_id\":\"npc-1\",\"status\":\"FAILED\",\"reason\":\"synth error\"}");

                Pump(transport);
                Assert.AreEqual(1, signals.Count);
                Assert.AreEqual(DialogueSignalType.Failed, signals[0].Type);
                Assert.AreEqual("synth error", signals[0].Reason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CancelAsync_SuppressesSubsequentDoneSignals()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                transport.Configure(client);

                var signals = new List<DialogueSignalV2>();
                transport.SignalReceived += signals.Add;

                transport.SpeakAsync(
                    new DialogueRequestV2("act-suppress", "seq-suppress", "Text", "npc-1"),
                    CancellationToken.None).GetAwaiter().GetResult();

                // Explicit cancellation
                transport.CancelAsync("act-suppress", "seq-suppress", "timeout", CancellationToken.None).GetAwaiter().GetResult();

                // Late signal arriving from network
                client.Receive(
                    "{\"contract_version\":2,\"event\":\"SPEAK_SCRIPT_DONE\",\"activation_id\":\"act-suppress\",\"sequence_id\":\"seq-suppress\",\"npc_binding_id\":\"npc-1\",\"status\":\"SUCCESS\"}");

                Pump(transport);
                Assert.IsEmpty(signals, "Late signal must be suppressed after CancelAsync");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private sealed class MockRouter : INpcAudioRouterV2
        {
            public string ActiveNpcBindingId { get; private set; } = string.Empty;
            public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source) { }
            public void UnregisterNpcAudioRoute(string npcBindingId) { }
            public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source) { source = null; return false; }
            public bool SetActiveNpcRoute(string npcBindingId)
            {
                ActiveNpcBindingId = npcBindingId;
                return true;
            }
        }

        [Test]
        public void SpeakAsync_SetsActiveNpcRouteOnRouter()
        {
            var go = new GameObject("dialogue-transport-test");
            go.SetActive(false);
            try
            {
                var transport = go.AddComponent<LiveKitDialogueTransportV2>();
                var client = new Client();
                var router = new MockRouter();
                transport.Configure(client, router);

                transport.SpeakAsync(
                    new DialogueRequestV2("act-1", "seq-1", "Text", "npc-teacher"),
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual("npc-teacher", router.ActiveNpcBindingId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
