using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRAutism.Cloud.LiveKit.Tests.Editor
{
    public sealed class LiveKitServiceDecompositionTests
    {
        [Test]
        public void MainThreadExecutor_PreservesOrderAndDropsStaleCommands()
        {
            var executor = new LiveKitMainThreadExecutor();
            var calls = new List<string>();

            executor.AdvanceGeneration(2);
            executor.AdvanceGeneration(1);
            Assert.IsTrue(executor.Post(1, () => calls.Add("stale")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("first")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("second")));
            executor.Drain();

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);

            Exception offThreadException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    executor.AdvanceGeneration(3);
                }
                catch (Exception exception)
                {
                    offThreadException = exception;
                }
            });
            thread.Start();
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(offThreadException);

            executor.Close();
            Assert.IsFalse(executor.Post(2, () => calls.Add("closed")));
        }

        [Test]
        public void V2Packet_PreservesBytesTopicAndReliability()
        {
            var adapter = new FakeRoomAdapter { Connected = true };
            var handle = new RoomConnectionHandle(3, adapter);
            var transport = new LiveKitDataPacketTransport(() => handle);
            var payload = new byte[] { 1, 2, 3 };
            byte[] received = null;
            string receivedTopic = null;
            transport.DataReceivedV2 += (data, topic) => { received = data; receivedTopic = topic; };

            transport.PublishDataV2(payload, "lesson-graph-v2.voice", true);
            transport.HandleIncoming(payload, null, "lesson-graph-v2.voice");

            CollectionAssert.AreEqual(payload, adapter.LastPublishedData);
            Assert.AreEqual("lesson-graph-v2.voice", adapter.LastPublishedTopic);
            Assert.IsTrue(adapter.LastPublishedReliable);
            CollectionAssert.AreEqual(payload, received);
            Assert.AreEqual("lesson-graph-v2.voice", receivedTopic);
        }

        [Test]
        public void LegacyPacket_PreservesCurrentPayloadAndEventBehavior()
        {
            var adapter = new FakeRoomAdapter { Connected = true };
            var transport = new LiveKitDataPacketTransport(() => new RoomConnectionHandle(4, adapter));
            var legacy = new LegacyVoicePacketAdapter(transport);
            var matched = 0;
            legacy.SpeechMatched += () => matched++;

            legacy.SendActiveQuest("Wash Hands", new[] { "soap", "rinse" });
            Assert.AreEqual(
                "{\"event\":\"SET_ACTIVE_QUEST\",\"quest_name\":\"Wash Hands\",\"default_phrases\":[\"soap\",\"rinse\"]}",
                Encoding.UTF8.GetString(adapter.LastPublishedData));
            Assert.IsNull(adapter.LastPublishedTopic);
            Assert.IsTrue(adapter.LastPublishedReliable);

            legacy.HandleIncoming(Encoding.UTF8.GetBytes("{\"event\":\"QUEST_MATCHED\"}"), null);
            Assert.AreEqual(1, matched);
        }

        [UnityTest]
        public IEnumerator MediaPublishCompletesAfterDisconnect_RollsBackResources_Microphone()
        {
            var publication = new FakeMicrophonePublication();
            var publisher = new LiveKitMicrophonePublisher(new FakeMicrophoneFactory(publication), null);
            var generation = 7L;
            var handle = new RoomConnectionHandle(7, new FakeRoomAdapter { Connected = true });

            var task = publisher.SetEnabledAsync(true, handle, value => value == generation);
            generation = 8;
            publisher.Stop();
            publication.CompletePublish();
            yield return CompleteWithinFrames(task, 60);

            Assert.IsTrue(publication.Unpublished);
            Assert.IsTrue(publication.Disposed);
            Assert.IsFalse(publication.Started);
            Assert.AreSame(handle, publication.UnpublishedHandle);
        }

        [UnityTest]
        public IEnumerator MediaPublishCompletesAfterDisconnect_RollsBackResources_Pov()
        {
            var publication = new FakePovPublication();
            var publisher = new LiveKitPovVideoPublisher(
                new FakePovFactory(publication),
                new FakeCoroutineHost());
            var generation = 7L;
            var handle = new RoomConnectionHandle(7, new FakeRoomAdapter { Connected = true });

            var task = publisher.EnableAsync(null, () => handle, value => value == generation, CancellationToken.None);
            generation = 8;
            publisher.Disable();
            publication.CompletePublish();
            yield return CompleteWithinFrames(task, 60);

            Assert.IsTrue(publication.Unpublished);
            Assert.IsTrue(publication.Disposed);
            Assert.IsFalse(publication.FramesStarted);
            Assert.AreSame(handle, publication.UnpublishedHandle);
        }

        private static IEnumerator CompleteWithinFrames(Task task, int frameCount)
        {
            for (var frame = 0; frame < frameCount && !task.IsCompleted; frame++)
                yield return null;

            if (task.IsFaulted)
                throw task.Exception.InnerException ?? task.Exception;

            if (!task.IsCompleted)
                Assert.Fail($"Task did not complete within {frameCount} frames.");
        }

        private sealed class FakeRoomAdapter : ILiveKitRoomAdapter
        {
            public bool Connected { get; set; }
            public byte[] LastPublishedData { get; private set; }
            public string LastPublishedTopic { get; private set; }
            public bool LastPublishedReliable { get; private set; }

            public Room SdkRoom => null;
            public bool IsConnected => Connected;
            public string RoomName => "test-room";
            public string LocalParticipantSid => "test-participant";

            public event Action<byte[], Participant, DataPacketKind, string> DataReceived;
            public event Action<Room> Reconnected;
            public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
            public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;

            public Task ConnectAsync(string roomUrl, string token) => Task.CompletedTask;

            public void PublishData(byte[] data, string topic, bool reliable)
            {
                LastPublishedData = data;
                LastPublishedTopic = topic;
                LastPublishedReliable = reliable;
            }

            public Task PublishAudioTrackAsync(LocalAudioTrack track, TrackPublishOptions options) => Task.CompletedTask;
            public Task PublishVideoTrackAsync(LocalVideoTrack track, TrackPublishOptions options) => Task.CompletedTask;
            public void UnpublishAudioTrack(LocalAudioTrack track) { }
            public void UnpublishVideoTrack(LocalVideoTrack track) { }
            public void Disconnect() { }
        }

        private sealed class FakeMicrophoneFactory : ILiveKitMicrophonePublicationFactory
        {
            private readonly ILiveKitMicrophonePublication _publication;

            public FakeMicrophoneFactory(ILiveKitMicrophonePublication publication)
            {
                _publication = publication;
            }

            public bool TryCreate(Transform parent, out ILiveKitMicrophonePublication publication)
            {
                publication = _publication;
                return true;
            }
        }

        private sealed class FakeMicrophonePublication : ILiveKitMicrophonePublication
        {
            private readonly TaskCompletionSource<bool> _publish = new TaskCompletionSource<bool>();

            public bool Started { get; private set; }
            public bool Unpublished { get; private set; }
            public bool Disposed { get; private set; }
            public RoomConnectionHandle UnpublishedHandle { get; private set; }

            public Task PublishAsync(RoomConnectionHandle handle) => _publish.Task;

            public void CompletePublish() => _publish.TrySetResult(true);

            public void Start() => Started = true;

            public void SetMuted(bool muted) { }

            public void Unpublish(RoomConnectionHandle handle)
            {
                Unpublished = true;
                UnpublishedHandle = handle;
            }

            public void Dispose() => Disposed = true;
        }

        private sealed class FakeCoroutineHost : ILiveKitCoroutineHost
        {
            public Coroutine StartLiveKitCoroutine(IEnumerator routine) => null;

            public void StopLiveKitCoroutine(Coroutine coroutine) { }
        }

        private sealed class FakePovFactory : ILiveKitPovPublicationFactory
        {
            private readonly ILiveKitPovPublication _publication;

            public FakePovFactory(ILiveKitPovPublication publication)
            {
                _publication = publication;
            }

            public ILiveKitPovPublication Create(Camera camera, int width, int height, int frameRate) => _publication;
        }

        private sealed class FakePovPublication : ILiveKitPovPublication
        {
            private readonly TaskCompletionSource<bool> _publish = new TaskCompletionSource<bool>();

            public bool FramesStarted { get; private set; }
            public bool Unpublished { get; private set; }
            public bool Disposed { get; private set; }
            public RoomConnectionHandle UnpublishedHandle { get; private set; }

            public Task PublishAsync(RoomConnectionHandle handle) => _publish.Task;

            public void CompletePublish() => _publish.TrySetResult(true);

            public void BeginFrames() => FramesStarted = true;

            public void Unpublish(RoomConnectionHandle handle)
            {
                Unpublished = true;
                UnpublishedHandle = handle;
            }

            public void Dispose() => Disposed = true;
        }
    }
}
