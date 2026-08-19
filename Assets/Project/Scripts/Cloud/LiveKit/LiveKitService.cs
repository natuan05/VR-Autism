using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient
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
        private readonly Dictionary<string, (AudioStream stream, GameObject go)> remoteAudioStreams = new();
        private AudioSource npcAudioSource;
        private RemoteAudioTrack pendingAudioTrack;

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
            room.TrackSubscribed += OnTrackSubscribed;
            room.TrackUnsubscribed += OnTrackUnsubscribed;

            try
            {
                await room.Connect(roomUrl, token, new global::LiveKit.RoomOptions());
                Debug.Log($"[LiveKitService] ✅ KẾT NỐI PHÒNG THÀNH CÔNG! Room Name: {room.Name} | Participant SID: {room.LocalParticipant?.Sid}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ LỖI KẾT NỐI LIVEKIT: {ex.Message}");
            }
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

            foreach (var entry in remoteAudioStreams.Values)
            {
                try { entry.stream.Dispose(); } catch { }
                if (entry.go != null && entry.go != npcAudioSource?.gameObject)
                {
                    Destroy(entry.go);
                }
            }
            remoteAudioStreams.Clear();

            if (room != null)
            {
                room.DataReceived -= OnDataReceived;
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

        public void SetAudioSource(AudioSource source)
        {
            npcAudioSource = source;
            if (source != null)
            {
                Debug.Log($"[LiveKitService] 🔊 Đã cập nhật NPC AudioSource: '{source.gameObject.name}'");

                // Nếu có luồng âm thanh AI vừa đăng ký trước đó đang đứng chờ -> Bind ngay vào AudioSource này!
                if (pendingAudioTrack != null)
                {
                    Debug.Log($"[LiveKitService] 🔗 Tự động kết nối luồng tiếng AI đang chờ vào AudioSource của '{source.gameObject.name}'!");
                    BindAudioTrack(pendingAudioTrack, source);
                    pendingAudioTrack = null;
                }
            }
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                Debug.Log($"[LiveKitService] 🔊 AI AGENT VỪA ĐĂNG KÝ LUỒNG ÂM THANH! (Participant: {participant.Identity} | Track: {audioTrack.Sid})");

                if (npcAudioSource == null)
                {
                    Debug.Log("[LiveKitService] ⏳ Chưa có NPC AudioSource tại thời điểm đăng ký. Đang lưu luồng âm thanh vào hàng chờ (Pending)...");
                    pendingAudioTrack = audioTrack;
                    return;
                }

                BindAudioTrack(audioTrack, npcAudioSource);
            }
        }

        private void BindAudioTrack(RemoteAudioTrack audioTrack, AudioSource targetSource)
        {
            if (audioTrack == null || targetSource == null) return;

            // Dọn dẹp AudioStream cũ nếu cùng Track SID được đăng ký lại
            if (remoteAudioStreams.TryGetValue(audioTrack.Sid, out var existingEntry))
            {
                try { existingEntry.stream.Dispose(); } catch { }
                remoteAudioStreams.Remove(audioTrack.Sid);
                Debug.Log($"[LiveKitService] 🧹 Đã Dispose AudioStream cũ trùng lặp cho Track '{audioTrack.Sid}'");
            }

            AudioStream audiostream = new AudioStream(audioTrack, targetSource);
            remoteAudioStreams[audioTrack.Sid] = (audiostream, targetSource.gameObject);

            Debug.Log($"[LiveKitService] 🔊 ĐÃ KẾT NỐI LUỒNG TIẾNG AI VÀO AUDIOSOURCE CỦA NPC '{targetSource.gameObject.name}' THÀNH CÔNG!");
        }

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                if (remoteAudioStreams.TryGetValue(audioTrack.Sid, out var entry))
                {
                    try { entry.stream.Dispose(); } catch { }
                    remoteAudioStreams.Remove(audioTrack.Sid);
                    Debug.Log($"[LiveKitService] 🔇 Đã dọn dẹp AudioStream cho Track HỦY ĐĂNG KÝ '{audioTrack.Sid}'");
                }
            }
        }

        #endregion

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}