using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    /// <summary>One participant-wide owner for cache-aware fixed speech and its RTC source.</summary>
    [DisallowMultipleComponent]
    public sealed class FixedAudioPlaybackCoordinatorV1 : MonoBehaviour
    {
        private static FixedAudioPlaybackCoordinatorV1 _instance;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly HashSet<string> _activeActivationIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedOpeningActivationIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _handledReminderIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _handledReminderOrder = new Queue<string>();
        private readonly Dictionary<string, Owner> _retiringStreamOwners = new Dictionary<string, Owner>(StringComparer.Ordinal);
        private readonly Queue<ReadyCandidate> _pendingReady = new Queue<ReadyCandidate>();
        private readonly HashSet<string> _pendingReadyKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _activitySequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Queue<string> _activitySequenceOrder = new Queue<string>();
        private JObject _latestActivity;
        private string _latestActivityActivationId = string.Empty;
        private ILiveKitDataPacketClientV2 _client;
        private ILiveKitParticipantDataPacketClientV1 _participantClient;
        private IFixedAudioOwnershipRouterV1 _router;
        private ITtsAudioCacheWarmerV1 _resolver;
        private SessionTtsSnapshotV1 _snapshot;
        private Owner _current;
        private Owner _deferredResumeOwner;
        private bool _readyWarmActive;
        private bool _foregroundStartedWhileWarm;
        private string _agentParticipantIdentity = string.Empty;
        private string _authenticatedAgentInstanceId = string.Empty;
        private long _ownerRevision;
        private string _currentRunId = string.Empty;
        private bool _disposed;

        private sealed class Owner
        {
            public FixedUtteranceV1 Request;
            public CancellationTokenSource Cancellation;
            public IDisposable RoutingLease;
            public LocalTtsPlaybackHandleV1 LocalHandle;
            public string Source = "none";
            public string AgentInstanceId = string.Empty;
            public bool Terminal;
        }

        private sealed class ReadyCandidate
        {
            public ArtifactV1 Artifact;
            public string AgentInstanceId;
            public string SenderIdentity;
            public float QueuedAt;
            public string Key => Artifact.profile_id + "/" + Artifact.key + "/" + Artifact.rejection_epoch;
        }

        public static FixedAudioPlaybackCoordinatorV1 Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var existing = FindObjectOfType<FixedAudioPlaybackCoordinatorV1>();
                if (existing != null) return existing;
                var host = new GameObject(nameof(FixedAudioPlaybackCoordinatorV1));
                DontDestroyOnLoad(host);
                return host.AddComponent<FixedAudioPlaybackCoordinatorV1>();
            }
        }

        public event Action<InvocationV1, string, string> DialogueTerminal;
        public event Action<CachePacketV1> AgentReminderRequested;
        public event Action FixedAudioOwnershipStarting;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            TryConfigureFromSession();
        }

        public void Configure(SessionTtsSnapshotV1 snapshot, string backendUrl, string bearerToken,
            ILiveKitDataPacketClientV2 client, INpcAudioRouterV2 router)
        {
            CancelCurrent("reconfigured", true);
            DiscardRetiringStreamOwners();
            ClearPendingReady();
            DiscardDeferredResume(false);
            _resolver?.Dispose();
            Unsubscribe();
            _activeActivationIds.Clear();
            _completedOpeningActivationIds.Clear();
            _currentRunId = string.Empty;
            _latestActivity = null;
            _latestActivityActivationId = string.Empty;
            _agentParticipantIdentity = string.Empty;
            _authenticatedAgentInstanceId = string.Empty;
            _snapshot = snapshot;
            _client = client;
            _participantClient = client as ILiveKitParticipantDataPacketClientV1;
            _router = router as IFixedAudioOwnershipRouterV1;
            if (snapshot == null) { _resolver = null; return; }
            _resolver = new TtsAudioResolverV1(
                new TtsLocalCacheV1(Application.persistentDataPath),
                new TtsCacheBackendClientV1(backendUrl, bearerToken), snapshot);
            if (_participantClient != null)
                _participantClient.DataReceivedByParticipantV1 += OnParticipantData;
            _resolver.BeginLaunchReconciliation();
        }

        private void TryConfigureFromSession()
        {
            var context = SessionContext.Instance;
            if (context?.TtsCacheSnapshot == null || string.IsNullOrWhiteSpace(context.TtsCacheBackendUrl)) return;
            Configure(context.TtsCacheSnapshot, context.TtsCacheBackendUrl, context.LiveKitToken,
                LiveKitService.Instance, LiveKitService.Instance);
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action)) action();
            _current?.LocalHandle?.Tick();
        }

        public string SessionId => _snapshot?.Scope?.session_id ?? string.Empty;

        public bool IsCacheAware
        {
            get
            {
                if (_snapshot == null || _resolver == null) TryConfigureFromSession();
                return _snapshot != null && _resolver != null;
            }
        }

        public void RegisterActivation(string runId, string activationId)
        {
            if (!IsCacheAware) TryConfigureFromSession();
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(activationId)) return;
            if (!IsCacheAware) return;
            if (!string.IsNullOrEmpty(_currentRunId) && !string.Equals(_currentRunId, runId, StringComparison.Ordinal))
            {
                CancelCurrent("scope_changed", true);
                _activeActivationIds.Clear();
                _completedOpeningActivationIds.Clear();
                _latestActivity = null;
                _latestActivityActivationId = string.Empty;
            }
            if (_activeActivationIds.Count >= 1024 && !_activeActivationIds.Contains(activationId)) return;
            if (!string.IsNullOrEmpty(_latestActivityActivationId) &&
                !_activeActivationIds.Contains(_latestActivityActivationId))
            {
                _latestActivity = null;
                _latestActivityActivationId = string.Empty;
            }
            _currentRunId = runId;
            _activeActivationIds.Add(activationId);
            PublishReconcileIfReady();
        }

        public void UnregisterActivation(string activationId)
        {
            if (string.IsNullOrWhiteSpace(activationId)) return;
            _activeActivationIds.Remove(activationId);
            _completedOpeningActivationIds.Remove(activationId);
            if (string.Equals(_latestActivityActivationId, activationId, StringComparison.Ordinal))
            {
                _latestActivity = null;
                _latestActivityActivationId = string.Empty;
            }
            if (_current != null && string.Equals(_current.Request.invocation.activation_id, activationId, StringComparison.Ordinal))
                Cancel(_current.Request.invocation, "scope_changed");
            PublishReconcileIfReady();
        }

        /// <summary>Schedules preparation and returns immediately so the existing node timer remains authoritative.</summary>
        public long Schedule(FixedUtteranceV1 request)
        {
            if (_disposed || !IsCacheAware || request == null || request.invocation == null) return -1;
            if (string.IsNullOrWhiteSpace(_currentRunId) || string.IsNullOrWhiteSpace(request.invocation.run_id) ||
                request.invocation.run_id != _currentRunId || !_activeActivationIds.Contains(request.invocation.activation_id)) return -1;
            try { FixedAudioOwnershipStarting?.Invoke(); }
            catch (Exception exception) { Debug.LogWarning("Could not cancel a pending legacy fixed-audio request: " + exception.Message); }
            CancelCurrent("superseded", true);
            DiscardDeferredResume(false);
            if (_readyWarmActive) _foregroundStartedWhileWarm = true;
            _resolver?.CancelBackgroundWarms();
            request.owner_revision = ++_ownerRevision;
            request.invocation.session_id = _snapshot.Scope.session_id;
            request.invocation.run_id = request.invocation.run_id ?? _currentRunId;
            var owner = new Owner { Request = request, Cancellation = new CancellationTokenSource() };
            _current = owner;
            PublishActivity(owner, "preparing", "none", "PENDING", string.Empty);
            owner.RoutingLease = _router?.AcquireLocalPlayback(request.invocation, request.owner_revision, request.invocation.npc_binding_id);
            if (owner.RoutingLease == null)
            {
                Finish(owner, "SKIPPED", "AUDIO_UNAVAILABLE");
                return -1;
            }
            ProcessAsync(owner);
            return request.owner_revision;
        }

        public bool Cancel(InvocationV1 invocation, string reason)
        {
            if (_current == null || !SameInvocation(_current.Request.invocation, invocation)) return false;
            CancelCurrent(MapCancelReason(reason), true);
            DrainPendingReadyIfIdle();
            return true;
        }

        internal static bool IsCurrentAgentInstance(string packetInstanceId, string currentInstanceId) =>
            !string.IsNullOrWhiteSpace(packetInstanceId) &&
            string.Equals(packetInstanceId, currentInstanceId, StringComparison.Ordinal);

        private async void ProcessAsync(Owner owner)
        {
            ResolvedAudioV1 resolved = null;
            try
            {
                resolved = await _resolver.ResolveAsync(owner.Request, owner.Cancellation.Token);
                if (!IsCurrent(owner)) { resolved?.Dispose(); return; }
                if (resolved.Outcome == ResolvedAudioOutcomeV1.LocalReady && !resolved.IsWithinRemoteBudget)
                {
                    resolved.Dispose();
                    resolved = new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
                }
                switch (resolved.Outcome)
                {
                    case ResolvedAudioOutcomeV1.LocalReady:
                        PlayLocal(owner, resolved);
                        resolved = null;
                        return;
                    case ResolvedAudioOutcomeV1.StreamRequired:
                        SelectStream(owner);
                        return;
                    case ResolvedAudioOutcomeV1.Cancelled:
                        Finish(owner, "CANCELLED", "cancelled");
                        return;
                    default:
                        Finish(owner, "FAILED", resolved.Reason ?? "AUDIO_UNAVAILABLE");
                        return;
                }
            }
            catch (OperationCanceledException) { if (IsCurrent(owner)) Finish(owner, "CANCELLED", "cancelled"); }
            catch (Exception exception) { if (IsCurrent(owner)) Finish(owner, "FAILED", exception.Message); }
            finally { resolved?.Dispose(); }
        }

        private void PlayLocal(Owner owner, ResolvedAudioV1 resolved)
        {
            if (!_routerSource(owner.Request.invocation.npc_binding_id, out var source))
            {
                Finish(owner, "SKIPPED", "AUDIO_UNAVAILABLE");
                resolved.Dispose();
                return;
            }
            owner.Source = "local";
            PublishActivity(owner, "local_selected", "local", "PENDING", string.Empty);
            PublishActivity(owner, "playing", "local", "PENDING", string.Empty);
            owner.LocalHandle = new LocalTtsPlaybackHandleV1(source, resolved, outcome =>
            {
                if (!IsCurrent(owner)) return;
                switch (outcome)
                {
                    case LocalTtsPlaybackOutcomeV1.Success: Finish(owner, "SUCCESS", string.Empty); break;
                    case LocalTtsPlaybackOutcomeV1.Cancelled: Finish(owner, "CANCELLED", "cancelled"); break;
                    default: Finish(owner, "FAILED", "LOCAL_PLAYBACK_FAILED"); break;
                }
            });
            Debug.Log($"[TTS Audio] source={(resolved.Lease != null ? "VR_CACHE" : "CLOUD_CACHE")} event=PLAY_REQUESTED npc={owner.Request.invocation.npc_binding_id} invocation={owner.Request.invocation.invocation_id} key={owner.Request.key}");
        }

        private bool _routerSource(string npcBindingId, out AudioSource source)
        {
            source = null;
            return LiveKitService.Instance != null && LiveKitService.Instance.TryGetNpcAudioRoute(npcBindingId, out source) && source != null;
        }

        private void SelectStream(Owner owner)
        {
            if (_client == null || !_client.IsConnectedV2 || _participantClient == null ||
                string.IsNullOrEmpty(_participantClient.CurrentAgentInstanceIdV1) ||
                string.IsNullOrEmpty(_agentParticipantIdentity) ||
                !_participantClient.IsCurrentAgentParticipantV1(_agentParticipantIdentity))
            {
                Finish(owner, "SKIPPED", "AUDIO_UNAVAILABLE");
                return;
            }
            owner.Source = "stream";
            owner.AgentInstanceId = _participantClient.CurrentAgentInstanceIdV1;
            if (_router == null || !_router.SelectStream(owner.Request.invocation, owner.Request.owner_revision,
                    owner.Request.invocation.npc_binding_id))
            {
                Finish(owner, "SKIPPED", "AUDIO_UNAVAILABLE");
                return;
            }
            PublishActivity(owner, "stream_selected", "stream", "PENDING", string.Empty);
            PublishStreamRequest(owner);
            Debug.Log($"[TTS Audio] source=AGENT_LIVEKIT event=STREAM_REQUESTED npc={owner.Request.invocation.npc_binding_id} invocation={owner.Request.invocation.invocation_id} key={owner.Request.key}");
        }

        private void PublishStreamRequest(Owner owner)
        {
            if (!IsCurrent(owner)) return;
            PublishPacket(new JObject
            {
                ["contract_version"] = 1, ["event"] = "FIXED_AUDIO_STREAM_REQUEST",
                ["session_id"] = owner.Request.invocation.session_id, ["run_id"] = owner.Request.invocation.run_id,
                ["activation_id"] = owner.Request.invocation.activation_id, ["invocation_id"] = owner.Request.invocation.invocation_id,
                ["npc_binding_id"] = owner.Request.invocation.npc_binding_id, ["kind"] = owner.Request.invocation.kind,
                ["owner_revision"] = owner.Request.owner_revision, ["text"] = owner.Request.text,
                ["profile_id"] = owner.Request.profile_id, ["key"] = owner.Request.key,
                ["rejection_epoch"] = owner.Request.rejection_epoch
            });
        }

        private void OnParticipantData(byte[] data, string topic, string senderIdentity, bool senderIsAgent)
        {
            if (!string.Equals(topic, TtsCacheContractsV1.Topic, StringComparison.Ordinal) || data == null || !senderIsAgent) return;
            _mainThreadQueue.Enqueue(() => HandleAgentPacket(data, senderIdentity));
        }

        private void HandleAgentPacket(byte[] data, string senderIdentity)
        {
            if (_disposed || _participantClient == null || !_participantClient.IsCurrentAgentParticipantV1(senderIdentity)) return;
            CachePacketV1 packet;
            try { packet = TtsCacheContractsV1.ParseCachePacket(Encoding.UTF8.GetString(data)); }
            catch { return; }
            if (!string.Equals(packet.session_id, _snapshot?.Scope.session_id, StringComparison.Ordinal)) return;
            var currentInstance = _participantClient.CurrentAgentInstanceIdV1;
            if (packet.@event == "FIXED_AUDIO_HELLO")
            {
                if (!IsCurrentAgentInstance(packet.agent_instance_id, currentInstance)) return;
                var previousInstanceId = _authenticatedAgentInstanceId;
                _agentParticipantIdentity = senderIdentity;
                _authenticatedAgentInstanceId = packet.agent_instance_id;
                if (!string.IsNullOrEmpty(previousInstanceId) && previousInstanceId != packet.agent_instance_id)
                {
                    FailStreamFromRetiredAgent(previousInstanceId);
                    ReleaseRetiringStreamsFrom(previousInstanceId);
                }
                PublishReconcileIfReady();
                return;
            }
            if (!IsCurrentAgentInstance(packet.agent_instance_id, currentInstance)) return;
            if (packet.@event == "AUDIO_CACHE_READY")
            {
                QueueReadyWarm(new ReadyCandidate
                {
                    AgentInstanceId = packet.agent_instance_id,
                    SenderIdentity = senderIdentity,
                    QueuedAt = Time.realtimeSinceStartup,
                    Artifact = new ArtifactV1
                    {
                        profile_id = packet.profile_id, key = packet.key, rejection_epoch = packet.rejection_epoch,
                        object_path = packet.object_path, generation = packet.generation, sha256 = packet.sha256,
                        byte_size = packet.byte_size, sample_count = packet.sample_count,
                        duration_ms = packet.duration_ms, format = packet.format
                    }
                });
            }
            else if (packet.@event == "FIXED_AUDIO_DONE")
            {
                var owner = _current;
                var invocation = new InvocationV1
                {
                    session_id = packet.session_id, run_id = packet.run_id, activation_id = packet.activation_id,
                    invocation_id = packet.invocation_id, npc_binding_id = packet.npc_binding_id, kind = packet.kind
                };
                if (owner != null && owner.Source == "stream" && owner.Request.owner_revision == packet.owner_revision &&
                    SameInvocation(owner.Request.invocation, invocation))
                { Finish(owner, packet.status, packet.reason); return; }
                var retiringKey = RetiringStreamKey(invocation, packet.owner_revision);
                if (_retiringStreamOwners.TryGetValue(retiringKey, out var retiring) && retiring.AgentInstanceId == packet.agent_instance_id)
                {
                    _retiringStreamOwners.Remove(retiringKey);
                    var canResume = _current == null && _retiringStreamOwners.Count == 0 && _deferredResumeOwner == null;
                    ReleaseRoutingLease(retiring, canResume);
                    ResumeDeferredOwnerIfUnblocked();
                    DrainPendingReadyIfIdle();
                }
            }
            else if (packet.@event == "FIXED_AUDIO_REMINDER_REQUEST")
            {
                if (packet.kind != "automatic_reminder" || !RememberReminder(packet.invocation_id)) return;
                var profile = _snapshot.Config.profiles.FirstOrDefault(item => item.profile_id == packet.profile_id);
                if (profile == null) return;
                AgentReminderRequested?.Invoke(packet);
                var request = NewUtterance(packet.run_id, packet.activation_id, packet.invocation_id,
                    packet.npc_binding_id, "automatic_reminder", packet.text, profile, packet.key, 0);
                if (request != null) Schedule(request);
            }
            else if (packet.@event == "FIXED_AUDIO_REMINDER_CANCEL")
            {
                var owner = _current;
                if (owner != null && owner.Request.invocation.kind == "automatic_reminder" &&
                    SameInvocation(owner.Request.invocation, new InvocationV1
                    {
                        session_id = packet.session_id, run_id = packet.run_id, activation_id = packet.activation_id,
                        invocation_id = packet.invocation_id, npc_binding_id = packet.npc_binding_id, kind = packet.kind
                    }))
                    Cancel(owner.Request.invocation, packet.reason);
            }
        }

        public FixedUtteranceV1 NewUtterance(string runId, string activationId, string invocationId,
            string npcBindingId, string kind, string text, TtsProfileV1 profile, string expectedKey = null, long epoch = 0)
        {
            if (!IsCacheAware || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(activationId) ||
                string.IsNullOrWhiteSpace(invocationId) || string.IsNullOrWhiteSpace(npcBindingId) || profile == null ||
                string.IsNullOrWhiteSpace(text)) return null;
            var key = TtsCacheContractsV1.ComputeTtsCacheKey(text, profile);
            if (!string.IsNullOrEmpty(expectedKey) && !string.Equals(key, expectedKey, StringComparison.Ordinal)) return null;
            return new FixedUtteranceV1
            {
                invocation = new InvocationV1
                {
                    session_id = _snapshot.Scope.session_id, run_id = runId, activation_id = activationId,
                    invocation_id = invocationId, npc_binding_id = npcBindingId, kind = kind
                },
                text = text, profile = profile, profile_id = profile.profile_id, key = key, rejection_epoch = epoch
            };
        }

        public FixedUtteranceV1 NewUtteranceForNpc(string runId, string activationId, string invocationId,
            string npcBindingId, string kind, string text)
        {
            if (!IsCacheAware || _snapshot.Config.npc_profiles == null) return null;
            var mapping = _snapshot.Config.npc_profiles.FirstOrDefault(item => item.npc_binding_id == npcBindingId);
            var profile = mapping == null ? null : _snapshot.Config.profiles.FirstOrDefault(item => item.profile_id == mapping.profile_id);
            return NewUtterance(runId, activationId, invocationId, npcBindingId, kind, text, profile);
        }

        private void Finish(Owner owner, string status, string reason)
        {
            if (!IsCurrent(owner) || owner.Terminal) return;
            owner.Terminal = true;
            PublishActivity(owner, "terminal", owner.Source, status, reason ?? string.Empty);
            var dialogueInvocation = owner.Request.invocation.kind == "dialogue" ? owner.Request.invocation : null;
            if (owner.Request.invocation.kind == "opening" && status == "SUCCESS" &&
                _activeActivationIds.Contains(owner.Request.invocation.activation_id))
                _completedOpeningActivationIds.Add(owner.Request.invocation.activation_id);
            owner.LocalHandle = null;
            if (_retiringStreamOwners.Count > 0)
            {
                DiscardDeferredResume(false);
                _deferredResumeOwner = owner;
            }
            else ReleaseRoutingLease(owner, true);
            owner.Cancellation.Dispose();
            _current = null;
            if (dialogueInvocation != null) DialogueTerminal?.Invoke(dialogueInvocation, status, reason ?? string.Empty);
            ResumeDeferredOwnerIfUnblocked();
            DrainPendingReadyIfIdle();
        }

        private void CancelCurrent(string reason, bool emitActivity)
        {
            var owner = _current;
            if (owner == null) return;
            _current = null;
            owner.Cancellation.Cancel();
            if (owner.LocalHandle != null)
            {
                var handle = owner.LocalHandle;
                owner.LocalHandle = null;
                handle.Cancel();
            }
            if (owner.Source == "stream" && _client != null && _client.IsConnectedV2)
                PublishPacket(new JObject
                {
                    ["contract_version"] = 1, ["event"] = "FIXED_AUDIO_CANCEL",
                    ["session_id"] = owner.Request.invocation.session_id, ["run_id"] = owner.Request.invocation.run_id,
                    ["activation_id"] = owner.Request.invocation.activation_id, ["invocation_id"] = owner.Request.invocation.invocation_id,
                    ["npc_binding_id"] = owner.Request.invocation.npc_binding_id, ["kind"] = owner.Request.invocation.kind,
                    ["owner_revision"] = owner.Request.owner_revision, ["reason"] = reason
                });
            if (emitActivity) PublishActivity(owner, "terminal", owner.Source, "CANCELLED", reason);
            if (owner.Request.invocation.kind == "dialogue") DialogueTerminal?.Invoke(owner.Request.invocation, "CANCELLED", reason);
            if (owner.Source == "stream")
            {
                // Stop the participant-wide RTC source now, but retain this exact lease until the
                // correlated DONE (or authenticated replacement boot) proves the old stream ended.
                try { _router?.ReleaseLocalPlayback(owner.Request.invocation, owner.Request.owner_revision, false); }
                catch (Exception exception) { Debug.LogWarning("Could not suspend the canceled fixed-audio stream: " + exception.Message); }
                var key = RetiringStreamKey(owner.Request.invocation, owner.Request.owner_revision);
                if (!string.IsNullOrWhiteSpace(owner.AgentInstanceId)) _retiringStreamOwners[key] = owner;
                else ReleaseRoutingLease(owner, false);
            }
            else
            {
                var resume = reason != "superseded" && reason != "scope_changed" && reason != "reconfigured" && reason != "session_ended";
                if (resume && _retiringStreamOwners.Count > 0)
                {
                    DiscardDeferredResume(false);
                    _deferredResumeOwner = owner;
                }
                else ReleaseRoutingLease(owner, resume);
            }
            owner.Cancellation.Dispose();
        }

        private void ReleaseRoutingLease(Owner owner, bool resumeOrdinaryAudio)
        {
            if (owner == null) return;
            try { _router?.ReleaseLocalPlayback(owner.Request.invocation, owner.Request.owner_revision, resumeOrdinaryAudio); }
            catch (Exception exception) { Debug.LogWarning("Could not release fixed-audio routing ownership: " + exception.Message); }
            owner.RoutingLease?.Dispose();
            owner.RoutingLease = null;
        }

        private void QueueReadyWarm(ReadyCandidate candidate)
        {
            if (candidate?.Artifact == null || _resolver == null || _disposed) return;
            if (!_pendingReadyKeys.Add(candidate.Key)) return;
            if (_pendingReady.Count >= 64)
            {
                var dropped = _pendingReady.Dequeue();
                _pendingReadyKeys.Remove(dropped.Key);
            }
            _pendingReady.Enqueue(candidate);
            DrainPendingReadyIfIdle();
        }

        private void DrainPendingReadyIfIdle()
        {
            if (_disposed || _current != null || _retiringStreamOwners.Count > 0 || _readyWarmActive ||
                _pendingReady.Count == 0 || _resolver == null || _participantClient == null) return;
            var candidate = _pendingReady.Dequeue();
            if (Time.realtimeSinceStartup - candidate.QueuedAt > TtsCacheBackendClientV1.BackgroundWarmTimeoutMs / 1000f)
            {
                _pendingReadyKeys.Remove(candidate.Key);
                DrainPendingReadyIfIdle();
                return;
            }
            if (candidate.AgentInstanceId != _authenticatedAgentInstanceId ||
                candidate.SenderIdentity != _agentParticipantIdentity ||
                !_participantClient.IsCurrentAgentParticipantV1(candidate.SenderIdentity) ||
                candidate.AgentInstanceId != _participantClient.CurrentAgentInstanceIdV1)
            {
                _pendingReadyKeys.Remove(candidate.Key);
                DrainPendingReadyIfIdle();
                return;
            }
            _readyWarmActive = true;
            Task<bool> task;
            try { task = _resolver.WarmReadyAsync(candidate.Artifact); }
            catch { task = Task.FromResult(false); }
            if (task == null) task = Task.FromResult(false);
            task.ContinueWith(completed =>
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    _readyWarmActive = false;
                    var retry = _foregroundStartedWhileWarm;
                    _foregroundStartedWhileWarm = false;
                    // Keep the key marked for the whole active task lifetime. Remove it only
                    // after completion so duplicate READY packets cannot enqueue another warm.
                    _pendingReadyKeys.Remove(candidate.Key);
                    if (retry && !_disposed && candidate.AgentInstanceId == _authenticatedAgentInstanceId &&
                        candidate.SenderIdentity == _agentParticipantIdentity)
                    {
                        QueueReadyWarm(candidate);
                    }
                    DrainPendingReadyIfIdle();
                });
            }, TaskScheduler.Default);
        }

        private void ClearPendingReady()
        {
            _pendingReady.Clear();
            _pendingReadyKeys.Clear();
            _foregroundStartedWhileWarm = false;
        }

        private void DiscardDeferredResume(bool resumeOrdinaryAudio)
        {
            var deferred = _deferredResumeOwner;
            _deferredResumeOwner = null;
            if (deferred != null) ReleaseRoutingLease(deferred, resumeOrdinaryAudio);
        }

        private void ResumeDeferredOwnerIfUnblocked()
        {
            if (_current != null || _retiringStreamOwners.Count > 0 || _deferredResumeOwner == null) return;
            var deferred = _deferredResumeOwner;
            _deferredResumeOwner = null;
            ReleaseRoutingLease(deferred, true);
        }

        private static string RetiringStreamKey(InvocationV1 invocation, long revision) =>
            invocation == null ? string.Empty : invocation.session_id + "/" + invocation.run_id + "/" + invocation.activation_id + "/" +
            invocation.invocation_id + "/" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void DiscardRetiringStreamOwners()
        {
            foreach (var owner in _retiringStreamOwners.Values.ToArray()) ReleaseRoutingLease(owner, false);
            _retiringStreamOwners.Clear();
        }

        private void ReleaseRetiringStreamsFrom(string agentInstanceId)
        {
            var retired = _retiringStreamOwners.Where(item => item.Value.AgentInstanceId == agentInstanceId).ToArray();
            foreach (var pair in retired)
            {
                _retiringStreamOwners.Remove(pair.Key);
                var canResume = _current == null && _retiringStreamOwners.Count == 0 && _deferredResumeOwner == null;
                ReleaseRoutingLease(pair.Value, canResume);
            }
            ResumeDeferredOwnerIfUnblocked();
            DrainPendingReadyIfIdle();
        }

        private void FailStreamFromRetiredAgent(string agentInstanceId)
        {
            var owner = _current;
            if (owner == null || owner.Source != "stream" || owner.AgentInstanceId != agentInstanceId) return;
            Finish(owner, "FAILED", "AGENT_RESTARTED");
        }

        private void PublishActivity(Owner owner, string phase, string source, string status, string reason)
        {
            if (owner == null) return;
            var id = owner.Request.invocation.invocation_id;
            if (!_activitySequences.ContainsKey(id))
            {
                _activitySequenceOrder.Enqueue(id);
                while (_activitySequenceOrder.Count > 1024) _activitySequences.Remove(_activitySequenceOrder.Dequeue());
            }
            var sequence = _activitySequences.TryGetValue(id, out var previous) ? previous + 1 : 1;
            _activitySequences[id] = sequence;
            var activity = new JObject
            {
                ["contract_version"] = 1, ["event"] = "FIXED_AUDIO_ACTIVITY",
                ["session_id"] = owner.Request.invocation.session_id, ["run_id"] = owner.Request.invocation.run_id,
                ["activation_id"] = owner.Request.invocation.activation_id, ["invocation_id"] = owner.Request.invocation.invocation_id,
                ["npc_binding_id"] = owner.Request.invocation.npc_binding_id, ["kind"] = owner.Request.invocation.kind,
                ["owner_revision"] = owner.Request.owner_revision, ["activity_sequence"] = sequence,
                ["phase"] = phase, ["source"] = source, ["status"] = status, ["reason"] = reason
            };
            if (_activeActivationIds.Contains(owner.Request.invocation.activation_id))
            {
                _latestActivity = (JObject)activity.DeepClone();
                _latestActivityActivationId = owner.Request.invocation.activation_id;
            }
            if (_client == null || !_client.IsConnectedV2 ||
                _participantClient == null || string.IsNullOrEmpty(_participantClient.CurrentAgentInstanceIdV1)) return;
            PublishPacket(activity);
        }

        private bool RememberReminder(string invocationId)
        {
            if (string.IsNullOrWhiteSpace(invocationId) || !_handledReminderIds.Add(invocationId)) return false;
            _handledReminderOrder.Enqueue(invocationId);
            while (_handledReminderOrder.Count > 1024) _handledReminderIds.Remove(_handledReminderOrder.Dequeue());
            return true;
        }

        private void PublishReconcileIfReady()
        {
            if (_snapshot == null || string.IsNullOrWhiteSpace(_currentRunId) || _client == null || !_client.IsConnectedV2 ||
                _participantClient == null || string.IsNullOrWhiteSpace(_agentParticipantIdentity) ||
                string.IsNullOrWhiteSpace(_participantClient.CurrentAgentInstanceIdV1) ||
                !_participantClient.IsCurrentAgentParticipantV1(_agentParticipantIdentity)) return;
            var packet = new JObject
            {
                ["contract_version"] = 1, ["event"] = "FIXED_AUDIO_RECONCILE",
                ["session_id"] = _snapshot.Scope.session_id, ["run_id"] = _currentRunId,
                ["agent_instance_id"] = _participantClient.CurrentAgentInstanceIdV1,
                ["active_activation_ids"] = new JArray(_activeActivationIds.OrderBy(item => item, StringComparer.Ordinal)),
                ["completed_opening_activation_ids"] = new JArray(_completedOpeningActivationIds
                    .Where(_activeActivationIds.Contains).OrderBy(item => item, StringComparer.Ordinal)),
                ["current_activity"] = _latestActivity == null || !_activeActivationIds.Contains(_latestActivityActivationId)
                    ? JValue.CreateNull() : _latestActivity.DeepClone()
            };
            PublishPacket(packet);
        }

        private void PublishPacket(JObject payload)
        {
            try
            {
                TtsCacheContractsV1.ParseCachePacket(payload.ToString(Formatting.None));
                _client?.PublishDataV2(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)), TtsCacheContractsV1.Topic, true);
            }
            catch (Exception exception) { Debug.LogWarning("Invalid local TTS cache packet suppressed: " + exception.Message); }
        }

        private static string MapCancelReason(string reason)
        {
            switch (reason)
            {
                case "skip": case "scope_changed": case "session_ended": case "user_speaking":
                case "superseded": case "reconfigured": return reason;
                case "lesson_scope_changed": case "source_terminated": return "scope_changed";
                case "bridge_unload": return "session_ended";
                default: return "superseded";
            }
        }

        private bool IsCurrent(Owner owner) => owner != null && ReferenceEquals(_current, owner) && !owner.Terminal;

        private static bool SameInvocation(InvocationV1 left, InvocationV1 right) => left != null && right != null &&
            left.session_id == right.session_id && left.run_id == right.run_id && left.activation_id == right.activation_id &&
            left.invocation_id == right.invocation_id && left.npc_binding_id == right.npc_binding_id && left.kind == right.kind;

        private void Unsubscribe()
        {
            if (_participantClient != null) _participantClient.DataReceivedByParticipantV1 -= OnParticipantData;
            _participantClient = null;
        }

        private void OnDestroy()
        {
            _disposed = true;
            CancelCurrent("session_ended", true);
            DiscardRetiringStreamOwners();
            DiscardDeferredResume(false);
            ClearPendingReady();
            _resolver?.Dispose();
            Unsubscribe();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}
