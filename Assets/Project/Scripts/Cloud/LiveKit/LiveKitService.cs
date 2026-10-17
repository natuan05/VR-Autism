using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using VRAutism.Gameplay.LessonGraphV2.AudioCache;

namespace VRAutism.Cloud.LiveKit
{
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient, ILiveKitDataPacketClientV2, ILiveKitDeferredDataPacketClientV2, INpcAudioRouterV2, ILiveKitCoroutineHost, ILiveKitMicrophoneControlV2, ILiveKitParticipantDataPacketClientV1, IFixedAudioOwnershipRouterV1
    {
        private const int MaxPendingSpeakScriptCancellations = 64;
        private const string SpeakScriptCancellationTopic = "lesson-graph-v2.voice";
        private static LiveKitService _instance;
        private static int _unityMainThreadId = -1;
        private readonly object _agentPresenceGate = new object();
        private readonly Queue<string> _retiredAgentInstanceOrder = new Queue<string>();
        private readonly HashSet<string> _retiredAgentInstanceIds = new HashSet<string>(StringComparer.Ordinal);
        private string _currentAgentParticipantIdentity;
        private string _currentAgentParticipantSid;
        private string _currentAgentInstanceId;
        private string _currentAgentSessionId;
        private bool _agentPresenceAmbiguous;
        private Room _agentRosterRoom;
        private static readonly FieldInfo ParticipantInfoField = typeof(Participant).GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureUnityMainThread()
        {
            Interlocked.CompareExchange(
                ref _unityMainThreadId,
                Thread.CurrentThread.ManagedThreadId,
                -1);
        }
        public static LiveKitService Instance
        {
            get
            {
                if (_instance == null)
                {
                    if (!IsUnityMainThread())
                        return null;

                    _instance = FindObjectOfType<LiveKitService>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("LiveKitService");
                        _instance = go.AddComponent<LiveKitService>();
                        if (Application.isPlaying)
                            DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        public event Action<string> OnAgentError;
        public event Action<byte[], string> DataReceivedV2;
        public event Action<byte[], string, string, bool> DataReceivedByParticipantV1;
        public event Action ReconnectedV2;
        public bool IsConnectedV2 => _lifecycleCoordinator != null && _lifecycleCoordinator.IsConnected;
        public string CurrentAgentInstanceIdV1
        {
            get { lock (_agentPresenceGate) return _agentPresenceAmbiguous ? string.Empty : _currentAgentInstanceId ?? string.Empty; }
        }

        [Header("Test Mode (Auto Connect trong Unity Inspector)")]
        [SerializeField] private bool autoConnectOnStart = false;
        [SerializeField] private string testRoomUrl = "wss://vra-9jrt51dr.livekit.cloud";
        [SerializeField] private string testToken = "";

        [Header("Video POV Settings")]
        [SerializeField] private int videoWidth = 1280;
        [SerializeField] private int videoHeight = 720;
        [SerializeField] private int videoFrameRate = 30;

        private LiveKitMainThreadExecutor _mainThreadExecutor;
        private LiveKitRoomConnection _roomConnection;
        private LiveKitLifecycleCoordinator _lifecycleCoordinator;

        private LiveKitMicrophonePublisher _microphonePublisher;
        private readonly LiveKitNpcAudioRouter _audioRouter = new LiveKitNpcAudioRouter();
        private LiveKitDataPacketTransport _dataPacketTransport;
        private LiveKitPovVideoPublisher _povPublisher;
        private readonly Queue<byte[]> _pendingSpeakScriptCancellations = new Queue<byte[]>();

        public Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory
        {
            get => EnsureMainThread(nameof(StreamFactory)) ? _audioRouter.StreamFactory : null;
            set
            {
                if (EnsureMainThread(nameof(StreamFactory)))
                    _audioRouter.StreamFactory = value;
            }
        }

        public int ActiveV2StreamCount => EnsureMainThread(nameof(ActiveV2StreamCount)) ? _audioRouter.ActiveV2StreamCount : 0;
        public int PendingV2TrackCount => EnsureMainThread(nameof(PendingV2TrackCount)) ? _audioRouter.PendingV2TrackCount : 0;
        public bool IsV2TrackActive(string trackSid) => EnsureMainThread(nameof(IsV2TrackActive)) && _audioRouter.IsV2TrackActive(trackSid);
        public bool IsV2TrackPending(string trackSid) => EnsureMainThread(nameof(IsV2TrackPending)) && _audioRouter.IsV2TrackPending(trackSid);
        public string ActiveNpcBindingId => EnsureMainThread(nameof(ActiveNpcBindingId)) ? _audioRouter.ActiveNpcBindingId : null;
        public void SimulatePendingAudioTrack(string trackSid, string participantIdentity)
        {
            if (EnsureMainThread(nameof(SimulatePendingAudioTrack)))
                _audioRouter.SimulatePendingAudioTrack(trackSid, participantIdentity);
        }
        public void SimulateActiveAudioStream(string trackSid, AudioSource source, string npcBindingId)
        {
            if (EnsureMainThread(nameof(SimulateActiveAudioStream)))
                _audioRouter.SimulateActiveAudioStream(trackSid, source, npcBindingId);
        }
        public AudioSource GetActiveStreamSource(string trackSid) =>
            EnsureMainThread(nameof(GetActiveStreamSource)) ? _audioRouter.GetActiveStreamSource(trackSid) : null;
        public string GetActiveStreamRoute(string trackSid) =>
            EnsureMainThread(nameof(GetActiveStreamRoute)) ? _audioRouter.GetActiveStreamRoute(trackSid) : null;
        private void Awake()
        {
            if (!Application.isPlaying) return;

            _unityMainThreadId = Thread.CurrentThread.ManagedThreadId;

            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _mainThreadExecutor = new LiveKitMainThreadExecutor();
            _microphonePublisher = new LiveKitMicrophonePublisher(
                new LiveKitMicrophonePublicationFactory(),
                transform);
            _povPublisher = new LiveKitPovVideoPublisher(
                new LiveKitPovPublicationFactory(this),
                this,
                videoWidth,
                videoHeight,
                videoFrameRate);
            _roomConnection = new LiveKitRoomConnection(new LiveKitRoomAdapterFactory());
            _lifecycleCoordinator = new LiveKitLifecycleCoordinator(
                _roomConnection,
                _mainThreadExecutor,
                () => _povPublisher?.Disable(),
                () => _microphonePublisher?.Stop(),
                () => _audioRouter.Reset());
            _dataPacketTransport = new LiveKitDataPacketTransport(() => _lifecycleCoordinator.CurrentHandle);
            _dataPacketTransport.DataReceivedV2 += RelayDataReceivedV2;
            _roomConnection.DataReceived += OnDataReceived;
            _roomConnection.Reconnected += OnRoomReconnectedV2;
            _roomConnection.TrackSubscribed += OnTrackSubscribed;
            _roomConnection.TrackUnsubscribed += OnTrackUnsubscribed;
            _lifecycleCoordinator.ConnectedOrReconnected += OnLifecycleConnected;
        }

        private void Start()
        {
            if (autoConnectOnStart && !string.IsNullOrEmpty(testRoomUrl) && !string.IsNullOrEmpty(testToken))
            {
                Debug.Log($"[LiveKitService] 🚀 Đang tự động kết nối LiveKit Test (Url: {testRoomUrl})...");
                Connect(testRoomUrl, testToken);
            }
        }

        private void Update()
        {
            _mainThreadExecutor?.Drain();
        }

        public void Connect(string roomUrl, string token)
        {
            if (!EnsureMainThread(nameof(Connect))) return;
            Debug.Log($"[LiveKitService] 🌐 Đang bắt đầu kết nối tới LiveKit Server: {roomUrl}...");
            Observe(_lifecycleCoordinator.ConnectAsync(roomUrl, token), nameof(Connect));
        }

        private void OnRoomReconnectedV2(RoomConnectionHandle handle)
        {
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                {
                    ResetAgentPresence();
                    DrainPendingSpeakScriptCancellations();
                    ReconnectedV2?.Invoke();
                }
            });
        }

        private void OnLifecycleConnected()
        {
            var handle = _lifecycleCoordinator.CurrentHandle;
            if (handle == null) return;
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                {
                    ResetAgentPresence();
                    ObserveAgentRoster(handle.SdkRoom);
                    DrainPendingSpeakScriptCancellations();
                    ReconnectedV2?.Invoke();
                }
            });
        }

