using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphRuntimeOwnerV2Tests
    {
        private static readonly MethodInfo RetryBridge = typeof(LessonGraphRuntimeOwnerV2)
            .GetMethod("RetryRemoteBridgeAttachment", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ReleaseResources = typeof(LessonGraphRuntimeOwnerV2)
            .GetMethod("ReleaseComposedResources", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly ConstructorInfo LaunchAttemptConstructor = typeof(LessonLaunchAttemptV2)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) }, null);
        private static readonly ConstructorInfo AcceptedLaunchConstructor = typeof(AcceptedLessonLaunchV2)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(LessonLaunchAttemptV2), typeof(string), typeof(string), typeof(int), typeof(int) }, null);

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<LessonGraph> _graphs = new List<LessonGraph>();
        private readonly List<LessonTelemetryWriterV2> _writers = new List<LessonTelemetryWriterV2>();
        private readonly List<HoldingExecutor> _holdingExecutors = new List<HoldingExecutor>();
        private readonly List<GateSink> _gateSinks = new List<GateSink>();
        private SessionContext _previousSession;
        private SessionContext _session;
        private string _sessionId;
        private string _launchToken;

        [SetUp]
        public void SetUp()
        {
            _previousSession = SessionContext.Instance;
            _sessionId = "owner-test-" + Guid.NewGuid().ToString("N");
            _launchToken = "launch-" + Guid.NewGuid().ToString("N");
            var sessionObject = new GameObject("LessonGraphRuntimeOwnerTestSession");
            sessionObject.SetActive(false);
            _objects.Add(sessionObject);
            _session = sessionObject.AddComponent<SessionContext>();
            _session.SessionId = _sessionId;
            _session.LessonId = "lesson-owner-test";
            SessionContext.Instance = _session;
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                _launchToken, _session.LessonId, 4, 6,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var sink in _gateSinks) sink.ReleaseAll();
            foreach (var executor in _holdingExecutors) executor.Complete(NodeStatus.Failed);
            foreach (var gameObject in _objects)
                if (gameObject != null) gameObject.GetComponent<LessonGraphRunner>()?.AbortLesson();
            for (var i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            foreach (var graph in _graphs)
                if (graph != null) UnityEngine.Object.DestroyImmediate(graph);
            foreach (var writer in _writers)
            {
                LessonTelemetryWriterV2.ReleaseSessionWriter(_sessionId, writer);
                writer.Dispose();
            }
            VoicePhraseSnapshotStoreV2.Clear();
            SessionContext.Instance = _previousSession;
        }

        [UnityTest]
        public IEnumerator DisabledOrDestroyedRuntimeOwnerCannotAuthorizeOrRestartRunner()
        {
            var graph = CreateWaitGraph();
            var runnerObject = NewObject("Owner availability runner");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var executor = new HoldingExecutor();
            _holdingExecutors.Add(executor);
            runner.Configure(graph, new SingleRegistry(executor), clock: new MonotonicClock());
            var ownerObject = NewObject("Owner availability owner");
            var owner = ownerObject.AddComponent<LessonGraphRuntimeOwnerV2>();
            SetField(owner, "_runner", runner);
            SetField(owner, "_compositionReady", true);
            runner.RegisterStartOwner(owner);

            owner.enabled = false;
            LogAssert.Expect(LogType.Error,
                "[LessonGraphV2] Refusing to start because no active owner authorized the lesson. The V2 runtime owner is not ready.");
            var disabledOwnerStart = runner.StartLessonAsync();
            Assert.That(disabledOwnerStart.IsCompleted, Is.True);
            Assert.That(runner.CurrentState, Is.Null);
            Assert.That(executor.ExecutionCount, Is.Zero);

            owner.enabled = true;
            typeof(LessonGraphRuntimeOwnerV2).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(owner, null);
            LogAssert.Expect(LogType.Error,
                "[LessonGraphV2] Refusing to start because no active owner authorized the lesson. No active V2 runtime owner is registered.");
            var destroyedOwnerStart = runner.StartLessonAsync();
            Assert.That(destroyedOwnerStart.IsCompleted, Is.True);
            Assert.That(runner.CurrentState, Is.Null);
            Assert.That(executor.ExecutionCount, Is.Zero);

            LogAssert.Expect(LogType.Warning,
                "[LessonGraphV2] Ignoring launch delivery to a destroyed runtime owner.");
            owner.BeginAcceptedLaunch(CreateAcceptedLaunch(), null, null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledRunnerFailsReadinessAndDefinitionKeepsPersistenceOwnership()
        {
            var graph = CreateWaitGraph();
            var definitionObject = NewObject("Unready lesson definition");
            var definition = definitionObject.AddComponent<LessonGraphSceneDefinitionV2>();
            var runnerObject = NewObject("Disabled lesson runner");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.enabled = false;
            var bindings = runnerObject.AddComponent<LessonGraphBindings>();
            SetField(definition, "_lessonGraph", graph);
            SetField(definition, "_runner", runner);
            SetField(definition, "_bindings", bindings);
            var ownerObject = NewObject("Lesson runtime owner");
            var owner = ownerObject.AddComponent<LessonGraphRuntimeOwnerV2>();

            LogAssert.Expect(LogType.Error,
                "[LessonGraphV2] Startup blocked: The graph runner and quest bindings must both be active and enabled.");
            owner.BeginAcceptedLaunch(CreateAcceptedLaunch(), definition, null);
            yield return null;

            Assert.That(runner.CurrentState, Is.Null, "Invalid scene readiness must reject before any graph node executes.");
            Assert.That(LessonGraphSceneDefinitionV2.IsV2PersistenceOwned, Is.True,
                "An authored V2 definition keeps legacy persistence excluded after readiness failure.");

            var originalLaunch = GetField<AcceptedLessonLaunchV2>(owner, "_launch");
            LogAssert.Expect(LogType.Warning,
                "[LessonGraphV2] Ignoring repeated launch delivery; this runtime owner already consumed its launch.");
            owner.BeginAcceptedLaunch(CreateAcceptedLaunch(), definition, null);
            Assert.That(GetField<AcceptedLessonLaunchV2>(owner, "_launch"), Is.SameAs(originalLaunch),
                "A duplicate launch must not replace or tear down the launch already consumed by this owner.");
            Assert.That(runner.CurrentState, Is.Null);
        }

        [UnityTest]
        public IEnumerator DisabledDefinitionFailsReadiness()
        {
            yield return RunDisabledDependencyCase(true, false,
                "The V2 scene definition is disabled or inactive.");
        }

        [UnityTest]
        public IEnumerator DisabledBindingsFailReadiness()
        {
            yield return RunDisabledDependencyCase(false, true,
                "The graph runner and quest bindings must both be active and enabled.");
        }

        private IEnumerator RunDisabledDependencyCase(bool disableDefinition, bool disableBindings,
            string expectedReason)
        {
            var graph = CreateWaitGraph();
            var definitionObject = NewObject("Inactive dependency definition");
            var definition = definitionObject.AddComponent<LessonGraphSceneDefinitionV2>();
            var runnerObject = NewObject("Active runner with disabled dependency");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var bindings = runnerObject.AddComponent<LessonGraphBindings>();
            SetField(definition, "_lessonGraph", graph);
            SetField(definition, "_runner", runner);
            SetField(definition, "_bindings", bindings);
            if (disableDefinition) definition.enabled = false;
            if (disableBindings) bindings.enabled = false;
            var ownerObject = NewObject("Dependency readiness owner");
            var owner = ownerObject.AddComponent<LessonGraphRuntimeOwnerV2>();

            LogAssert.Expect(LogType.Error, "[LessonGraphV2] Startup blocked: " + expectedReason);
            owner.BeginAcceptedLaunch(CreateAcceptedLaunch(), definition, null);
            yield return null;

            Assert.That(runner.CurrentState, Is.Null);
            Assert.That(LessonGraphSceneDefinitionV2.IsV2PersistenceOwned, Is.True);
        }

        [UnityTest]
        public IEnumerator ReenabledBridgeReattachesThroughOwnerWithoutRestartingOrReplacingWriter()
        {
            var graph = CreateWaitGraph();
            var runnerObject = NewObject("Reconnect runner");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var executor = new HoldingExecutor();
            _holdingExecutors.Add(executor);
            runner.Configure(graph, new SingleRegistry(executor), clock: new MonotonicClock());
            runner.ConfigureSession(CreateAcceptedLaunch().CreateSessionContext());

            var bridgeObject = NewObject("Reconnect bridge");
            var bridge = bridgeObject.AddComponent<LiveKitLessonRemoteBridgeV2>();
            var client = new CallbackCountingClient();
            var ownerObject = NewObject("Reconnect owner");
            var owner = ownerObject.AddComponent<LessonGraphRuntimeOwnerV2>();
            var launch = CreateAcceptedLaunch();
            var writer = NewWriter(new ImmediateSink());
            Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionId, writer), Is.True);
            SetField(owner, "_launch", launch);
            SetField(owner, "_runner", runner);
            SetField(owner, "_remoteBridge", bridge);
            SetField(owner, "_telemetryWriter", writer);
            SetField(owner, "_compositionReady", true);
            runner.RegisterStartOwner(owner);

            RetryBridge.Invoke(owner, new object[] { client });
            Assert.That(client.DataSubscriberCount, Is.EqualTo(1));
            Assert.That(client.ReconnectSubscriberCount, Is.EqualTo(1));

            var task = runner.StartLessonAsync();
            yield return Until(() => executor.Context != null);
            var before = runner.CurrentState;
            Assert.That(before, Is.Not.Null);

            bridge.enabled = false;
            Assert.That(client.DataSubscriberCount, Is.Zero);
            Assert.That(client.ReconnectSubscriberCount, Is.Zero);
            bridge.enabled = true;
            RetryBridge.Invoke(owner, new object[] { client });

            Assert.That(client.DataSubscriberCount, Is.EqualTo(1), "The same connected client must be reattached after bridge re-enable.");
            Assert.That(client.ReconnectSubscriberCount, Is.EqualTo(1));
            Assert.That(runner.CurrentState.run_id, Is.EqualTo(before.run_id));
            Assert.That(runner.CurrentState.node_id, Is.EqualTo(before.node_id));
            Assert.That(runner.CurrentState.status, Is.EqualTo(before.status));
            Assert.That(GetField<LessonTelemetryWriterV2>(owner, "_telemetryWriter"), Is.SameAs(writer));
            Assert.That(executor.ExecutionCount, Is.EqualTo(1), "Bridge reattachment must not start a second graph run.");
            var replacementWriter = NewWriter(new ImmediateSink());
            Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionId, replacementWriter), Is.False,
                "Reattaching the remote bridge must keep the original session writer registered.");

            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "replacement-token", _session.LessonId, 4, 6,
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));
            RetryBridge.Invoke(owner, new object[] { client });
            Assert.That(client.DataSubscriberCount, Is.Zero,
                "A bridge must detach if the immutable phrase identity no longer matches its accepted launch.");
            Assert.That(runner.CurrentState.run_id, Is.EqualTo(before.run_id));

            executor.Complete(NodeStatus.Success);
            yield return CompleteWithinFrames(task);
        }

        [UnityTest]
        public IEnumerator SceneUnloadDetachesAdapterAndBridgeAndRetainsWriterUntilDrain()
        {
            var graph = CreateWaitGraph();
            var runnerObject = NewObject("Unload runner");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var executor = new HoldingExecutor();
            _holdingExecutors.Add(executor);
            runner.Configure(graph, new SingleRegistry(executor), clock: new MonotonicClock());
            var launch = CreateAcceptedLaunch();
            var context = launch.CreateSessionContext();
            runner.ConfigureSession(context);

            var sink = new GateSink();
            _gateSinks.Add(sink);
            var writer = NewWriter(sink);
            Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionId, writer), Is.True);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, elapsedSeconds: () => 1d);
            adapter.Attach(runner, context);

            var bridgeObject = NewObject("Unload bridge");
            var bridge = bridgeObject.AddComponent<LiveKitLessonRemoteBridgeV2>();
            var client = new CallbackCountingClient();
            bridge.Configure(client, runner);
            var ownerObject = NewObject("Unload owner");
            var owner = ownerObject.AddComponent<LessonGraphRuntimeOwnerV2>();
            SetField(owner, "_launch", launch);
            SetField(owner, "_runner", runner);
            SetField(owner, "_remoteBridge", bridge);
            SetField(owner, "_telemetryWriter", writer);
            SetField(owner, "_telemetryAdapter", adapter);
            SetField(owner, "_configuredRemoteClient", client);
            SetField(owner, "_remoteBridgeConfiguredForCurrentRun", true);
            SetField(owner, "_compositionReady", true);
            runner.RegisterStartOwner(owner);

            var task = runner.StartLessonAsync();
            yield return Until(() => executor.Context != null && sink.WriteStateCalls > 0);
            Assert.That(writer.PendingCount, Is.GreaterThan(0));

            ReleaseResources.Invoke(owner, null);

            Assert.That(client.DataSubscriberCount, Is.Zero);
            Assert.That(client.ReconnectSubscriberCount, Is.Zero);
            Assert.That(GetRunnerEventCount(runner, "StateChanged"), Is.Zero,
                "The scene owner must detach its telemetry adapter from the runner during unload.");
            var replacement = NewWriter(new ImmediateSink());
            Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionId, replacement), Is.False,
                "The pending unload writer retains exclusive session ownership until its queued writes drain.");

            sink.ReleaseAll();
            var replacementRegistered = false;
            for (var frame = 0; frame < 120 && !replacementRegistered; frame++)
            {
                replacementRegistered = LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionId, replacement);
                if (!replacementRegistered) yield return null;
            }
            Assert.That(replacementRegistered, Is.True,
                "The original writer must release session ownership after all pending writes drain.");
            LessonTelemetryWriterV2.ReleaseSessionWriter(_sessionId, replacement);
            replacement.Dispose();

            executor.Complete(NodeStatus.Success);
            yield return CompleteWithinFrames(task);
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private LessonGraph CreateWaitGraph()
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.name = "runtime-owner-test-graph";
            graph.Editor_SetEntryNodeId("wait");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("wait", NodeType.Wait, new WaitNodeConfig(30f))
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            _graphs.Add(graph);
            return graph;
        }

        private AcceptedLessonLaunchV2 CreateAcceptedLaunch()
        {
            var attempt = (LessonLaunchAttemptV2)LaunchAttemptConstructor.Invoke(new object[]
            {
                Guid.NewGuid().ToString("N"), _sessionId, _session.LessonId,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, _launchToken
            });
            return (AcceptedLessonLaunchV2)AcceptedLaunchConstructor.Invoke(new object[]
            {
                attempt, "child-owner-test", "host-owner-test", 4, 6
            });
        }

        private LessonTelemetryWriterV2 NewWriter(ILessonTelemetrySinkV2 sink)
        {
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            _writers.Add(writer);
            return writer;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static int GetRunnerEventCount(LessonGraphRunner runner, string eventName)
        {
            var eventField = typeof(LessonGraphRunner).GetField(eventName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (eventField?.GetValue(runner) as Delegate)?.GetInvocationList().Length ?? 0;
        }

        private static IEnumerator Until(Func<bool> predicate)
        {
            for (var frame = 0; frame < 120 && !predicate(); frame++) yield return null;
            Assert.That(predicate(), Is.True, "The focused owner operation must become ready within 120 frames.");
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "The focused graph run must complete within 120 frames.");
        }

        private sealed class SingleRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public SingleRegistry(INodeExecutor executor) => _executor = executor;
            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = type == NodeType.Wait ? _executor : null;
                return executor != null;
            }
        }

        private sealed class HoldingExecutor : INodeExecutor
        {
            private TaskCompletionSource<NodeResult> _completion;
            public NodeExecutionContext Context { get; private set; }
            public int ExecutionCount { get; private set; }

            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                Context = context;
                ExecutionCount++;
                _completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _completion.Task;
            }

            public void Complete(NodeStatus status)
            {
                if (_completion == null || Context == null) return;
                _completion.TrySetResult(NodeResult.Completed(Context.Node.Id, Context.ActivationId,
                    status, Context.ElapsedSeconds));
            }
        }

        private sealed class CallbackCountingClient : ILiveKitDataPacketClientV2
        {
            private Action<byte[], string> _dataReceived;
            private Action _reconnected;
            public bool IsConnectedV2 => true;
            public int DataSubscriberCount { get; private set; }
            public int ReconnectSubscriberCount { get; private set; }
            public event Action<byte[], string> DataReceivedV2
            {
                add { _dataReceived += value; DataSubscriberCount++; }
                remove { _dataReceived -= value; DataSubscriberCount--; }
            }
            public event Action ReconnectedV2
            {
                add { _reconnected += value; ReconnectSubscriberCount++; }
                remove { _reconnected -= value; ReconnectSubscriberCount--; }
            }
            public void PublishDataV2(byte[] data, string topic, bool reliable) { }
        }

        private sealed class ImmediateSink : ILessonTelemetrySinkV2
        {
            public Task WriteStateAsync(LessonStateV2 state, CancellationToken token) => Task.CompletedTask;
            public Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token) => Task.CompletedTask;
        }

        private sealed class GateSink : ILessonTelemetrySinkV2
        {
            private readonly List<TaskCompletionSource<bool>> _pending = new List<TaskCompletionSource<bool>>();
            private bool _released;
            public int WriteStateCalls { get; private set; }

            public Task WriteStateAsync(LessonStateV2 state, CancellationToken token)
            {
                WriteStateCalls++;
                return CreatePendingTask();
            }

            public Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token) => Task.CompletedTask;

            public void ReleaseAll()
            {
                _released = true;
                foreach (var pending in _pending) pending.TrySetResult(true);
            }

            private Task CreatePendingTask()
            {
                if (_released) return Task.CompletedTask;
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(pending);
                return pending.Task;
            }
        }
    }
}
