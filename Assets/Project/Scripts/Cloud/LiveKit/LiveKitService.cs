using System;
using System.Collections;
using System.Collections.Generic;
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
        private LocalAudioTrack localAudioTrack;
        private GameObject micGameObject;
        private MicrophoneSource micSource;
        private readonly LiveKitNpcAudioRouter _audioRouter = new LiveKitNpcAudioRouter();

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

        [Serializable]
        private class DataPacketEvent
        {
            public string @event;
            public string quest_name;
            public string status;
            public string reason;
            public string text;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
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

            if (localAudioTrack != null)
            {
                if (room != null && room.LocalParticipant != null)
                {
                    room.LocalParticipant.UnpublishTrack(localAudioTrack, false);
                }
                localAudioTrack = null;
            }
            if (micSource != null)
            {
                micSource.Dispose();
                micSource = null;
            }
            if (micGameObject != null)
            {
                Destroy(micGameObject);
                micGameObject = null;
            }

            _audioRouter.Reset();

            if (room != null)
            {
                room.DataReceived -= OnDataReceived;
                room.Reconnected -= OnRoomReconnectedV2;
                room.TrackSubscribed -= OnTrackSubscribed;
                room.TrackUnsubscribed -= OnTrackUnsubscribed;
                room.Disconnect();
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
            room.LocalParticipant.PublishData(data, null, reliable, topic);
        }

        public void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi Quest: Room chưa kết nối hoặc NULL!");
                return;
            }

            string phrasesJson = defaultPhrases != null && defaultPhrases.Length > 0
                ? "[\"" + string.Join("\",\"", defaultPhrases) + "\"]"
                : "[]";

            string jsonPayload = $"{{\"event\":\"SET_ACTIVE_QUEST\",\"quest_name\":\"{questName}\",\"default_phrases\":{phrasesJson}}}";

            byte[] data = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            room.LocalParticipant.PublishData(data, reliable: true);
            Debug.Log($"[LiveKitService] 📡 GỬI DỮ LIỆU QUEST LÊN SERVER: {jsonPayload}");
        }

        public void SendVerbalHint()
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi VerbalHint: Room chưa kết nối!");
                return;
            }

            string jsonPayload = "{\"event\":\"VERBAL_HINT\"}";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            room.LocalParticipant.PublishData(data, reliable: true);
            Debug.Log($"[LiveKitService] 📡 GỬI VERBAL_HINT LÊN AGENT: {jsonPayload}");
        }

        public void SendOnReminder()
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi OnReminder: Room chưa kết nối!");
                return;
            }

            string jsonPayload = "{\"event\":\"ON_REMINDER\"}";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            room.LocalParticipant.PublishData(data, reliable: true);
            Debug.Log($"[LiveKitService] 📡 GỬI ON_REMINDER LÊN AGENT: {jsonPayload}");
        }

        private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
        {
            if (string.Equals(topic, "lesson-graph-v2.voice", StringComparison.Ordinal))
            {
                DataReceivedV2?.Invoke(data, topic);
                return;
            }
            string json = System.Text.Encoding.UTF8.GetString(data);
            Debug.Log($"[LiveKitService] 📥 NHẬN GÓI TIN TỪ ({participant?.Identity}): {json}");

            try
            {
                var packet = JsonUtility.FromJson<DataPacketEvent>(json);
                if (packet == null || string.IsNullOrEmpty(packet.@event))
                {
                    // Fallback substring check
                    if (json.Contains("QUEST_MATCHED"))
                    {
                        OnSpeechMatched?.Invoke();
                    }
                    return;
                }

                switch (packet.@event)
                {
                    case "QUEST_MATCHED":
                        Debug.Log("[LiveKitService] 🎯 QUEST_MATCHED -> Kích hoạt OnSpeechMatched!");
                        OnSpeechMatched?.Invoke();
                        break;

                    case "AGENT_INIT_FAILED":
                        Debug.LogError($"[LiveKitService] ❌ AGENT_INIT_FAILED: {packet.reason}");
                        OnAgentError?.Invoke(packet.reason);
                        break;

                    case "QUEST_STATUS":
                        Debug.Log($"[LiveKitService] 📋 QUEST_STATUS: {packet.quest_name} -> {packet.status}");
                        OnQuestStatusUpdate?.Invoke(packet.quest_name, packet.status);
                        break;

                    default:
                        Debug.Log($"[LiveKitService] ℹ️ Unhandled packet event: {packet.@event}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ Lỗi parse DataPacket: {ex.Message}");
            }
        }

        #endregion

        #region Audio & Microphone

        public async void EnableMicrophone(bool enable)
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning($"[LiveKitService] ⚠️ Không thể {(enable ? "bật" : "tắt")} Mic: Room chưa kết nối!");
                return;
            }

            try
            {
                if (enable)
                {
                    if (localAudioTrack == null)
                    {
                        if (Microphone.devices != null && Microphone.devices.Length > 0)
                        {
                            string microphoneDevice = Microphone.devices[0];
                            Debug.Log($"[LiveKitService] 🎙️ Tìm thấy Mic phần cứng: '{microphoneDevice}'. Đang khởi tạo luồng...");

                            micGameObject = new GameObject($"LiveKitMic_{microphoneDevice}");
                            micGameObject.transform.SetParent(transform);

                            micSource = new MicrophoneSource(microphoneDevice, micGameObject);
                            localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", micSource, room);

                            var options = new TrackPublishOptions
                            {
                                AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
                                Source = TrackSource.SourceMicrophone
                            };

                            await room.LocalParticipant.PublishTrack(localAudioTrack, options);
                            micSource.Start();
                            Debug.Log("[LiveKitService] 🎙️ Đã Publish luồng Microphone lên LiveKit Server thành công!");
                        }
                        else
                        {
                            Debug.LogError("[LiveKitService] ❌ Không tìm thấy thiết bị Microphone nào trên máy!");
                        }
                    }
                    if (localAudioTrack != null)
                    {
                        ((ILocalTrack)localAudioTrack).SetMute(false);
                        Debug.Log("[LiveKitService] 🎙️ MICROPHONE ĐANG BẬT & UNMUTE (Đang thu âm)");
                    }
                }
                else
                {
                    if (localAudioTrack != null)
                    {
                        ((ILocalTrack)localAudioTrack).SetMute(true);
                        Debug.Log("[LiveKitService] 🎙️ MICROPHONE ĐÃ TẮT & MUTE");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ Lỗi xử lý Mic: {ex.Message}");
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
            Disconnect();
        }
    }
}