        public void Disconnect()
        {
            if (!EnsureMainThread(nameof(Disconnect))) return;
            Observe(_lifecycleCoordinator.DisconnectAsync(), nameof(Disconnect));
        }

        #region Video POV Stream (720p @ 30 FPS)

        public void EnablePOVCamera(Camera vrCamera)
        {
            if (!EnsureMainThread(nameof(EnablePOVCamera))) return;
            Observe(
                _povPublisher.EnableAsync(
                    vrCamera,
                    () => _lifecycleCoordinator.CurrentHandle,
                    generation => _lifecycleCoordinator.CurrentHandle != null &&
                                  _lifecycleCoordinator.CurrentHandle.Generation == generation,
                    CancellationToken.None),
                nameof(EnablePOVCamera));
        }

        public void DisablePOVCamera()
        {
            if (!EnsureMainThread(nameof(DisablePOVCamera))) return;
            _povPublisher?.Disable();
        }

        #endregion

        #region DataPackets

        public void PublishDataV2(byte[] data, string topic, bool reliable)
        {
            if (!EnsureMainThread(nameof(PublishDataV2)) || data == null || !IsConnectedV2) return;
            var handle = _lifecycleCoordinator.CurrentHandle;
            if (handle == null || handle.SdkRoom == null || handle.SdkRoom.LocalParticipant == null) return;
            _dataPacketTransport.PublishDataV2(data, topic, reliable);
        }

