using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient, ILiveKitDataPacketClientV2, INpcAudioRouterV2, ILiveKitCoroutineHost
    {
        private static LiveKitService _instance;
        public static LiveKitService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<LiveKitService>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("LiveKitService");
                        _instance = go.AddComponent<LiveKitService>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        public event Action OnSpeechMatched;
        public event Action<string> OnAgentError;
        public event Action<string, string> OnQuestStatusUpdate;
        public event Action<byte[], string> DataReceivedV2;
        public event Action ReconnectedV2;
        public bool IsConnectedV2 => room != null && room.IsConnected;

        [Header("Test Mode (Auto Connect trong Unity Inspector)")]
        [SerializeField] private bool autoConnectOnStart = false;
        [SerializeField] private string testRoomUrl = "wss://vra-9jrt51dr.livekit.cloud";
        [SerializeField] private string testToken = "";

        [Header("Video POV Settings")]
        [SerializeField] private int videoWidth = 1280;
        [SerializeField] private int videoHeight = 720;
        [SerializeField] private int videoFrameRate = 30;

        private Room room;
        
        // Microphone & Audio
        private LiveKitMicrophonePublisher _microphonePublisher;
        private readonly LiveKitNpcAudioRouter _audioRouter = new LiveKitNpcAudioRouter();
        private LiveKitDataPacketTransport _dataPacketTransport;
        private LegacyVoicePacketAdapter _legacyVoicePacketAdapter;
        private ILiveKitRoomAdapter _packetRoomAdapter;
        private RoomConnectionHandle _packetConnectionHandle;
        private long _packetConnectionGeneration;
        private LiveKitPovVideoPublisher _povPublisher;

        public Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory
        {
            get => _audioRouter.StreamFactory;
            set => _audioRouter.StreamFactory = value;
        }

        public int ActiveV2StreamCount => _audioRouter.ActiveV2StreamCount;
        public int PendingV2TrackCount => _audioRouter.PendingV2TrackCount;
        public bool IsV2TrackActive(string trackSid) => _audioRouter.IsV2TrackActive(trackSid);
        public bool IsV2TrackPending(string trackSid) => _audioRouter.IsV2TrackPending(trackSid);
        public string ActiveNpcBindingId => _audioRouter.ActiveNpcBindingId;
        public void SimulatePendingAudioTrack(string trackSid, string participantIdentity) => _audioRouter.SimulatePendingAudioTrack(trackSid, participantIdentity);
        public void SimulateActiveAudioStream(string trackSid, AudioSource source, string npcBindingId) => _audioRouter.SimulateActiveAudioStream(trackSid, source, npcBindingId);
        public AudioSource GetActiveStreamSource(string trackSid) => _audioRouter.GetActiveStreamSource(trackSid);
        public string GetActiveStreamRoute(string trackSid) => _audioRouter.GetActiveStreamRoute(trackSid);
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _dataPacketTransport = new LiveKitDataPacketTransport(() => _packetConnectionHandle);
            _microphonePublisher = new LiveKitMicrophonePublisher(
                new LiveKitMicrophonePublicationFactory(),
                transform);
            _povPublisher = new LiveKitPovVideoPublisher(
                new LiveKitPovPublicationFactory(this),
                this,
                videoWidth,
                videoHeight,
                videoFrameRate);
            _legacyVoicePacketAdapter = new LegacyVoicePacketAdapter(_dataPacketTransport);
            _dataPacketTransport.DataReceivedV2 += RelayDataReceivedV2;
            _dataPacketTransport.LegacyDataReceived += _legacyVoicePacketAdapter.HandleIncoming;
            _legacyVoicePacketAdapter.SpeechMatched += RelaySpeechMatched;
            _legacyVoicePacketAdapter.AgentError += RelayAgentError;
            _legacyVoicePacketAdapter.QuestStatusUpdated += RelayQuestStatusUpdated;
        }

        private void Start()
        {
            if (autoConnectOnStart && !string.IsNullOrEmpty(testRoomUrl) && !string.IsNullOrEmpty(testToken))
            {
                Debug.Log($"[LiveKitService] 🚀 Đang tự động kết nối LiveKit Test (Url: {testRoomUrl})...");
                Connect(testRoomUrl, testToken);
            }
        }

        public async void Connect(string roomUrl, string token)
        {
            Debug.Log($"[LiveKitService] 🌐 Đang bắt đầu kết nối tới LiveKit Server: {roomUrl}...");
            room = new Room();
            _packetRoomAdapter = new LiveKitRoomAdapter(room);
            _packetConnectionHandle = new RoomConnectionHandle(++_packetConnectionGeneration, _packetRoomAdapter);
            room.DataReceived += OnDataReceived;
            room.Reconnected += OnRoomReconnectedV2;
            room.TrackSubscribed += OnTrackSubscribed;
            room.TrackUnsubscribed += OnTrackUnsubscribed;

            try
            {
                await room.Connect(roomUrl, token, new global::LiveKit.RoomOptions());
                Debug.Log($"[LiveKitService] ✅ KẾT NỐI PHÒNG THÀNH CÔNG! Room Name: {room.Name} | Participant SID: {room.LocalParticipant?.Sid}");
                ReconnectedV2?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ LỖI KẾT NỐI LIVEKIT: {ex.Message}");
            }
        }

        private void OnRoomReconnectedV2(Room reconnectedRoom)
        {
            if (ReferenceEquals(room, reconnectedRoom)) ReconnectedV2?.Invoke();
        }

        public void Disconnect()
        {
            _povPublisher?.Disable();

            _microphonePublisher?.Stop();

            _audioRouter.Reset();

            if (room != null)
            {
                room.DataReceived -= OnDataReceived;
                room.Reconnected -= OnRoomReconnectedV2;
                room.TrackSubscribed -= OnTrackSubscribed;
                room.TrackUnsubscribed -= OnTrackUnsubscribed;
                if (_packetRoomAdapter != null)
                {
                    _packetRoomAdapter.Disconnect();
                    _packetRoomAdapter = null;
                }
                else
                {
                    room.Disconnect();
                }
                _packetConnectionHandle = null;
                room = null;
            }
            Debug.Log("[LiveKitService] Disconnected from LiveKit room");
        }

        #region Video POV Stream (720p @ 30 FPS)

        public void EnablePOVCamera(Camera vrCamera)
        {
            Observe(
                _povPublisher.EnableAsync(
                    vrCamera,
                    () => _packetConnectionHandle,
                    generation => _packetConnectionHandle != null &&
                                  _packetConnectionHandle.Generation == generation &&
                                  room != null &&
                                  room.IsConnected,
                    CancellationToken.None),
                "EnablePOVCamera");
        }

        public void DisablePOVCamera()
        {
            _povPublisher?.Disable();
        }

        #endregion

        #region DataPackets

        public void PublishDataV2(byte[] data, string topic, bool reliable)
        {
            if (data == null || room == null || !room.IsConnected || room.LocalParticipant == null) return;
            _dataPacketTransport.PublishDataV2(data, topic, reliable);
        }

        public void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi Quest: Room chưa kết nối hoặc NULL!");
                return;
            }

            _legacyVoicePacketAdapter.SendActiveQuest(questName, defaultPhrases);
        }

        public void SendVerbalHint()
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi VerbalHint: Room chưa kết nối!");
                return;
            }

            _legacyVoicePacketAdapter.SendVerbalHint();
        }

        public void SendOnReminder()
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi OnReminder: Room chưa kết nối!");
                return;
            }

            _legacyVoicePacketAdapter.SendOnReminder();
        }

        private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
        {
            _dataPacketTransport.HandleIncoming(data, participant, topic);
        }

        private void RelayDataReceivedV2(byte[] data, string topic) => DataReceivedV2?.Invoke(data, topic);
        private void RelaySpeechMatched() => OnSpeechMatched?.Invoke();
        private void RelayAgentError(string reason) => OnAgentError?.Invoke(reason);
        private void RelayQuestStatusUpdated(string questName, string status) => OnQuestStatusUpdate?.Invoke(questName, status);

        private void UnwireServices()
        {
            if (_dataPacketTransport == null || _legacyVoicePacketAdapter == null) return;
            _dataPacketTransport.DataReceivedV2 -= RelayDataReceivedV2;
            _dataPacketTransport.LegacyDataReceived -= _legacyVoicePacketAdapter.HandleIncoming;
            _legacyVoicePacketAdapter.SpeechMatched -= RelaySpeechMatched;
            _legacyVoicePacketAdapter.AgentError -= RelayAgentError;
            _legacyVoicePacketAdapter.QuestStatusUpdated -= RelayQuestStatusUpdated;
        }

        #endregion

        #region Audio & Microphone

        public void EnableMicrophone(bool enable)
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning($"[LiveKitService] ⚠️ Không thể {(enable ? "bật" : "tắt")} Mic: Room chưa kết nối!");
                return;
            }

            var handle = _packetConnectionHandle;
            Observe(
                _microphonePublisher.SetEnabledAsync(
                    enable,
                    handle,
                    generation => _packetConnectionHandle != null &&
                                  _packetConnectionHandle.Generation == generation &&
                                  room != null &&
                                  room.IsConnected),
                "EnableMicrophone");
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

        public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source) =>
            _audioRouter.RegisterNpcAudioRoute(npcBindingId, source);

        public void UnregisterNpcAudioRoute(string npcBindingId) =>
            _audioRouter.UnregisterNpcAudioRoute(npcBindingId);

        public bool SetActiveNpcRoute(string npcBindingId) =>
            _audioRouter.SetActiveNpcRoute(npcBindingId);

        public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source) =>
            _audioRouter.TryGetNpcAudioRoute(npcBindingId, out source);

        public void SetAudioSource(AudioSource source) =>
            _audioRouter.SetLegacyAudioSource(source);

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant) =>
            _audioRouter.HandleTrackSubscribed(track, publication, participant);

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant) =>
            _audioRouter.HandleTrackUnsubscribed(track, publication, participant);

        Coroutine ILiveKitCoroutineHost.StartLiveKitCoroutine(IEnumerator routine) => StartCoroutine(routine);

        void ILiveKitCoroutineHost.StopLiveKitCoroutine(Coroutine coroutine) => StopCoroutine(coroutine);
        #endregion

        private void OnDestroy()
        {
            UnwireServices();
            Disconnect();
        }
    }
}
