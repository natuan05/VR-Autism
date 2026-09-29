using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Inspector-friendly composition bridge for a LessonGraph V2 runner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphRunnerInstaller : MonoBehaviour
    {
        private const int SceneUnloadFlushTimeoutMilliseconds = 5000;

        [SerializeField] private LessonGraph _lessonGraph;
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private LiveKitDialogueTransportV2 _dialogueTransport;
        [SerializeField] private LiveKitLessonRemoteBridgeV2 _remoteBridge;
        [SerializeField] private TimelinePlaybackController _timelinePlaybackController;
        [SerializeField] private LessonGraphVariableStore _variableStore;
        [SerializeField] private bool _startOnStart;

        private INodeClock _clock;
        private LessonSessionContextV2 _sessionContext;
        private bool _sessionContextCaptured;
        private bool _lessonStartRequested;
        private bool _runnerConfigured;
        private bool _remoteBridgeConfigured;
        private string _lastSessionContextWarning;
        private string _configuredRemoteSessionId = string.Empty;
        private LessonGraphRunner _configuredRunner;
        private ILiveKitDataPacketClientV2 _configuredRemoteClient;
        private LessonGraphRunner _configuredRemoteRunner;
        private LiveKitLessonRemoteBridgeV2 _configuredRemoteBridge;
        private LessonGraph _configuredGraph;
        private LessonGraphBindings _configuredBindings;
        private LiveKitDialogueTransportV2 _configuredDialogueTransport;
        private TimelinePlaybackController _configuredTimelinePlaybackController;
        private LessonGraphVariableStore _configuredVariableStore;
        private LessonTelemetryWriterV2 _telemetryWriter;
        private LessonTelemetryAdapterV2 _telemetryAdapter;
        private bool _telemetryConfigured;
        private bool _telemetryInitializationFailed;
        private string _telemetryInitializationError;
        private LessonGraphRunner _registeredRunner;

        public LessonGraph LessonGraph => _lessonGraph;
        public LessonGraphRunner Runner => _runner;
        public LessonGraphBindings Bindings => _bindings;
        public LiveKitDialogueTransportV2 DialogueTransport => _dialogueTransport;
        public bool IsTelemetryPersistenceReady => _telemetryConfigured && !_telemetryInitializationFailed;
        public string TelemetryPersistenceError => _telemetryInitializationError;

        public bool CanAuthorizeLessonStart(out string reason)
        {
            if (!isActiveAndEnabled)
            {
                reason = "V2 telemetry installer is disabled while its runner is still attached.";
                return false;
            }
            if (!IsTelemetryPersistenceReady)
            {
                reason = string.IsNullOrWhiteSpace(_telemetryInitializationError)
                    ? "V2 telemetry composition is not ready."
                    : _telemetryInitializationError;
                return false;
            }
            if (_lessonStartRequested)
            {
                reason = "This V2 installer already owns a lesson run for the external session.";
                return false;
            }

            reason = null;
            return true;
        }

        public void MarkLessonStartRequested()
        {
            if (!_telemetryConfigured || _lessonStartRequested)
                throw new InvalidOperationException("A V2 lesson start must be authorized exactly once after telemetry composition.");
            _lessonStartRequested = true;
        }

        public static bool ShouldSkipLegacyFirebasePersistence(bool hasActiveV2Installer) => hasActiveV2Installer;

        public static bool ShouldSkipLegacyFirebasePersistence()
        {
            LessonGraphRunnerInstaller[] installers = FindObjectsOfType<LessonGraphRunnerInstaller>();
            for (int i = 0; i < installers.Length; i++)
                if (installers[i] != null && installers[i].isActiveAndEnabled)
                    return ShouldSkipLegacyFirebasePersistence(true);
            return ShouldSkipLegacyFirebasePersistence(false);
        }

        private void Awake()
        {
            if (_runner == null) _runner = GetComponent<LessonGraphRunner>();
            if (_bindings == null) _bindings = GetComponent<LessonGraphBindings>();
            if (_dialogueTransport == null) _dialogueTransport = GetComponent<LiveKitDialogueTransportV2>() ?? FindObjectOfType<LiveKitDialogueTransportV2>();
            if (_remoteBridge == null) _remoteBridge = GetComponent<LiveKitLessonRemoteBridgeV2>();
            if (_timelinePlaybackController == null) _timelinePlaybackController = GetComponent<TimelinePlaybackController>();
            try
            {
                Configure();
            }
            catch (Exception exception)
            {
                _telemetryInitializationFailed = true;
                _telemetryInitializationError = "V2 installer composition failed: " + exception.Message;
                Debug.LogError("[LessonGraphV2] " + _telemetryInitializationError, this);
            }
        }

        private void OnEnable()
        {
            if (_runner == null) return;
            try
            {
                RegisterRunnerOwner();
            }
            catch (Exception exception)
            {
                _telemetryInitializationFailed = true;
                _telemetryInitializationError = "V2 installer composition failed: " + exception.Message;
                Debug.LogError("[LessonGraphV2] " + _telemetryInitializationError, this);
            }
        }

        private async void Start()
        {
            RetryOptionalConfiguration();
            if (!_startOnStart) return;
            if (!_runnerConfigured || !EnsureTelemetryConfigured())
            {
                if (!_runnerConfigured && string.IsNullOrWhiteSpace(_telemetryInitializationError))
                    _telemetryInitializationError = "V2 runner configuration is unavailable; refusing to start without its telemetry owner.";
                Debug.LogError("[LessonGraphV2] Refusing to start because V2 telemetry composition is not ready. " + _telemetryInitializationError, this);
                return;
            }
            Debug.Log($"[LessonGraphV2] Installer auto-starting lesson graph='{_lessonGraph.name}'", this);
            try
            {
                var result = await _runner.StartLessonAsync();
                if (result.IsSuccess)
                    Debug.Log($"[LessonGraphV2] Lesson finished successfully run={result.RunId}", this);
                else
                    Debug.LogWarning($"[LessonGraphV2] Lesson finished with failure reason={result.FailureReason} run={result.RunId}", this);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LessonGraphV2] Lesson start exception: {ex}", this);
            }
        }

        private void Update()
        {
            if (IsRemoteBridgeAttachedToCurrentSession()) return;
            RetryOptionalConfiguration();
        }

        public void Configure()
        {
            if (_runner == null) _runner = GetComponent<LessonGraphRunner>();
            if (_variableStore == null) _variableStore = GetComponent<LessonGraphVariableStore>();
            var runnerConfigurationChanged = !_runnerConfigured || !ReferenceEquals(_configuredRunner, _runner) ||
                !ReferenceEquals(_configuredGraph, _lessonGraph) ||
                !ReferenceEquals(_configuredBindings, _bindings) ||
                !ReferenceEquals(_configuredDialogueTransport, _dialogueTransport) ||
                !ReferenceEquals(_configuredTimelinePlaybackController, _timelinePlaybackController) ||
                !ReferenceEquals(_configuredVariableStore, _variableStore);
            if (_telemetryConfigured && runnerConfigurationChanged)
                throw new InvalidOperationException("Runner composition cannot change after the V2 telemetry adapter attaches.");

            RegisterRunnerOwner();
            if (_lessonGraph == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraph asset.");
            if (_runner == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphRunner component.");
            if (_bindings == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphBindings component.");

            if (runnerConfigurationChanged)
            {
                _clock = new MonotonicClock();
                _runner.Configure(
                    _lessonGraph,
                    new LessonGraphExecutorRegistry(_bindings, _clock, dialogueTransport: _dialogueTransport,
                        timelinePlaybackController: _timelinePlaybackController),
                    _bindings,
                    _clock,
                    variableSource: _variableStore);
                _configuredRunner = _runner;
                _configuredGraph = _lessonGraph;
                _configuredBindings = _bindings;
                _configuredDialogueTransport = _dialogueTransport;
                _configuredTimelinePlaybackController = _timelinePlaybackController;
                _configuredVariableStore = _variableStore;
                _runnerConfigured = true;
            }

            RetryOptionalConfiguration();
            if (_sessionContextCaptured && !_telemetryInitializationFailed)
                EnsureTelemetryConfigured();
            if (runnerConfigurationChanged)
                Debug.Log($"[LessonGraphV2] Installer configured: graph='{_lessonGraph.name}' runner={_runner.name} bindings={_bindings.name}", this);
        }

        private bool EnsureTelemetryConfigured()
        {
            if (_telemetryInitializationFailed) return false;
            if (_telemetryConfigured)
            {
                SessionContext currentSessionContext = SessionContext.Instance;
                if (currentSessionContext != null &&
                    string.Equals(currentSessionContext.SessionId, _sessionContext.SessionId, StringComparison.Ordinal) &&
                    string.Equals(currentSessionContext.LessonId, _sessionContext.LessonId, StringComparison.Ordinal) &&
                    IsCapturedPhraseSnapshotCurrent(currentSessionContext))
                    return true;
                return FailTelemetryInitialization("the active session or immutable phrase snapshot changed after telemetry composition.");
            }
            if (!_runnerConfigured || _runner == null || _lessonGraph == null || _sessionContext == null)
                return FailTelemetryInitialization("V2 runner, graph, or immutable session context is unavailable.");

            SessionContext activeSessionContext = SessionContext.Instance;
            if (activeSessionContext == null ||
                !string.Equals(activeSessionContext.SessionId, _sessionContext.SessionId, StringComparison.Ordinal) ||
                !string.Equals(activeSessionContext.LessonId, _sessionContext.LessonId, StringComparison.Ordinal))
                return FailTelemetryInitialization("the active SessionContext does not match the captured V2 session.");

            LessonTelemetryWriterV2 writer = null;
            bool registered = false;
            try
            {
                var sink = new FirebaseLessonTelemetrySinkV2(_sessionContext, activeSessionContext, _lessonGraph.name);
                writer = new LessonTelemetryWriterV2(sink);
                if (!LessonTelemetryWriterV2.TryRegisterSessionWriter(_sessionContext.SessionId, writer))
                    throw new InvalidOperationException("A V2 telemetry writer already owns this external session.");
                registered = true;

                var adapter = new LessonTelemetryAdapterV2(writer, _lessonGraph);
                adapter.Attach(_runner, _sessionContext);
                _telemetryWriter = writer;
                _telemetryAdapter = adapter;
                _telemetryConfigured = true;
                _telemetryInitializationError = null;
                return true;
            }
            catch (Exception exception)
            {
                if (registered)
                    LessonTelemetryWriterV2.ReleaseSessionWriter(_sessionContext.SessionId, writer);
                writer?.Dispose();
                return FailTelemetryInitialization(exception.Message);
            }
        }

        private void RegisterRunnerOwner()
        {
            if (_runner == null)
            {
                ReleaseRunnerOwner();
                return;
            }
            if (ReferenceEquals(_registeredRunner, _runner)) return;

            ReleaseRunnerOwner();
            _runner.RegisterTelemetryInstaller(this);
            _registeredRunner = _runner;
        }

        private void ReleaseRunnerOwner()
        {
            if (_registeredRunner != null)
                _registeredRunner.UnregisterTelemetryInstaller(this);
            _registeredRunner = null;
        }

        private bool FailTelemetryInitialization(string reason)
        {
            _telemetryInitializationFailed = true;
            _telemetryInitializationError = reason;
            Debug.LogError("[LessonGraphV2] V2 telemetry is required for this enabled installer; lesson start is blocked: " + reason, this);
            return false;
        }

        private void OnDestroy()
        {
            if (_telemetryAdapter != null)
            {
                try
                {
                    _telemetryAdapter.CaptureSceneUnload();
                }
                catch (Exception exception)
                {
                    Debug.LogError("[LessonGraphV2] Scene-unload telemetry capture failed: " + exception);
                }
                try
                {
                    _telemetryAdapter.Detach();
                }
                catch (Exception exception)
                {
                    Debug.LogError("[LessonGraphV2] Telemetry event detach failed: " + exception);
                }
                _telemetryAdapter = null;
            }

            // Keep the runner fail-closed through disable and telemetry detachment. In particular,
            // serialized runners on another GameObject cannot fall back to GetComponent here.
            ReleaseRunnerOwner();

            LessonTelemetryWriterV2 writer = _telemetryWriter;
            _telemetryWriter = null;
            if (writer != null && _sessionContext != null)
                FlushAndReleaseSessionWriterAsync(_sessionContext.SessionId, writer);
        }

        private static async Task FlushAndReleaseSessionWriterAsync(string sessionId, LessonTelemetryWriterV2 writer)
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
            catch (Exception exception)
            {
                Debug.LogError("[LessonGraphV2] Scene unload flush failed: " + exception);
            }

            try
            {
                // The session registry roots the writer while it drains, even after this installer
                // is destroyed. This second wait has no scene lifetime and releases ownership only
                // after every retained batch has either succeeded or the writer is explicitly stopped.
                await writer.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogError("[LessonGraphV2] Background telemetry drain failed: " + exception);
            }

            if (writer.PendingCount == 0)
            {
                LessonTelemetryWriterV2.ReleaseSessionWriter(sessionId, writer);
                writer.Dispose();
            }
            else
            {
                Debug.LogError($"[LessonGraphV2] V2 telemetry writer remains registered with {writer.PendingCount} batch(es) pending. Last error: {writer.LastError}");
            }
        }

        private void RetryOptionalConfiguration()
        {
            // Session identity and phrase revisions are captured once before a run starts.
            // Late metadata can still be composed before start; applying it mid-run would make
            // the remote identity differ from the state already owned by the runner.
            if (!_sessionContextCaptured && !_lessonStartRequested && _runner?.CurrentState == null)
                TryCaptureSessionContext();
            if (_runnerConfigured && _sessionContextCaptured && !_telemetryConfigured && !_telemetryInitializationFailed)
                EnsureTelemetryConfigured();
            ConfigureRemoteBridge();
        }

        private bool TryCaptureSessionContext()
        {
            if (_sessionContextCaptured) return true;

            var activeSessionContext = SessionContext.Instance;
            var externalSessionId = activeSessionContext?.SessionId;
            if (string.IsNullOrWhiteSpace(externalSessionId))
            {
                WarnSessionContextUnavailable("SessionContext.SessionId is empty; V2 lesson start is blocked until session persistence metadata is available.");
                return false;
            }

            VoicePhraseSessionMetadataV2 phraseMetadata;
            if (!VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out phraseMetadata))
            {
                WarnSessionContextUnavailable("the immutable V2 phrase snapshot is unavailable.");
                return false;
            }

            if (!string.Equals(phraseMetadata.LessonId, activeSessionContext.LessonId, StringComparison.Ordinal))
            {
                WarnSessionContextUnavailable("the immutable V2 phrase snapshot belongs to a different lesson.");
                return false;
            }

            try
            {
                var sessionContext = new LessonSessionContextV2(
                    externalSessionId,
                    phraseMetadata.LessonId,
                    phraseMetadata.LaunchToken,
                    phraseMetadata.LessonVoiceRevision,
                    phraseMetadata.ChildPhraseRevision);
                _runner.ConfigureSession(sessionContext);
                _sessionContext = sessionContext;
                _sessionContextCaptured = true;
                _lastSessionContextWarning = null;
                return true;
            }
            catch (ArgumentException exception)
            {
                WarnSessionContextUnavailable($"session metadata is invalid: {exception.Message}");
                return false;
            }
        }

        private void WarnSessionContextUnavailable(string reason)
        {
            if (string.Equals(reason, _lastSessionContextWarning, StringComparison.Ordinal)) return;
            _lastSessionContextWarning = reason;
            Debug.LogWarning($"[LessonGraphV2] Remote control unavailable because {reason}", this);
        }

        private void ConfigureRemoteBridge()
        {
            var activeSessionContext = SessionContext.Instance;
            var currentSessionId = activeSessionContext?.SessionId;
            var sessionMatches = _sessionContext != null &&
                string.Equals(currentSessionId, _sessionContext.SessionId, StringComparison.Ordinal);
            var phraseSnapshotMatchesSession = sessionMatches && IsCapturedPhraseSnapshotCurrent(activeSessionContext);
            ILiveKitDataPacketClientV2 client = phraseSnapshotMatchesSession ? LiveKitService.Instance : null;
            var bridgeSessionId = sessionMatches ? currentSessionId : string.Empty;
            var shouldBeConfigured = phraseSnapshotMatchesSession && client != null;

            if (_remoteBridge == null && client == null) return;

            if (_remoteBridge == null)
                _remoteBridge = gameObject.AddComponent<LiveKitLessonRemoteBridgeV2>();

            if (!_remoteBridge.isActiveAndEnabled) return;

            if (_remoteBridgeConfigured && ReferenceEquals(client, _configuredRemoteClient) &&
                ReferenceEquals(_runner, _configuredRemoteRunner) &&
                ReferenceEquals(_remoteBridge, _configuredRemoteBridge) &&
                _remoteBridge.IsConfigured == shouldBeConfigured &&
                string.Equals(bridgeSessionId, _configuredRemoteSessionId, StringComparison.Ordinal))
                return;

            if (!shouldBeConfigured)
            {
                if (_remoteBridge.IsConfigured)
                    _remoteBridge.Configure(null, _runner);

                _configuredRemoteClient = client;
                _configuredRemoteRunner = _runner;
                _configuredRemoteBridge = _remoteBridge;
                _configuredRemoteSessionId = bridgeSessionId;
                _remoteBridgeConfigured = true;
                return;
            }

            _remoteBridge.Configure(client, _runner);
            _configuredRemoteClient = client;
            _configuredRemoteRunner = _runner;
            _configuredRemoteBridge = _remoteBridge;
            _configuredRemoteSessionId = bridgeSessionId;
            _remoteBridgeConfigured = true;
        }

        private bool IsRemoteBridgeAttachedToCurrentSession()
        {
            if (!_remoteBridgeConfigured || _remoteBridge == null || !_remoteBridge.isActiveAndEnabled ||
                _configuredRemoteBridge == null ||
                !_remoteBridge.IsConfigured ||
                !ReferenceEquals(_remoteBridge, _configuredRemoteBridge) || _configuredRemoteClient == null ||
                !ReferenceEquals(_runner, _configuredRemoteRunner) || _sessionContext == null)
                return false;

            if (_configuredRemoteClient is UnityEngine.Object unityClient && unityClient == null)
                return false;

            var currentSessionId = SessionContext.Instance?.SessionId;
            return string.Equals(currentSessionId, _sessionContext.SessionId, StringComparison.Ordinal) &&
                   string.Equals(currentSessionId, _configuredRemoteSessionId, StringComparison.Ordinal) &&
                   IsCapturedPhraseSnapshotCurrent(SessionContext.Instance);
        }

        private bool IsCapturedPhraseSnapshotCurrent(SessionContext activeSessionContext)
        {
            if (_sessionContext == null || activeSessionContext == null) return false;

            VoicePhraseSessionMetadataV2 phraseMetadata;
            if (!VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out phraseMetadata)) return false;

            return string.Equals(activeSessionContext.LessonId, _sessionContext.LessonId, StringComparison.Ordinal) &&
                   string.Equals(phraseMetadata.LessonId, activeSessionContext.LessonId, StringComparison.Ordinal) &&
                   string.Equals(phraseMetadata.LaunchToken, _sessionContext.LaunchToken, StringComparison.Ordinal) &&
                   phraseMetadata.LessonVoiceRevision == _sessionContext.LessonVoiceRevision &&
                   phraseMetadata.ChildPhraseRevision == _sessionContext.ChildPhraseRevision;
        }
    }
}