        public bool PublishSpeakScriptCancellationV2(byte[] cancellationPacket)
        {
            if (!EnsureMainThread(nameof(PublishSpeakScriptCancellationV2)) || cancellationPacket == null)
                return false;

            if (CanPublishSpeakScriptCancellation())
            {
                _dataPacketTransport.PublishDataV2(
                    cancellationPacket,
                    SpeakScriptCancellationTopic,
                    true);
                return true;
            }

            if (_pendingSpeakScriptCancellations.Count >= MaxPendingSpeakScriptCancellations)
            {
                _pendingSpeakScriptCancellations.Dequeue();
                Debug.LogWarning("[LiveKitService] Dropped oldest queued SPEAK_SCRIPT cancellation after reaching the bounded reconnect queue.");
            }

            _pendingSpeakScriptCancellations.Enqueue((byte[])cancellationPacket.Clone());
            return true;
        }

        private void DrainPendingSpeakScriptCancellations()
        {
            while (_pendingSpeakScriptCancellations.Count > 0 && CanPublishSpeakScriptCancellation())
            {
                _dataPacketTransport.PublishDataV2(
                    _pendingSpeakScriptCancellations.Peek(),
                    SpeakScriptCancellationTopic,
                    true);
                _pendingSpeakScriptCancellations.Dequeue();
            }
        }

        private bool CanPublishSpeakScriptCancellation()
        {
            if (_dataPacketTransport == null || !IsConnectedV2 || _lifecycleCoordinator == null)
                return false;

            var handle = _lifecycleCoordinator.CurrentHandle;
            return handle != null && handle.IsConnected && handle.Adapter != null &&
                   !string.IsNullOrEmpty(handle.Adapter.LocalParticipantSid);
        }

