using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient, ILiveKitDataPacketClientV2, INpcAudioRouterV2
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
        // POV Video
        private LocalVideoTrack localVideoTrack;
        private TextureVideoSource videoSource;
        private RenderTexture povRenderTexture;
        private Camera captureCamera;
        private Coroutine videoSourceCoroutine;
        private bool isStreamingPOV = false;

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
            DisablePOVCamera();

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

        public async void EnablePOVCamera(Camera vrCamera)
        {
            if (vrCamera == null)
            {
                vrCamera = Camera.main ?? FindObjectOfType<Camera>();
            }

            if (vrCamera == null)
            {
                Debug.LogWarning("[LiveKitService] EnablePOVCamera: vrCamera is null and no Camera found in scene!");
                return;
            }

            // Chờ kết nối phòng nếu đang trong tiến trình Connect
            int waitCount = 0;
            while ((room == null || !room.IsConnected) && waitCount < 50)
            {
                await System.Threading.Tasks.Task.Delay(200);
                waitCount++;
            }

            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể bật POV Camera: Room chưa kết nối!");
                return;
            }

            if (isStreamingPOV)
            {
                Debug.Log("[LiveKitService] POV Camera is already streaming.");
                return;
            }

            try
            {
                isStreamingPOV = true;
                Debug.Log($"[LiveKitService] 📹 Khởi tạo POV Video Stream ({videoWidth}x{videoHeight} @ {videoFrameRate}fps)...");

                // Tạo secondary camera bám theo góc nhìn của trẻ
                GameObject captureCamObj = new GameObject("LiveKit_POVCaptureCamera");
                captureCamObj.transform.SetParent(vrCamera.transform, false);
                captureCamObj.transform.localPosition = Vector3.zero;
                captureCamObj.transform.localRotation = Quaternion.identity;

                captureCamera = captureCamObj.AddComponent<Camera>();
                captureCamera.CopyFrom(vrCamera);
                captureCamera.cullingMask = vrCamera.cullingMask;
                captureCamera.clearFlags = vrCamera.clearFlags;
                captureCamera.backgroundColor = vrCamera.backgroundColor;
                captureCamera.fieldOfView = vrCamera.fieldOfView;
                captureCamera.nearClipPlane = vrCamera.nearClipPlane;
                captureCamera.farClipPlane = vrCamera.farClipPlane;
                captureCamera.depth = vrCamera.depth - 1;
                captureCamera.allowHDR = false;
                captureCamera.allowMSAA = false;

                // Tạo RenderTexture 720p
                povRenderTexture = new RenderTexture(videoWidth, videoHeight, 24, RenderTextureFormat.ARGB32);
                povRenderTexture.name = "LiveKit_POV_Texture";
                povRenderTexture.Create();

                captureCamera.targetTexture = povRenderTexture;
                captureCamera.enabled = true; // Bật để Unity URP tự động render vào targetTexture mỗi frame

                // Cấu hình URP Additional Camera Data nếu có
                var additionalData = captureCamObj.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (additionalData == null)
                {
                    additionalData = captureCamObj.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                }
                if (additionalData != null)
                {
                    additionalData.renderShadows = false; // Tối ưu GPU cho VR
                    additionalData.renderPostProcessing = false;
                }

                // Khởi tạo TextureVideoSource từ LiveKit SDK
                videoSource = new TextureVideoSource(povRenderTexture, VideoBufferType.Rgba);
                videoSource.Start(); // Bật cờ _playing = true của RtcVideoSource
                videoSourceCoroutine = StartCoroutine(videoSource.Update()); // Chạy vòng lặp AsyncGPUReadback và SendFrame

                localVideoTrack = LocalVideoTrack.CreateVideoTrack("pov_camera", videoSource, room);

                var options = new TrackPublishOptions
                {
                    Source = TrackSource.SourceCamera,
                    VideoEncoding = new VideoEncoding
                    {
                        MaxBitrate = 1500000,
                        MaxFramerate = (uint)videoFrameRate
                    }
                };

                await room.LocalParticipant.PublishTrack(localVideoTrack, options);
                Debug.Log("[LiveKitService] ✅ POV Video Track published thành công với luồng frame hoạt động!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ Lỗi khởi tạo POV Camera: {ex.Message}");
                DisablePOVCamera();
            }
        }

        public void DisablePOVCamera()
        {
            if (!isStreamingPOV && captureCamera == null) return;

            isStreamingPOV = false;

            if (videoSourceCoroutine != null)
            {
                StopCoroutine(videoSourceCoroutine);
                videoSourceCoroutine = null;
            }

            if (localVideoTrack != null)
            {
                if (room != null && room.LocalParticipant != null)
                {
                    try { room.LocalParticipant.UnpublishTrack(localVideoTrack, false); } catch { }
                }
                localVideoTrack = null;
            }

            if (videoSource != null)
            {
                try
                {
                    videoSource.Stop();
                    // Chờ GPU AsyncReadback hoàn tất các frame dở dang trước khi huỷ NativeArray
                    UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                    videoSource.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LiveKitService] VideoSource cleanup notice: {ex.Message}");
                }
                videoSource = null;
            }

            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                Destroy(captureCamera.gameObject);
                captureCamera = null;
            }

            if (povRenderTexture != null)
            {
                povRenderTexture.Release();
                Destroy(povRenderTexture);
                povRenderTexture = null;
            }

            Debug.Log("[LiveKitService] 🛑 Đã tắt POV Video Stream");
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
        #endregion

        private void OnDestroy()
        {
            UnwireServices();
            Disconnect();
        }
    }
}
