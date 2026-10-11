using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Cloud.RTDB;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>Composes and tears down one accepted, scene-scoped V2 lesson run.</summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphRuntimeOwnerV2 : MonoBehaviour, ILessonRunnerStartOwnerV2
    {
        private const int ConnectionWaitFrames = 900;
        private const int SceneUnloadFlushTimeoutMilliseconds = 5000;

        private AcceptedLessonLaunchV2 _launch;
        private LessonGraphSceneDefinitionV2 _definition;
        private LessonGraphRunner _runner;
        private LiveKitLessonRemoteBridgeV2 _remoteBridge;
        private ILiveKitDataPacketClientV2 _configuredRemoteClient;
        private LessonTelemetryWriterV2 _telemetryWriter;
        private LessonTelemetryAdapterV2 _telemetryAdapter;
        private Action<LessonGraphRuntimeOwnerV2> _released;
        private bool _compositionReady;
        private bool _startRequested;
        private bool _disposed;
        private string _failure;

        public bool IsAvailable => !_disposed && isActiveAndEnabled && _compositionReady;

        public void BeginAcceptedLaunch(AcceptedLessonLaunchV2 launch,
            LessonGraphSceneDefinitionV2 definition, Action<LessonGraphRuntimeOwnerV2> released)
        {
            if (_disposed)
            {
                Debug.LogWarning("[LessonGraphV2] Ignoring launch delivery to a destroyed runtime owner.", this);
                return;
            }
            if (_launch != null)
            {
                Debug.LogWarning("[LessonGraphV2] Ignoring repeated launch delivery; this runtime owner already consumed its launch.", this);
                return;
            }
            if (launch == null || definition == null)
            {
                Fail("The runtime owner received incomplete launch inputs.");
                return;
            }
            _launch = launch;
            _definition = definition;
            _released = released;
            StartCoroutine(ComposeAndStart());
        }

        public bool CanAuthorizeLessonStart(out string reason)
        {
            reason = _failure;
            if (!IsAvailable)
            {
                if (string.IsNullOrWhiteSpace(reason)) reason = "The V2 runtime owner is not ready.";
                return false;
            }
            if (_startRequested)
            {
                reason = "This V2 runtime owner already authorized a lesson run.";
                return false;
            }
            reason = null;
            return true;
        }

        public void MarkLessonStartRequested()
        {
            if (!_compositionReady || _startRequested)
                throw new InvalidOperationException("V2 lesson start authorization must be ready and used exactly once.");
            _startRequested = true;
        }

        private IEnumerator ComposeAndStart()
        {
            string reason;
            if (!ValidateSceneDefinition(out reason))
            {
                Fail(reason);
                yield break;
            }

            var session = SessionContext.Instance;
            if (!MatchesCurrentLaunch(session))
            {
                Fail("The active session or immutable phrase snapshot does not match the accepted launch.");
                yield break;
            }

            var legacyListeners = FindObjectsOfType<RemoteCommandListener>();
            foreach (var listener in legacyListeners)
            {
                if (listener == null || listener.gameObject.scene != gameObject.scene) continue;
                listener.StopListening();
                listener.enabled = false;
            }

            int framesWaited = 0;
            while (LiveKitService.Instance == null || !LiveKitService.Instance.IsConnectedV2)
            {
                if (_disposed || !MatchesCurrentLaunch(SessionContext.Instance))
                {
                    Fail("The accepted launch became stale while waiting for the LiveKit room.");
                    yield break;
                }
                if (++framesWaited >= ConnectionWaitFrames)
                {
                    Fail("LiveKit did not become ready before the V2 startup deadline.");
                    yield break;
                }
                yield return null;
            }

            bool composed;
            try { composed = TryCompose(out reason); }
            catch (Exception exception)
            {
                composed = false;
                reason = "Runtime composition threw: " + exception.Message;
            }
            if (!composed)
            {
                Fail(reason);
                yield break;
            }

            if (!MatchesCurrentLaunch(SessionContext.Instance))
            {
                Fail("The accepted launch changed during runtime composition.");
                yield break;
            }

            _compositionReady = true;
            Task<LessonResult> run;
            try
            {
                run = _runner.StartLessonAsync();
            }
            catch (Exception exception)
            {
                Fail("Lesson start threw before activation: " + exception.Message);
                yield break;
            }

            while (run != null && !run.IsCompleted && !_disposed) yield return null;
            if (_disposed) yield break;
            if (run == null)
            {
                Fail("Lesson start did not return a run task.");
                yield break;
            }
            try
            {
                var result = run.GetAwaiter().GetResult();
                if (!result.IsSuccess)
                {
                    Debug.LogWarning($"[LessonGraphV2] Accepted lesson ended with {result.FailureReason}.", this);
                    ReleaseComposedResources();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[LessonGraphV2] Accepted lesson run failed: " + exception, this);
                ReleaseComposedResources();
            }
        }

        private bool ValidateSceneDefinition(out string reason)
        {
            reason = null;
            if (_definition == null || _definition.gameObject.scene != gameObject.scene)
            {
                reason = "The scene lesson definition is unavailable.";
                return false;
            }
            if (!_definition.isActiveAndEnabled || !_definition.gameObject.activeInHierarchy)
            {
                reason = "The V2 scene definition is disabled or inactive.";
                return false;
            }
            var definitions = Resources.FindObjectsOfTypeAll<LessonGraphSceneDefinitionV2>()
                .Count(item => item != null && item.gameObject.scene == gameObject.scene);
            var owners = Resources.FindObjectsOfTypeAll<LessonGraphRuntimeOwnerV2>()
                .Count(item => item != null && item.gameObject.scene == gameObject.scene);
            if (definitions != 1 || owners != 1)
            {
                reason = $"The scene has {definitions} V2 definitions and {owners} runtime owners; exactly one of each is required.";
                return false;
            }
            if (_definition.LessonGraph == null || _definition.Runner == null || _definition.Bindings == null)
            {
                reason = "The V2 scene definition requires a graph, runner, and quest bindings.";
                return false;
            }
            if (_definition.Runner.gameObject.scene != gameObject.scene ||
                _definition.Bindings.gameObject.scene != gameObject.scene)
            {
                reason = "The graph runner and quest bindings must belong to the accepted lesson scene.";
                return false;
            }
            if (!_definition.Runner.isActiveAndEnabled || !_definition.Bindings.isActiveAndEnabled ||
                !_definition.Runner.gameObject.activeInHierarchy || !_definition.Bindings.gameObject.activeInHierarchy)
            {
                reason = "The graph runner and quest bindings must both be active and enabled.";
                return false;
            }
            var graph = _definition.LessonGraph;
            var requiresDialogue = graph.Nodes.Any(node => node != null && node.NodeType == NodeType.Dialogue);
            var requiresTimeline = graph.Nodes.Any(node => node != null && node.NodeType == NodeType.Timeline);
            if (requiresDialogue && (_definition.DialogueTransport == null || !_definition.DialogueTransport.isActiveAndEnabled))
            {
                reason = "This graph has Dialogue nodes but no active dialogue transport.";
                return false;
            }
            if (requiresTimeline && (_definition.TimelinePlaybackController == null ||
                                     !_definition.TimelinePlaybackController.isActiveAndEnabled))
            {
                reason = "This graph has Timeline nodes but no active Timeline playback controller.";
                return false;
            }
            if (!_definition.Bindings.IsReady(graph, out reason)) return false;
            var graphValidation = LessonGraphValidator.ValidateForExecution(graph, _definition.VariableStore != null);
            if (!graphValidation.IsValid)
            {
                reason = "Lesson graph validation failed: " + graphValidation;
                return false;
            }
            return true;
        }

        private bool TryCompose(out string reason)
        {
            reason = null;
            bool registered = false;
            try
            {
                var sessionContext = SessionContext.Instance;
                if (!MatchesCurrentLaunch(sessionContext))
                    throw new InvalidOperationException("The active session or phrase snapshot no longer matches the accepted launch.");

                _runner = _definition.Runner;
                _remoteBridge = _definition.RemoteBridge;
                var sceneBridges = Resources.FindObjectsOfTypeAll<LiveKitLessonRemoteBridgeV2>()
                    .Where(item => item != null && item.gameObject.scene == gameObject.scene).ToArray();
                if (sceneBridges.Length > 1)
                    throw new InvalidOperationException("The accepted scene contains multiple V2 remote bridges.");
                if (_remoteBridge == null && sceneBridges.Length == 1) _remoteBridge = sceneBridges[0];
                if (_remoteBridge == null) _remoteBridge = gameObject.AddComponent<LiveKitLessonRemoteBridgeV2>();

                var clock = new MonotonicClock();
                var registry = new LessonGraphExecutorRegistry(_definition.Bindings, clock,
                    checkpointTelemetry: null, dialogueTransport: _definition.DialogueTransport,
                    timelinePlaybackController: _definition.TimelinePlaybackController);
                _runner.Configure(_definition.LessonGraph, registry, _definition.Bindings, clock,
                    variableSource: _definition.VariableStore);
                _runner.ConfigureSession(_launch.CreateSessionContext());
                _runner.RegisterStartOwner(this);

                _telemetryWriter = new LessonTelemetryWriterV2(
                    new FirebaseLessonTelemetrySinkV2(_launch.CreateSessionContext(), sessionContext,
                        _definition.LessonGraph.name));
                if (!LessonTelemetryWriterV2.TryRegisterSessionWriter(_launch.SessionId, _telemetryWriter))
                    throw new InvalidOperationException("A V2 telemetry writer already owns this session.");
                registered = true;
                _telemetryAdapter = new LessonTelemetryAdapterV2(_telemetryWriter, _definition.LessonGraph);
                _telemetryAdapter.Attach(_runner, _launch.CreateSessionContext());
                _remoteBridge.Configure(LiveKitService.Instance, _runner);
                if (!_remoteBridge.isActiveAndEnabled)
                    throw new InvalidOperationException("The V2 remote bridge is disabled.");
                _configuredRemoteClient = LiveKitService.Instance;
                _remoteBridgeConfiguredForCurrentRun = true;
                return true;
            }
            catch (Exception exception)
            {
                _telemetryAdapter?.Detach();
                _telemetryAdapter = null;
                if (registered) LessonTelemetryWriterV2.ReleaseSessionWriter(_launch.SessionId, _telemetryWriter);
                _telemetryWriter?.Dispose();
                _telemetryWriter = null;
                if (_remoteBridge != null) _remoteBridge.Configure(null, _runner);
                _configuredRemoteClient = null;
                _remoteBridgeConfiguredForCurrentRun = false;
                _runner?.UnregisterStartOwner(this);
                reason = exception.Message;
                return false;
            }
        }

        private bool MatchesCurrentLaunch(SessionContext session)
        {
            if (_launch == null || session == null ||
                !_launch.HasIdentity(session.SessionId, session.LessonId, VoicePhraseSnapshotStoreV2.LaunchToken))
                return false;
            return VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out var metadata) &&
                   metadata.LessonVoiceRevision == _launch.LessonVoiceRevision &&
                   metadata.ChildPhraseRevision == _launch.ChildPhraseRevision &&
                   string.Equals(metadata.LessonId, _launch.LessonId, StringComparison.Ordinal) &&
                   string.Equals(metadata.LaunchToken, _launch.LaunchToken, StringComparison.Ordinal);
        }

        private void Update()
        {
            RetryRemoteBridgeAttachment(LiveKitService.Instance);
        }

        private bool _remoteBridgeConfiguredForCurrentRun;

        private void RetryRemoteBridgeAttachment(ILiveKitDataPacketClientV2 client)
        {
            if (_disposed || _remoteBridge == null || _runner == null || !_compositionReady ||
                client == null || !client.IsConnectedV2 || !_remoteBridge.isActiveAndEnabled)
                return;

            if (!MatchesCurrentLaunch(SessionContext.Instance))
            {
                if (_remoteBridge.IsConfigured) _remoteBridge.Configure(null, _runner);
                _configuredRemoteClient = null;
                _remoteBridgeConfiguredForCurrentRun = false;
                return;
            }

            // OnDisable disposes the bridge. Its current configuration state is authoritative,
            // so the same client is reattached after re-enable without starting the graph again.
            if (_remoteBridgeConfiguredForCurrentRun && ReferenceEquals(client, _configuredRemoteClient) &&
                _remoteBridge.IsConfigured)
                return;

            _remoteBridge.Configure(client, _runner);
            _configuredRemoteClient = client;
            _remoteBridgeConfiguredForCurrentRun = _remoteBridge.IsConfigured;
        }

        private void Fail(string reason)
        {
            if (_disposed) return;
            _failure = reason ?? "V2 lesson startup failed.";
            Debug.LogError("[LessonGraphV2] Startup blocked: " + _failure, this);
            ReleaseComposedResources();
        }

        private void OnDestroy()
        {
            _disposed = true;
            ReleaseComposedResources();
            _released?.Invoke(this);
            _released = null;
        }

        private void ReleaseComposedResources()
        {
            if (_runner != null)
            {
                _runner.AbortLesson();
                _runner.UnregisterStartOwner(this);
            }
            _compositionReady = false;
            if (_telemetryAdapter != null)
            {
                try { _telemetryAdapter.CaptureSceneUnload(); }
                catch (Exception exception) { Debug.LogError("[LessonGraphV2] Scene-unload capture failed: " + exception); }
                _telemetryAdapter.Detach();
                _telemetryAdapter = null;
            }
            if (_remoteBridge != null)
            {
                _remoteBridge.Configure(null, _runner);
                _configuredRemoteClient = null;
                _remoteBridgeConfiguredForCurrentRun = false;
                _remoteBridge = null;
            }
            var writer = _telemetryWriter;
            _telemetryWriter = null;
            if (writer != null && _launch != null)
                FlushAndReleaseSessionWriterAsync(_launch.SessionId, writer);
            _runner = null;
            _definition = null;
        }

        private static async void FlushAndReleaseSessionWriterAsync(string sessionId, LessonTelemetryWriterV2 writer)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(SceneUnloadFlushTimeoutMilliseconds))
                    await writer.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"[LessonGraphV2] Scene unload flush timed out with {writer.PendingCount} telemetry batch(es) pending; background retries remain active.");
            }
            catch (Exception exception) { Debug.LogError("[LessonGraphV2] Scene unload flush failed: " + exception); }

            try { await writer.FlushAsync(CancellationToken.None); }
            catch (Exception exception) { Debug.LogError("[LessonGraphV2] Background telemetry drain failed: " + exception); }

            if (writer.PendingCount == 0)
            {
                LessonTelemetryWriterV2.ReleaseSessionWriter(sessionId, writer);
                writer.Dispose();
            }
            else Debug.LogError($"[LessonGraphV2] V2 telemetry writer remains registered with {writer.PendingCount} batch(es) pending. Last error: {writer.LastError}");
        }
    }
}