        private void OnDataReceived(
            RoomConnectionHandle handle,
            byte[] data,
            Participant participant,
            DataPacketKind kind,
            string topic)
        {
            var copy = data == null ? null : (byte[])data.Clone();
            var senderIdentity = participant?.Identity ?? string.Empty;
            var senderSid = participant?.Sid ?? string.Empty;
            var senderIsAgent = IsServerAgentParticipant(participant);
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                {
                    if (string.Equals(topic, TtsCacheContractsV1.Topic, StringComparison.Ordinal) && senderIsAgent)
                        ObserveAgentHello(copy, senderIdentity, senderSid, handle);
                    _dataPacketTransport.HandleIncoming(copy, participant, topic);
                    TryHandleAgentInitializationFailure(copy, topic, senderIsAgent, handle, senderIdentity, senderSid);
                    DataReceivedByParticipantV1?.Invoke(copy == null ? null : (byte[])copy.Clone(), topic, senderIdentity, senderIsAgent);
                }
            });
        }

        internal static bool IsServerAgentInfo(object info)
        {
            try { return info is ParticipantInfo participantInfo && participantInfo.Kind == ParticipantKind.Agent; }
            catch { return false; }
        }

        internal static bool IsServerAgentParticipant(Participant participant)
        {
            if (participant == null || ParticipantInfoField == null) return false;
            try { return IsServerAgentInfo(ParticipantInfoField.GetValue(participant)); }
            catch { return false; }
        }

        private bool TryHandleAgentInitializationFailure(
            byte[] data,
            string topic,
            bool senderIsAgent,
            RoomConnectionHandle handle,
            string senderIdentity,
            string senderSid)
        {
            if (!senderIsAgent || !string.IsNullOrEmpty(topic) || handle == null ||
                !IsCurrentHandle(handle) || string.IsNullOrWhiteSpace(senderIdentity) || string.IsNullOrWhiteSpace(senderSid))
                return false;

            var room = handle.SdkRoom;
            if (room == null || !room.RemoteParticipants.TryGetValue(senderIdentity, out var currentParticipant) ||
                !string.Equals(currentParticipant?.Sid, senderSid, StringComparison.Ordinal) ||
                !IsServerAgentParticipant(currentParticipant))
                return false;

            if (!TryDispatchAgentInitializationFailure(data, topic, senderIsAgent, true, error => OnAgentError?.Invoke(error), out _)) return false;
            return true;
        }

        internal static bool TryDispatchAgentInitializationFailure(
            byte[] data,
            string topic,
            bool senderIsAgent,
            bool participantIsCurrent,
            Action<string> onAgentError,
            out string reason)
        {
            if (!TryParseAgentInitializationFailure(data, topic, senderIsAgent, participantIsCurrent, out reason)) return false;
            Debug.LogWarning($"[LiveKitService] Agent initialization failed: {reason}");
            onAgentError?.Invoke(reason);
            return true;
        }

        internal static bool TryParseAgentInitializationFailure(
            byte[] data,
            string topic,
            bool senderIsAgent,
            bool participantIsCurrent,
            out string reason)
        {
            reason = null;
            if (!senderIsAgent || !participantIsCurrent || !string.IsNullOrEmpty(topic) || data == null) return false;
            try
            {
                var packet = JsonUtility.FromJson<AgentInitializationFailurePacket>(System.Text.Encoding.UTF8.GetString(data));
                if (packet == null || !string.Equals(packet.@event, "AGENT_INIT_FAILED", StringComparison.Ordinal)) return false;
                switch (packet.reason)
                {
                    case "missing_gemini_key":
                    case "missing_gcloud_credentials":
                    case "invalid_vr_cache_metadata":
                    case "tts_session_snapshot_unavailable":
                    case "vr_session_pairing_mismatch":
                        reason = packet.reason;
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        [Serializable]
        private sealed class AgentInitializationFailurePacket
        {
            public string @event;
            public string reason;
        }

        private void ObserveAgentHello(byte[] data, string participantIdentity, string participantSid, RoomConnectionHandle handle)
        {
            if (data == null || string.IsNullOrWhiteSpace(participantIdentity) || string.IsNullOrWhiteSpace(participantSid) || handle == null) return;
            try
            {
                var packet = TtsCacheContractsV1.ParseCachePacket(System.Text.Encoding.UTF8.GetString(data));
                if (packet.@event != "FIXED_AUDIO_HELLO") return;
                var context = VRAutism.Core.SessionContext.Instance;
                if (context?.TtsCacheSnapshot == null ||
                    !string.Equals(packet.session_id, context.TtsCacheSnapshot.Scope.session_id, StringComparison.Ordinal)) return;
                var room = handle.SdkRoom;
                ObserveAgentRoster(room);
                if (room == null || !room.RemoteParticipants.TryGetValue(participantIdentity, out var currentParticipant) ||
                    !IsCurrentAgentIncarnation(participantSid, currentParticipant.Sid) ||
                    !IsServerAgentParticipant(currentParticipant)) return;
                lock (_agentPresenceGate)
                {
                    if (!string.Equals(_currentAgentSessionId, packet.session_id, StringComparison.Ordinal))
                    {
                        _currentAgentSessionId = packet.session_id;
                        _currentAgentInstanceId = null;
                        _agentPresenceAmbiguous = false;
                        _retiredAgentInstanceIds.Clear();
                        _retiredAgentInstanceOrder.Clear();
                    }
                    if (_agentPresenceAmbiguous || !string.Equals(_currentAgentParticipantIdentity, participantIdentity, StringComparison.Ordinal) ||
                        !string.Equals(_currentAgentParticipantSid, participantSid, StringComparison.Ordinal)) return;
                    if (_retiredAgentInstanceIds.Contains(packet.agent_instance_id)) return;
                    if (string.Equals(_currentAgentInstanceId, packet.agent_instance_id, StringComparison.Ordinal)) return;
                    if (!string.IsNullOrEmpty(_currentAgentInstanceId))
                    {
                        RetireAgentInstanceNoLock(_currentAgentInstanceId);
                    }
                    _currentAgentInstanceId = packet.agent_instance_id;
                }
            }
            catch { }
        }

        private void ObserveAgentRoster(Room room)
        {
            if (_agentRosterRoom != room)
            {
                if (_agentRosterRoom != null)
                {
                    _agentRosterRoom.ParticipantConnected -= OnAgentParticipantChanged;
                    _agentRosterRoom.ParticipantDisconnected -= OnAgentParticipantChanged;
                }
                _agentRosterRoom = room;
                if (_agentRosterRoom != null)
                {
                    _agentRosterRoom.ParticipantConnected += OnAgentParticipantChanged;
                    _agentRosterRoom.ParticipantDisconnected += OnAgentParticipantChanged;
                }
            }
            ReconcileAgentRoster(room);
        }

        private void OnAgentParticipantChanged(Participant participant)
        {
            ReconcileAgentRoster(_agentRosterRoom);
        }

        private void ReconcileAgentRoster(Room room)
        {
            var candidates = new List<Tuple<string, string, long>>();
            if (room != null)
            {
                foreach (var participant in room.RemoteParticipants.Values)
                {
                    if (!IsServerAgentParticipant(participant) || !TryGetJoinedAt(participant, out var joinedAt)) continue;
                    candidates.Add(Tuple.Create(participant.Identity, participant.Sid, joinedAt));
                }
            }
            var selectedSid = SelectCurrentAgentSid(candidates, out var tied);
            var selected = string.IsNullOrEmpty(selectedSid) ? null : room.RemoteParticipants.Values.FirstOrDefault(item => item.Sid == selectedSid);
            lock (_agentPresenceGate)
            {
                var ambiguous = tied;
                var identity = ambiguous ? null : selected?.Identity;
                var sid = ambiguous ? null : selected?.Sid;
                if (!string.Equals(_currentAgentParticipantIdentity, identity, StringComparison.Ordinal) ||
                    !string.Equals(_currentAgentParticipantSid, sid, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(_currentAgentInstanceId)) RetireAgentInstanceNoLock(_currentAgentInstanceId);
                    _currentAgentParticipantIdentity = identity;
                    _currentAgentParticipantSid = sid;
                    _currentAgentInstanceId = null;
                }
                _agentPresenceAmbiguous = ambiguous;
            }
        }

        private static bool TryGetJoinedAt(Participant participant, out long joinedAt)
        {
            joinedAt = 0;
            try
            {
                var info = ParticipantInfoField?.GetValue(participant) as ParticipantInfo;
                if (info == null) return false;
                joinedAt = info.JoinedAt;
                return true;
            }
            catch { return false; }
        }

        internal static string SelectCurrentAgentSid(IList<Tuple<string, string, long>> candidates, out bool ambiguous)
        {
            ambiguous = false;
            if (candidates == null || candidates.Count == 0) return null;
            var newest = long.MinValue;
            string selectedSid = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Item1) || string.IsNullOrWhiteSpace(candidate.Item2)) continue;
                if (candidate.Item3 > newest) { newest = candidate.Item3; selectedSid = candidate.Item2; ambiguous = false; }
                else if (candidate.Item3 == newest && !string.Equals(selectedSid, candidate.Item2, StringComparison.Ordinal)) ambiguous = true;
            }
            return ambiguous ? null : selectedSid;
        }

        internal static bool IsCurrentAgentIncarnation(string senderSid, string currentSid) =>
            !string.IsNullOrWhiteSpace(senderSid) && string.Equals(senderSid, currentSid, StringComparison.Ordinal);

        private void RetireAgentInstanceNoLock(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || !_retiredAgentInstanceIds.Add(instanceId)) return;
            _retiredAgentInstanceOrder.Enqueue(instanceId);
            while (_retiredAgentInstanceOrder.Count > 1024)
                _retiredAgentInstanceIds.Remove(_retiredAgentInstanceOrder.Dequeue());
        }

        private void ResetAgentPresence()
        {
            lock (_agentPresenceGate)
            {
                _currentAgentParticipantIdentity = null;
                _currentAgentParticipantSid = null;
                _currentAgentInstanceId = null;
                _agentPresenceAmbiguous = false;
            }
        }

        bool ILiveKitParticipantDataPacketClientV1.IsCurrentAgentParticipantV1(string participantIdentity)
        {
            var handle = _lifecycleCoordinator?.CurrentHandle;
            var room = handle?.SdkRoom;
            if (handle == null || !handle.IsConnected || room == null) return false;
            ReconcileAgentRoster(room);
            lock (_agentPresenceGate)
            {
                if (_agentPresenceAmbiguous || string.IsNullOrEmpty(_currentAgentInstanceId) ||
                    !string.Equals(_currentAgentParticipantIdentity, participantIdentity, StringComparison.Ordinal)) return false;
            }
            lock (_agentPresenceGate)
                return room.RemoteParticipants.TryGetValue(participantIdentity, out var participant) &&
                       IsCurrentAgentIncarnation(participant.Sid, _currentAgentParticipantSid) && IsServerAgentParticipant(participant);
        }

        private void RelayDataReceivedV2(byte[] data, string topic) => DataReceivedV2?.Invoke(data, topic);

        private void UnwireServices()
        {
            if (_agentRosterRoom != null)
            {
                _agentRosterRoom.ParticipantConnected -= OnAgentParticipantChanged;
                _agentRosterRoom.ParticipantDisconnected -= OnAgentParticipantChanged;
                _agentRosterRoom = null;
            }
            if (_dataPacketTransport != null)
                _dataPacketTransport.DataReceivedV2 -= RelayDataReceivedV2;

            if (_roomConnection != null)
            {
                _roomConnection.DataReceived -= OnDataReceived;
                _roomConnection.Reconnected -= OnRoomReconnectedV2;
                _roomConnection.TrackSubscribed -= OnTrackSubscribed;
                _roomConnection.TrackUnsubscribed -= OnTrackUnsubscribed;
            }

            if (_lifecycleCoordinator != null)
                _lifecycleCoordinator.ConnectedOrReconnected -= OnLifecycleConnected;
        }

        #endregion

        #region Audio & Microphone

        public void EnableMicrophone(bool enable)
        {
            if (!EnsureMainThread(nameof(EnableMicrophone))) return;
            if (!IsConnectedV2)
            {
                Debug.LogWarning($"[LiveKitService] ⚠️ Không thể {(enable ? "bật" : "tắt")} Mic: Room chưa kết nối!");
                return;
            }

            var handle = _lifecycleCoordinator.CurrentHandle;
            Observe(
                _microphonePublisher.SetEnabledAsync(
                    enable,
                    handle,
                    generation => _lifecycleCoordinator.CurrentHandle != null &&
                                  _lifecycleCoordinator.CurrentHandle.Generation == generation &&
                                  IsConnectedV2),
                nameof(EnableMicrophone));
        }

        private async void Observe(Task task, string operation)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LiveKitService] {operation} failed: {exception.Message}");
            }
        }

        public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source)
        {
            if (EnsureMainThread(nameof(RegisterNpcAudioRoute)))
                _audioRouter.RegisterNpcAudioRoute(npcBindingId, source);
        }

        public void UnregisterNpcAudioRoute(string npcBindingId)
        {
            if (EnsureMainThread(nameof(UnregisterNpcAudioRoute)))
                _audioRouter.UnregisterNpcAudioRoute(npcBindingId);
        }

        public bool SetActiveNpcRoute(string npcBindingId) =>
            EnsureMainThread(nameof(SetActiveNpcRoute)) && _audioRouter.SetActiveNpcRoute(npcBindingId);

        public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source)
        {
            if (!EnsureMainThread(nameof(TryGetNpcAudioRoute)))
            {
                source = null;
                return false;
            }
            return _audioRouter.TryGetNpcAudioRoute(npcBindingId, out source);
        }

        IDisposable IFixedAudioOwnershipRouterV1.AcquireLocalPlayback(InvocationV1 invocation, long ownerRevision, string npcBindingId)
        {
            if (!EnsureMainThread(nameof(IFixedAudioOwnershipRouterV1.AcquireLocalPlayback))) return null;
            return ((IFixedAudioOwnershipRouterV1)_audioRouter).AcquireLocalPlayback(invocation, ownerRevision, npcBindingId);
        }

        bool IFixedAudioOwnershipRouterV1.SelectStream(InvocationV1 invocation, long ownerRevision, string npcBindingId)
        {
            if (!EnsureMainThread(nameof(IFixedAudioOwnershipRouterV1.SelectStream))) return false;
            return ((IFixedAudioOwnershipRouterV1)_audioRouter).SelectStream(invocation, ownerRevision, npcBindingId);
        }

        void IFixedAudioOwnershipRouterV1.ReleaseLocalPlayback(InvocationV1 invocation, long ownerRevision, bool resumeOrdinaryAudio)
        {
            if (!EnsureMainThread(nameof(IFixedAudioOwnershipRouterV1.ReleaseLocalPlayback))) return;
            ((IFixedAudioOwnershipRouterV1)_audioRouter).ReleaseLocalPlayback(invocation, ownerRevision, resumeOrdinaryAudio);
        }

        public void SetAudioSource(AudioSource source)
        {
            if (EnsureMainThread(nameof(SetAudioSource)))
                _audioRouter.SetLegacyAudioSource(source);
        }

        private void OnTrackSubscribed(
            RoomConnectionHandle handle,
            IRemoteTrack track,
            RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                    _audioRouter.HandleTrackSubscribed(track, publication, participant);
            });
        }

        private void OnTrackUnsubscribed(
            RoomConnectionHandle handle,
            IRemoteTrack track,
            RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                    _audioRouter.HandleTrackUnsubscribed(track, publication, participant);
            });
        }

        Coroutine ILiveKitCoroutineHost.StartLiveKitCoroutine(IEnumerator routine)
        {
            if (!EnsureMainThread(nameof(ILiveKitCoroutineHost.StartLiveKitCoroutine)))
                return null;
            return StartCoroutine(routine);
        }

        void ILiveKitCoroutineHost.StopLiveKitCoroutine(Coroutine coroutine)
        {
            if (EnsureMainThread(nameof(ILiveKitCoroutineHost.StopLiveKitCoroutine)))
                StopCoroutine(coroutine);
        }
        #endregion

        private bool EnsureMainThread(string operation)
        {
            if ((_mainThreadExecutor != null && _mainThreadExecutor.IsOwnerThread) ||
                (_mainThreadExecutor == null && IsUnityMainThread()))
                return true;

            Debug.LogError($"[LiveKitService] {operation} must be called on Unity's main thread.");
            return false;
        }

        private static bool IsUnityMainThread() =>
            (Volatile.Read(ref _unityMainThreadId) != -1 &&
             Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref _unityMainThreadId)) ||
            string.Equals(
                SynchronizationContext.Current?.GetType().FullName,
                "UnityEngine.UnitySynchronizationContext",
                StringComparison.Ordinal);

        private bool IsCurrentHandle(RoomConnectionHandle handle) =>
            handle != null && _lifecycleCoordinator != null &&
            ReferenceEquals(_lifecycleCoordinator.CurrentHandle, handle) &&
            _lifecycleCoordinator.IsConnected;

        private void OnDestroy()
        {
            UnwireServices();
            _lifecycleCoordinator?.Destroy();
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }
    }
}
