using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LiveKitLessonRemoteBridgeV2Tests
    {
        private sealed class Client : ILiveKitDataPacketClientV2
        {
            public sealed class SentPacket
            {
                public string json;
                public string topic;
                public bool reliable;
            }

            public bool IsConnectedV2 { get; set; } = true;
            public event Action<byte[], string> DataReceivedV2;
            public event Action ReconnectedV2;
            public readonly List<SentPacket> Sent = new List<SentPacket>();

            public void PublishDataV2(byte[] data, string topic, bool reliable)
            {
                Sent.Add(new SentPacket { json = Encoding.UTF8.GetString(data), topic = topic, reliable = reliable });
            }

            public void Receive(string json, string topic = LessonRemoteContractV2.RemoteTopic) =>
                DataReceivedV2?.Invoke(Encoding.UTF8.GetBytes(json), topic);

            public void Reconnect() => ReconnectedV2?.Invoke();
        }

        private sealed class ControlledExecutor : INodeExecutor
        {
            private readonly List<TaskCompletionSource<NodeResult>> _completions = new List<TaskCompletionSource<NodeResult>>();
            public readonly List<NodeExecutionContext> Contexts = new List<NodeExecutionContext>();

            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                Contexts.Add(context);
                var completion = new TaskCompletionSource<NodeResult>();
                _completions.Add(completion);
                return completion.Task;
            }

            public void Complete(int index, NodeStatus status)
            {
                var context = Contexts[index];
                Assert.That(_completions[index].TrySetResult(NodeResult.Completed(context.Node.Id,
                    context.ActivationId, status, context.ElapsedSeconds)), Is.True);
            }

            public void CompleteAll(NodeStatus status)
            {
                for (var index = 0; index < _completions.Count; index++)
                {
                    var context = Contexts[index];
                    _completions[index].TrySetResult(NodeResult.Completed(context.Node.Id,
                        context.ActivationId, status, context.ElapsedSeconds));
                }
            }
        }

        private sealed class Registry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public Registry(INodeExecutor executor) { _executor = executor; }
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = type == NodeType.Wait ? _executor : null;
                return executor != null;
            }
        }

        private static readonly MethodInfo BridgeUpdate = typeof(LiveKitLessonRemoteBridgeV2)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo InstallerUpdate = typeof(LessonGraphRunnerInstaller)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LiveKitServiceInstance = typeof(LiveKitService)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        private SessionContext _previousSession;
        private LiveKitService _previousLiveKitService;
        private GameObject _sessionObject;
        private GameObject _runnerObject;
        private GameObject _bridgeObject;
        private LessonGraphRunnerInstaller _installer;
        private LessonGraph _graph;
        private LessonGraphRunner _runner;
        private LiveKitLessonRemoteBridgeV2 _bridge;
        private ControlledExecutor _controlledExecutor;

        [SetUp]
        public void SetUp()
        {
            _previousSession = SessionContext.Instance;
            _previousLiveKitService = LiveKitServiceInstance.GetValue(null) as LiveKitService;
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "launch-1", "lesson-1", 3, 4, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            _sessionObject = new GameObject("RemoteBridgeSessionContext");
            _sessionObject.SetActive(false);
            var context = _sessionObject.AddComponent<SessionContext>();
            context.SessionId = "session-1";
            context.LessonId = "lesson-1";
            SessionContext.Instance = context;
        }

        [TearDown]
        public void TearDown()
        {
            _controlledExecutor?.CompleteAll(NodeStatus.Failed);
            _runner?.AbortLesson();
            if (_bridgeObject != null) UnityEngine.Object.DestroyImmediate(_bridgeObject);
            if (_runnerObject != null) UnityEngine.Object.DestroyImmediate(_runnerObject);
            if (_graph != null) UnityEngine.Object.DestroyImmediate(_graph);
            if (_sessionObject != null) UnityEngine.Object.DestroyImmediate(_sessionObject);
            var currentLiveKitService = LiveKitServiceInstance.GetValue(null) as LiveKitService;
            if (currentLiveKitService != null && !ReferenceEquals(currentLiveKitService, _previousLiveKitService))
                UnityEngine.Object.DestroyImmediate(currentLiveKitService.gameObject);
            VoicePhraseSnapshotStoreV2.Clear();
            SessionContext.Instance = _previousSession;
        }

        [UnityTest]
        public IEnumerator InstallerRetriesSessionAndBridgeConfigurationAfterMetadataArrives()
        {
            VoicePhraseSnapshotStoreV2.Clear();
            SessionContext.Instance = null;
            CreateInstaller(startOnStart: false);

            Assert.That(_installer.GetComponent<LiveKitLessonRemoteBridgeV2>(), Is.Null,
                "Remote bridge setup should wait until valid session and phrase metadata exist.");

            SessionContext.Instance = _sessionObject.GetComponent<SessionContext>();
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "late-launch", "lesson-1", 8, 9, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            InstallerUpdate.Invoke(_installer, null);

            var bridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(bridge, Is.Not.Null);
            var client = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(bridge);
            Assert.That(client, Is.SameAs(LiveKitService.Instance),
                "The installer should bind the bridge when LiveKitService becomes available.");
            var generationField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_configurationGeneration", BindingFlags.Instance | BindingFlags.NonPublic);
            var configuredGeneration = generationField.GetValue(bridge);
            InstallerUpdate.Invoke(_installer, null);
            Assert.That(generationField.GetValue(bridge), Is.EqualTo(configuredGeneration),
                "Repeated installer updates must not detach and reconfigure an already bound bridge.");

            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            Assert.That(_runner.CurrentState.session_id, Is.EqualTo("session-1"));
            Assert.That(_runner.CurrentState.launch_token, Is.EqualTo("late-launch"));
            Assert.That(_runner.CurrentState.lesson_voice_revision, Is.EqualTo(8));
            Assert.That(_runner.CurrentState.child_phrase_revision, Is.EqualTo(9));

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator InstallerKeepsActiveLocalRunLocalWhenRemoteMetadataArrivesLate()
        {
            VoicePhraseSnapshotStoreV2.Clear();
            SessionContext.Instance = null;
            CreateInstaller(startOnStart: false);

            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");

            SessionContext.Instance = _sessionObject.GetComponent<SessionContext>();
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "late-launch", "lesson-1", 8, 9, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            InstallerUpdate.Invoke(_installer, null);

            Assert.That(_runner.CurrentState.session_id, Is.Empty);
            Assert.That(_installer.GetComponent<LiveKitLessonRemoteBridgeV2>(), Is.Null);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator InstallerRejectsPhraseSnapshotForDifferentSessionLesson()
        {
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "wrong-lesson-launch", "different-lesson", 8, 9,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            CreateInstaller(startOnStart: false);

            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");

            Assert.That(_runner.CurrentState.session_id, Is.Empty,
                "Phrase metadata for another lesson must not configure this runner's session identity.");
            Assert.That(_runner.CurrentState.launch_token, Is.Empty);
            Assert.That(_installer.GetComponent<LiveKitLessonRemoteBridgeV2>(), Is.Null,
                "A mismatched phrase snapshot must not bind a remote bridge to this session.");

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [Test]
        public void InstallerDoesNotReconfigureBridgeEveryFrameWhenPhraseSnapshotIsMissing()
        {
            VoicePhraseSnapshotStoreV2.Clear();
            CreateInstaller(startOnStart: false);
            var bridge = AttachBridgeToInstaller();

            AssertInstallerUpdateDoesNotAdvanceBridgeGeneration(bridge);
        }

        [Test]
        public void InstallerDoesNotReconfigureBridgeEveryFrameWhenPhraseLessonDoesNotMatchSession()
        {
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "wrong-lesson-launch", "different-lesson", 8, 9,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            CreateInstaller(startOnStart: false);
            var bridge = AttachBridgeToInstaller();

            AssertInstallerUpdateDoesNotAdvanceBridgeGeneration(bridge);
        }

        [Test]
        public void InstallerDetachesBridgeOnceWhenCapturedPhraseSnapshotDisappears()
        {
            CreateInstaller(startOnStart: false);
            var bridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(bridge, Is.Not.Null);
            var clientField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic);
            var generationField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_configurationGeneration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clientField.GetValue(bridge), Is.Not.Null);
            var configuredGeneration = generationField.GetValue(bridge);

            VoicePhraseSnapshotStoreV2.Clear();
            InstallerUpdate.Invoke(_installer, null);

            Assert.That(clientField.GetValue(bridge), Is.Null,
                "The bridge must detach when its captured immutable phrase snapshot is no longer available.");
            Assert.That(generationField.GetValue(bridge), Is.Not.EqualTo(configuredGeneration),
                "Invalidating an active snapshot must detach the existing bridge once.");
            AssertInstallerUpdateDoesNotAdvanceBridgeGeneration(bridge);
        }

        [TestCase("different-lesson", "launch-1", 3, 4)]
        [TestCase("lesson-1", "replacement-launch", 3, 4)]
        [TestCase("lesson-1", "launch-1", 30, 4)]
        [TestCase("lesson-1", "launch-1", 3, 40)]
        public void InstallerDetachesWhenCapturedPhraseSnapshotIsReplacedWithDifferentIdentity(
            string lessonId,
            string launchToken,
            int lessonVoiceRevision,
            int childPhraseRevision)
        {
            CreateInstaller(startOnStart: false);
            var bridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(bridge, Is.Not.Null);
            var clientField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic);
            var generationField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_configurationGeneration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clientField.GetValue(bridge), Is.Not.Null);
            var configuredGeneration = generationField.GetValue(bridge);

            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                launchToken, lessonId, lessonVoiceRevision, childPhraseRevision,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            InstallerUpdate.Invoke(_installer, null);

            Assert.That(clientField.GetValue(bridge), Is.Null,
                "A replaced phrase snapshot must not stay attached to the captured runner session.");
            Assert.That(generationField.GetValue(bridge), Is.Not.EqualTo(configuredGeneration),
                "The bridge must detach when any captured phrase identity field changes.");
            AssertInstallerUpdateDoesNotAdvanceBridgeGeneration(bridge);
        }

        [Test]
        public void InstallerConfiguresReplacementBridgeInstanceForSameRunnerAndSession()
        {
            CreateInstaller(startOnStart: false);
            var firstBridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(firstBridge, Is.Not.Null);
            var firstClient = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(firstBridge);
            Assert.That(firstClient, Is.Not.Null);

            UnityEngine.Object.DestroyImmediate(firstBridge);
            InstallerUpdate.Invoke(_installer, null);

            var replacementBridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(replacementBridge, Is.Not.Null);
            Assert.That(replacementBridge, Is.Not.SameAs(firstBridge));
            Assert.That(typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(replacementBridge), Is.SameAs(firstClient),
                "A replacement bridge must bind to the current LiveKit client even when runner/session identities are unchanged.");
        }

        [Test]
        public void InstallerReattachesBridgeAfterItIsReenabled()
        {
            CreateInstaller(startOnStart: false);
            var bridge = _installer.GetComponent<LiveKitLessonRemoteBridgeV2>();
            Assert.That(bridge, Is.Not.Null);
            var generationField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_configurationGeneration", BindingFlags.Instance | BindingFlags.NonPublic);

            bridge.enabled = false;
            var disposedGeneration = generationField.GetValue(bridge);
            InstallerUpdate.Invoke(_installer, null);
            Assert.That(typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(bridge), Is.Null, "A disabled bridge must stay detached while it cannot pump callbacks.");
            Assert.That(generationField.GetValue(bridge), Is.EqualTo(disposedGeneration));

            bridge.enabled = true;
            InstallerUpdate.Invoke(_installer, null);
            Assert.That(typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(bridge), Is.SameAs(LiveKitService.Instance));
            Assert.That(generationField.GetValue(bridge), Is.Not.EqualTo(disposedGeneration));
        }

        [UnityTest]
        public IEnumerator InstallerRetriesMetadataImmediatelyBeforeAutomaticStart()
        {
            VoicePhraseSnapshotStoreV2.Clear();
            SessionContext.Instance = null;
            CreateInstaller(startOnStart: true);

            SessionContext.Instance = _sessionObject.GetComponent<SessionContext>();
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "start-launch", "lesson-1", 5, 6, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));

            yield return UntilFrames(() => _runner.CurrentState?.status == "running");

            Assert.That(_runner.CurrentState.session_id, Is.EqualTo("session-1"));
            Assert.That(_runner.CurrentState.launch_token, Is.EqualTo("start-launch"));
            Assert.That(_runner.CurrentState.lesson_voice_revision, Is.EqualTo(5));
            Assert.That(_runner.CurrentState.child_phrase_revision, Is.EqualTo(6));

            _runner.AbortLesson();
            yield return UntilFrames(() => _runner.CurrentState?.status == "cancelled");
        }

        [UnityTest]
        public IEnumerator WrongTopicAndVersionAreIgnored_ValidStateRequestPublishesReliableSnapshot()
        {
            CreateRunner(twoNodes: false);
            var client = new Client();
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            var state = _runner.CurrentState;
            Pump(_bridge);
            client.Sent.Clear();
            var command = Command("wrong-route", state, LessonCommandKindV2.Skip);

            client.Receive(JsonUtility.ToJson(command), "lesson-graph-v2.voice");
            command.contract_version = 1;
            client.Receive(JsonUtility.ToJson(command));
            Pump(_bridge);

            Assert.That(client.Sent, Is.Empty, "A packet on another topic or with a wrong contract version must not reach the runner.");

            client.Receive("{\"contract_version\":2,\"event\":\"LESSON_STATE_REQUEST\",\"session_id\":\"session-1\",\"command\":\"SKIP\"}");
            client.Receive("{\"contract_version\":2,\"event\":\"LESSON_STATE_REQUEST\",\"session_id\":\"other-session\"}");
            Pump(_bridge);
            Assert.That(client.Sent, Is.Empty, "State requests must contain only version/event/session and match this session.");

            client.Receive("{\"contract_version\":2,\"event\":\"LESSON_STATE_REQUEST\",\"session_id\":\"session-1\"}");
            Pump(_bridge);

            Assert.That(client.Sent.Count, Is.EqualTo(1));
            AssertReliableRemotePackets(client);
            var statePacket = JsonUtility.FromJson<LessonStatePacketV2>(client.Sent[0].json);
            Assert.That(statePacket.@event, Is.EqualTo(LessonRemoteContractV2.StateEvent));
            Assert.That(statePacket.state.run_id, Is.EqualTo(state.run_id));
            Assert.That(client.Sent[0].reliable, Is.True);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator QueuedCommandIsRejectedAgainstTheStateAtDispatchTime()
        {
            CreateRunner(twoNodes: true);
            var client = new Client();
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            var queuedState = _runner.CurrentState;
            client.Receive(JsonUtility.ToJson(Command("queued-skip", queuedState, LessonCommandKindV2.Skip)));

            _runner.RequestSkip();
            yield return UntilFrames(() => _runner.CurrentState?.node_id == "second");
            Pump(_bridge);

            var result = LastResult(client);
            Assert.That(result.accepted, Is.False);
            Assert.That(result.reason, Is.EqualTo(LessonCommandReasonV2.WrongNode));
            Assert.That(_runner.CurrentState.node_id, Is.EqualTo("second"));
            AssertReliableRemotePackets(client);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator WrongSessionAndRunReturnTypedRejectionsWithActualRunnerState()
        {
            CreateRunner(twoNodes: false);
            var client = new Client();
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            Pump(_bridge);
            client.Sent.Clear();

            var state = _runner.CurrentState;
            var wrongSession = Command("wrong-session", state, LessonCommandKindV2.Skip);
            wrongSession.session_id = "other-session";
            client.Receive(JsonUtility.ToJson(wrongSession));
            Pump(_bridge);

            var wrongSessionResult = LastResult(client);
            Assert.That(wrongSessionResult.reason, Is.EqualTo(LessonCommandReasonV2.WrongSession));
            Assert.That(wrongSessionResult.session_id, Is.EqualTo("other-session"));
            Assert.That(wrongSessionResult.state.session_id, Is.EqualTo("session-1"));
            Assert.That(_runner.CurrentState.state_revision, Is.EqualTo(state.state_revision));

            var wrongRun = Command("wrong-run", state, LessonCommandKindV2.Skip);
            wrongRun.run_id = "other-run";
            client.Receive(JsonUtility.ToJson(wrongRun));
            Pump(_bridge);

            var wrongRunResult = LastResult(client);
            Assert.That(wrongRunResult.reason, Is.EqualTo(LessonCommandReasonV2.WrongRun));
            Assert.That(wrongRunResult.run_id, Is.EqualTo("other-run"));
            Assert.That(wrongRunResult.state.run_id, Is.EqualTo(state.run_id));
            Assert.That(_runner.CurrentState.state_revision, Is.EqualTo(state.state_revision));
            AssertReliableRemotePackets(client);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator DuplicateCommandAfterReconnectRemainsDuplicateAndReconnectPublishesSnapshotOnly()
        {
            CreateRunner(twoNodes: true);
            var client = new Client();
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            var original = Command("skip-once", _runner.CurrentState, LessonCommandKindV2.Skip);
            client.Receive(JsonUtility.ToJson(original));
            Pump(_bridge);
            yield return UntilFrames(() => _runner.CurrentState?.node_id == "second");
            yield return UntilFrames(() => FindLastResult(client)?.accepted == true);
            Pump(_bridge);

            var sentBeforeReconnect = client.Sent.Count;
            client.Reconnect();
            Pump(_bridge);
            Assert.That(client.Sent.Count, Is.EqualTo(sentBeforeReconnect + 1));
            Assert.That(JsonUtility.FromJson<LessonStatePacketV2>(client.Sent[client.Sent.Count - 1].json).@event,
                Is.EqualTo(LessonRemoteContractV2.StateEvent));

            client.Receive(JsonUtility.ToJson(original));
            Pump(_bridge);
            var duplicate = LastResult(client);
            Assert.That(duplicate.accepted, Is.False);
            Assert.That(duplicate.reason, Is.EqualTo(LessonCommandReasonV2.Duplicate));
            Assert.That(_runner.CurrentState.node_id, Is.EqualTo("second"));
            AssertReliableRemotePackets(client);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator BridgeConfiguredBeforeFirstRoomConnectionPublishesStateWhenConnected()
        {
            CreateRunner(twoNodes: false);
            var client = new Client { IsConnectedV2 = false };
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            Pump(_bridge);
            Assert.That(client.Sent, Is.Empty, "State changes before connection cannot be published.");

            client.IsConnectedV2 = true;
            client.Reconnect(); // LiveKitService emits ReconnectedV2 on the first successful connection too.
            Pump(_bridge);

            var packet = LastStatePacket(client);
            Assert.That(packet, Is.Not.Null);
            Assert.That(packet.state.session_id, Is.EqualTo("session-1"));
            Assert.That(packet.state.run_id, Is.EqualTo(_runner.CurrentState.run_id));
            Assert.That(packet.state.status, Is.EqualTo("running"));
            AssertReliableRemotePackets(client);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator BridgeConfiguredAfterRunnerStartsPublishesCurrentStateImmediately()
        {
            CreateRunner(twoNodes: false);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            var currentState = _runner.CurrentState;
            var client = new Client { IsConnectedV2 = true };

            CreateBridge(client, isActive: true);
            Assert.That(_bridge.isActiveAndEnabled, Is.True,
                "The late-attached bridge must be active while configuring against the running lesson.");

            var packet = LastStatePacket(client);
            Assert.That(packet, Is.Not.Null,
                "A connected client attached after runner start must receive the current state during Configure.");
            Assert.That(CountStatePackets(client), Is.EqualTo(1),
                "Late attachment must publish exactly one current state snapshot.");
            Assert.That(packet.state.state_revision, Is.EqualTo(currentState.state_revision));
            Assert.That(packet.state.run_id, Is.EqualTo(currentState.run_id));
            Assert.That(packet.state.node_id, Is.EqualTo(currentState.node_id));
            AssertReliableRemotePackets(client);

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator ReconfigureAndDisposeUnsubscribeOldClientAndDropQueuedCommands()
        {
            CreateRunner(twoNodes: false);
            var firstClient = new Client();
            var secondClient = new Client();
            CreateBridge(firstClient);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _runner.CurrentState?.status == "running");
            var command = Command("must-not-dispatch", _runner.CurrentState, LessonCommandKindV2.Skip);

            firstClient.Receive(JsonUtility.ToJson(command));
            _bridge.Configure(secondClient, _runner);
            Assert.That(CountStatePackets(secondClient), Is.EqualTo(1),
                "Reconfiguring to a connected client must publish exactly one authoritative state snapshot.");
            var configuredState = LastStatePacket(secondClient);
            Assert.That(configuredState, Is.Not.Null);
            Assert.That(configuredState.state.session_id, Is.EqualTo(_runner.CurrentState.session_id));
            Assert.That(configuredState.state.run_id, Is.EqualTo(_runner.CurrentState.run_id));
            Assert.That(configuredState.state.state_revision, Is.EqualTo(_runner.CurrentState.state_revision));
            AssertReliableRemotePackets(secondClient);
            secondClient.Sent.Clear();
            Pump(_bridge);
            firstClient.Receive(JsonUtility.ToJson(command));
            secondClient.Receive("{\"contract_version\":2,\"event\":\"LESSON_STATE_REQUEST\",\"session_id\":\"other-session\"}");
            Pump(_bridge);

            Assert.That(_runner.CurrentState.status, Is.EqualTo("running"));
            Assert.That(firstClient.Sent, Is.Empty);
            Assert.That(secondClient.Sent, Is.Empty);

            secondClient.Receive(JsonUtility.ToJson(command));
            _bridge.Dispose();
            Pump(_bridge);
            Assert.That(_runner.CurrentState.status, Is.EqualTo("running"));
            Assert.That(secondClient.Sent, Is.Empty, "Dispose must suppress queued work and later callbacks.");

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator PendingPauseReplyIsSuppressedAfterBridgeDispose()
        {
            _controlledExecutor = new ControlledExecutor();
            CreateRunner(twoNodes: false, _controlledExecutor);
            var client = new Client();
            CreateBridge(client);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _controlledExecutor.Contexts.Count == 1);
            Pump(_bridge);
            client.Sent.Clear();
            var command = Command("pause-pending", _runner.CurrentState, LessonCommandKindV2.Pause);
            client.Receive(JsonUtility.ToJson(command));
            Pump(_bridge);

            Assert.That(_runner.CurrentState.status, Is.EqualTo("pausing"));
            var sentBeforeDispose = client.Sent.Count;
            _bridge.Dispose();
            _controlledExecutor.Complete(0, NodeStatus.Success);
            yield return UntilFrames(() => _runner.CurrentState?.status == "paused");
            Pump(_bridge);

            Assert.That(client.Sent.Count, Is.EqualTo(sentBeforeDispose), "A completion queued after teardown must not publish a stale reply.");
            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [Test]
        public void PhraseMetadataAccessorReturnsAnImmutableSnapshot()
        {
            Assert.That(VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out var metadata), Is.True);
            Assert.That(metadata.LaunchToken, Is.EqualTo("launch-1"));
            Assert.That(metadata.LessonId, Is.EqualTo("lesson-1"));
            Assert.That(metadata.LessonVoiceRevision, Is.EqualTo(3));
            Assert.That(metadata.ChildPhraseRevision, Is.EqualTo(4));

            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "replacement-launch", "replacement-lesson", 10, 11,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            Assert.That(metadata.LaunchToken, Is.EqualTo("launch-1"));
            Assert.That(metadata.LessonId, Is.EqualTo("lesson-1"));
            Assert.That(metadata.LessonVoiceRevision, Is.EqualTo(3));
            Assert.That(metadata.ChildPhraseRevision, Is.EqualTo(4));
        }

        private void CreateRunner(bool twoNodes, INodeExecutor executor = null)
        {
            _graph = ScriptableObject.CreateInstance<LessonGraph>();
            _graph.name = "RemoteBridgeGraph";
            _graph.Editor_SetEntryNodeId("first");
            var nodes = new List<LessonNodeData>
            {
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(30f))
            };
            var edges = new List<LessonEdgeData>();
            if (twoNodes)
            {
                nodes.Add(new LessonNodeData("second", NodeType.Wait, new WaitNodeConfig(30f)));
                edges.Add(new LessonEdgeData("first", "second", new StatusCondition(StatusCondition.Skipped)));
            }
            _graph.Editor_SetNodes(nodes);
            _graph.Editor_SetEdges(edges);

            _runnerObject = new GameObject("RemoteBridgeRunner");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new Registry(executor ?? new WaitNodeExecutor(new MonotonicClock())));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 3, 4));
        }

        private void CreateInstaller(bool startOnStart)
        {
            _graph = ScriptableObject.CreateInstance<LessonGraph>();
            _graph.name = "InstallerRetryGraph";
            _graph.Editor_SetEntryNodeId("first");
            _graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(30f))
            });
            _graph.Editor_SetEdges(new List<LessonEdgeData>());

            _runnerObject = new GameObject("RemoteBridgeInstaller");
            _runnerObject.SetActive(false);
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            var bindings = _runnerObject.AddComponent<LessonGraphBindings>();
            _installer = _runnerObject.AddComponent<LessonGraphRunnerInstaller>();
            SetInstallerField("_lessonGraph", _graph);
            SetInstallerField("_runner", _runner);
            SetInstallerField("_bindings", bindings);
            SetInstallerField("_startOnStart", startOnStart);
            _runnerObject.SetActive(true);
        }

        private void SetInstallerField(string fieldName, object value) =>
            typeof(LessonGraphRunnerInstaller).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_installer, value);

        private LiveKitLessonRemoteBridgeV2 AttachBridgeToInstaller()
        {
            var bridge = _runnerObject.AddComponent<LiveKitLessonRemoteBridgeV2>();
            SetInstallerField("_remoteBridge", bridge);
            return bridge;
        }

        private static void AssertInstallerUpdateDoesNotAdvanceBridgeGeneration(LiveKitLessonRemoteBridgeV2 bridge)
        {
            var generationField = typeof(LiveKitLessonRemoteBridgeV2)
                .GetField("_configurationGeneration", BindingFlags.Instance | BindingFlags.NonPublic);

            InstallerUpdate.Invoke(bridge.GetComponent<LessonGraphRunnerInstaller>(), null);
            var generation = generationField.GetValue(bridge);
            for (var update = 0; update < 3; update++)
                InstallerUpdate.Invoke(bridge.GetComponent<LessonGraphRunnerInstaller>(), null);

            Assert.That(generationField.GetValue(bridge), Is.EqualTo(generation),
                "Invalid phrase metadata must not reconfigure the bridge on every Update.");
        }

        private void CreateBridge(Client client, bool isActive = false)
        {
            _bridgeObject = new GameObject("RemoteBridge");
            if (!isActive) _bridgeObject.SetActive(false);
            _bridge = _bridgeObject.AddComponent<LiveKitLessonRemoteBridgeV2>();
            _bridge.Configure(client, _runner);
        }

        private static LessonCommandV2 Command(string id, LessonStateV2 state, string kind)
        {
            return new LessonCommandV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandEvent,
                command_id = id,
                session_id = state.session_id,
                run_id = state.run_id,
                node_id = state.node_id,
                activation_id = state.activation_id,
                command = kind,
                binding_id = string.Empty
            };
        }

        private static LessonCommandResultV2 LastResult(Client client)
        {
            var result = FindLastResult(client);
            Assert.That(result, Is.Not.Null, "The bridge must publish a typed command result.");
            return result;
        }

        private static LessonCommandResultV2 FindLastResult(Client client)
        {
            for (var index = client.Sent.Count - 1; index >= 0; index--)
            {
                var packet = JsonUtility.FromJson<LessonCommandResultV2>(client.Sent[index].json);
                if (packet != null && packet.@event == LessonRemoteContractV2.CommandResultEvent)
                    return packet;
            }
            return null;
        }

        private static LessonStatePacketV2 LastStatePacket(Client client)
        {
            for (var index = client.Sent.Count - 1; index >= 0; index--)
            {
                var packet = JsonUtility.FromJson<LessonStatePacketV2>(client.Sent[index].json);
                if (packet != null && packet.@event == LessonRemoteContractV2.StateEvent)
                    return packet;
            }
            return null;
        }

        private static int CountStatePackets(Client client)
        {
            var count = 0;
            foreach (var sent in client.Sent)
            {
                var packet = JsonUtility.FromJson<LessonStatePacketV2>(sent.json);
                if (packet != null && packet.@event == LessonRemoteContractV2.StateEvent)
                    count++;
            }
            return count;
        }

        private static void AssertReliableRemotePackets(Client client)
        {
            foreach (var packet in client.Sent)
            {
                Assert.That(packet.topic, Is.EqualTo(LessonRemoteContractV2.RemoteTopic));
                Assert.That(packet.reliable, Is.True);
            }
        }

        private static void Pump(LiveKitLessonRemoteBridgeV2 bridge) => BridgeUpdate.Invoke(bridge, null);

        private static IEnumerator UntilFrames(Func<bool> condition)
        {
            for (var frame = 0; frame < 120 && !condition(); frame++) yield return null;
            Assert.That(condition(), Is.True, "Condition did not complete within 120 Unity frames.");
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            Assert.That(task, Is.Not.Null);
            for (var frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Task did not complete within 120 Unity frames.");
        }
    }
}
