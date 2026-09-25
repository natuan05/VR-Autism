using System;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Inspector-friendly composition bridge for a LessonGraph V2 runner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphRunnerInstaller : MonoBehaviour
    {
        [SerializeField] private LessonGraph _lessonGraph;
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private LiveKitDialogueTransportV2 _dialogueTransport;
        [SerializeField] private LiveKitLessonRemoteBridgeV2 _remoteBridge;
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

        public LessonGraph LessonGraph => _lessonGraph;
        public LessonGraphRunner Runner => _runner;
        public LessonGraphBindings Bindings => _bindings;
        public LiveKitDialogueTransportV2 DialogueTransport => _dialogueTransport;

        private void Awake()
        {
            if (_runner == null) _runner = GetComponent<LessonGraphRunner>();
            if (_bindings == null) _bindings = GetComponent<LessonGraphBindings>();
            if (_dialogueTransport == null) _dialogueTransport = GetComponent<LiveKitDialogueTransportV2>() ?? FindObjectOfType<LiveKitDialogueTransportV2>();
            if (_remoteBridge == null) _remoteBridge = GetComponent<LiveKitLessonRemoteBridgeV2>();
            Configure();
        }

        private async void Start()
        {
            RetryOptionalConfiguration();
            if (!_startOnStart) return;
            Debug.Log($"[LessonGraphV2] Installer auto-starting lesson graph='{_lessonGraph.name}'", this);
            _lessonStartRequested = true;
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
            if (_lessonGraph == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraph asset.");
            if (_runner == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphRunner component.");
            if (_bindings == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphBindings component.");

            var runnerConfigurationChanged = !_runnerConfigured || !ReferenceEquals(_configuredRunner, _runner) ||
                !ReferenceEquals(_configuredGraph, _lessonGraph) ||
                !ReferenceEquals(_configuredBindings, _bindings) ||
                !ReferenceEquals(_configuredDialogueTransport, _dialogueTransport);
            if (runnerConfigurationChanged)
            {
                _clock = new MonotonicClock();
                _runner.Configure(
                    _lessonGraph,
                    new LessonGraphExecutorRegistry(_bindings, _clock, dialogueTransport: _dialogueTransport),
                    _bindings,
                    _clock);
                _configuredRunner = _runner;
                _configuredGraph = _lessonGraph;
                _configuredBindings = _bindings;
                _configuredDialogueTransport = _dialogueTransport;
                _runnerConfigured = true;
            }

            RetryOptionalConfiguration();
            if (runnerConfigurationChanged)
                Debug.Log($"[LessonGraphV2] Installer configured: graph='{_lessonGraph.name}' runner={_runner.name} bindings={_bindings.name}", this);
        }

        private void RetryOptionalConfiguration()
        {
            // Session identity and phrase revisions are captured once before a run starts.
            // If they arrive later, this run remains local-only; applying them mid-run would
            // make its remote identity differ from the state already owned by the runner.
            if (!_sessionContextCaptured && !_lessonStartRequested && _runner?.CurrentState == null)
                TryCaptureSessionContext();
            ConfigureRemoteBridge();
        }

        private bool TryCaptureSessionContext()
        {
            if (_sessionContextCaptured) return true;

            var activeSessionContext = SessionContext.Instance;
            var externalSessionId = activeSessionContext?.SessionId;
            if (string.IsNullOrWhiteSpace(externalSessionId))
            {
                WarnSessionContextUnavailable("SessionContext.SessionId is empty; local lesson execution remains available.");
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
