using System;
using System.Collections;
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
        private static int _unityMainThreadId = -1;

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
        public bool IsConnectedV2 => _lifecycleCoordinator != null && _lifecycleCoordinator.IsConnected;

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
        private LegacyVoicePacketAdapter _legacyVoicePacketAdapter;
        private LiveKitPovVideoPublisher _povPublisher;

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
            _legacyVoicePacketAdapter = new LegacyVoicePacketAdapter(_dataPacketTransport);
            _dataPacketTransport.DataReceivedV2 += RelayDataReceivedV2;
            _dataPacketTransport.LegacyDataReceived += _legacyVoicePacketAdapter.HandleIncoming;
            _legacyVoicePacketAdapter.SpeechMatched += RelaySpeechMatched;
            _legacyVoicePacketAdapter.AgentError += RelayAgentError;
            _legacyVoicePacketAdapter.QuestStatusUpdated += RelayQuestStatusUpdated;
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
                    ReconnectedV2?.Invoke();
            });
        }

        private void OnLifecycleConnected()
        {
            var handle = _lifecycleCoordinator.CurrentHandle;
            if (handle == null) return;
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                    ReconnectedV2?.Invoke();
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

        public void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            if (!EnsureMainThread(nameof(SendActiveQuest))) return;
            if (!IsConnectedV2)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi Quest: Room chưa kết nối hoặc NULL!");
                return;
            }

            _legacyVoicePacketAdapter.SendActiveQuest(questName, defaultPhrases);
        }

        public void SendVerbalHint()
        {
            if (!EnsureMainThread(nameof(SendVerbalHint))) return;
            if (!IsConnectedV2)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi VerbalHint: Room chưa kết nối!");
                return;
            }

            _legacyVoicePacketAdapter.SendVerbalHint();
        }

        public void SendOnReminder()
        {
            if (!EnsureMainThread(nameof(SendOnReminder))) return;
            if (!IsConnectedV2)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi OnReminder: Room chưa kết nối!");
                return;
            }

            _legacyVoicePacketAdapter.SendOnReminder();
        }

        private void OnDataReceived(
            RoomConnectionHandle handle,
            byte[] data,
            Participant participant,
            DataPacketKind kind,
            string topic)
        {
            var copy = data == null ? null : (byte[])data.Clone();
            _mainThreadExecutor.Post(handle.Generation, () =>
            {
                if (IsCurrentHandle(handle))
                    _dataPacketTransport.HandleIncoming(copy, participant, topic);
            });
        }

        private void RelayDataReceivedV2(byte[] data, string topic) => DataReceivedV2?.Invoke(data, topic);
        private void RelaySpeechMatched() => OnSpeechMatched?.Invoke();
        private void RelayAgentError(string reason) => OnAgentError?.Invoke(reason);
        private void RelayQuestStatusUpdated(string questName, string status) => OnQuestStatusUpdate?.Invoke(questName, status);

        private void UnwireServices()
        {
            if (_dataPacketTransport != null && _legacyVoicePacketAdapter != null)
            {
                _dataPacketTransport.DataReceivedV2 -= RelayDataReceivedV2;
                _dataPacketTransport.LegacyDataReceived -= _legacyVoicePacketAdapter.HandleIncoming;
                _legacyVoicePacketAdapter.SpeechMatched -= RelaySpeechMatched;
                _legacyVoicePacketAdapter.AgentError -= RelayAgentError;
                _legacyVoicePacketAdapter.QuestStatusUpdated -= RelayQuestStatusUpdated;
            }

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
