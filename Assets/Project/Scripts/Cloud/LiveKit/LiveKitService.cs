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
        private readonly Dictionary<string, (AudioStream stream, GameObject go)> remoteAudioStreams = new();
        private AudioSource npcAudioSource;
        private RemoteAudioTrack pendingAudioTrack;

        // V2 Audio Routing
        private sealed class PendingTrackEntry
        {
            public RemoteAudioTrack Track { get; }
            public string ParticipantIdentity { get; }

            public PendingTrackEntry(RemoteAudioTrack track, string participantIdentity)
            {
                Track = track;
                ParticipantIdentity = participantIdentity;
            }
        }

        private sealed class ActiveTrackEntry
        {
            public RemoteAudioTrack Track { get; set; }
            public IDisposable Stream { get; set; }
            public AudioSource Source { get; set; }
            public string NpcBindingId { get; set; }
        }

        private readonly Dictionary<string, AudioSource> _npcAudioRoutes = new Dictionary<string, AudioSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingTrackEntry> _pendingAudioTracks = new Dictionary<string, PendingTrackEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActiveTrackEntry> _activeAudioStreams = new Dictionary<string, ActiveTrackEntry>(StringComparer.Ordinal);
        private readonly object _audioRoutingLock = new object();

        public Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory { get; set; }
        public int ActiveV2StreamCount
        {
            get { lock (_audioRoutingLock) return _activeAudioStreams.Count; }
        }
        public int PendingV2TrackCount
        {
            get { lock (_audioRoutingLock) return _pendingAudioTracks.Count; }
        }
        public bool IsV2TrackActive(string trackSid)
        {
            lock (_audioRoutingLock) return _activeAudioStreams.ContainsKey(trackSid);
        }
        public bool IsV2TrackPending(string trackSid)
        {
            lock (_audioRoutingLock) return _pendingAudioTracks.ContainsKey(trackSid);
        }
        private string _activeNpcBindingId = string.Empty;
        public string ActiveNpcBindingId
        {
            get
            {
                lock (_audioRoutingLock)
                {
                    return _activeNpcBindingId;
                }
            }
        }

        public void SimulatePendingAudioTrack(string trackSid, string participantIdentity)
        {
            lock (_audioRoutingLock)
            {
                _pendingAudioTracks[trackSid] = new PendingTrackEntry(null, participantIdentity);
            }
        }

        public void SimulateActiveAudioStream(string trackSid, AudioSource source, string npcBindingId)
        {
            lock (_audioRoutingLock)
            {
                _activeAudioStreams[trackSid] = new ActiveTrackEntry
                {
                    Track = null,
                    Stream = null,
                    Source = source,
                    NpcBindingId = npcBindingId
                };
            }
        }

        public AudioSource GetActiveStreamSource(string trackSid)
        {
            lock (_audioRoutingLock)
            {
                return _activeAudioStreams.TryGetValue(trackSid, out var entry) ? entry.Source : null;
            }
        }

        public string GetActiveStreamRoute(string trackSid)
        {
            lock (_audioRoutingLock)
            {
                return _activeAudioStreams.TryGetValue(trackSid, out var entry) ? entry.NpcBindingId : null;
            }
        }

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

            foreach (var entry in remoteAudioStreams.Values)
            {
                try { entry.stream.Dispose(); } catch { }
                if (entry.go != null && entry.go != npcAudioSource?.gameObject)
                {
                    Destroy(entry.go);
                }
            }
            remoteAudioStreams.Clear();

            foreach (var entry in _activeAudioStreams.Values)
            {
                try { entry.Stream?.Dispose(); } catch { }
            }
            _activeAudioStreams.Clear();
            _pendingAudioTracks.Clear();

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

        private IDisposable CreateAudioStream(RemoteAudioTrack track, AudioSource targetSource)
        {
            if (StreamFactory != null)
            {
                return StreamFactory(track, targetSource);
            }
            return track != null && targetSource != null ? new AudioStream(track, targetSource) : null;
        }

        public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                Debug.LogWarning("[LiveKitService] RegisterNpcAudioRoute: npcBindingId must not be empty or whitespace.");
                return;
            }

            if (source == null)
            {
                Debug.LogWarning($"[LiveKitService] RegisterNpcAudioRoute: source cannot be null for route '{npcBindingId}'.");
                return;
            }

            lock (_audioRoutingLock)
            {
                if (_npcAudioRoutes.TryGetValue(npcBindingId, out var existingSource))
                {
                    if (existingSource != source)
                    {
                        // Route source swap / rebind
                        _npcAudioRoutes[npcBindingId] = source;
                        Debug.Log($"[LiveKitService] 🔄 Swapping AudioSource for route '{npcBindingId}' to '{source.gameObject.name}'");

                        foreach (var entry in _activeAudioStreams.Values)
                        {
                            if (string.Equals(entry.NpcBindingId, npcBindingId, StringComparison.Ordinal))
                            {
                                try { entry.Stream?.Dispose(); } catch { }
                                entry.Source = source;
                                entry.Stream = CreateAudioStream(entry.Track, source);
                                Debug.Log($"[LiveKitService] 🔄 Rebound active stream for track '{entry.Track?.Sid}' to new AudioSource '{source.gameObject.name}'");
                            }
                        }
                    }
                }
                else
                {
                    _npcAudioRoutes[npcBindingId] = source;
                    Debug.Log($"[LiveKitService] 🔊 Registered NPC audio route '{npcBindingId}' -> '{source.gameObject.name}'");
                }

                // If this is currently the active route, re-target active streams to it
                if (string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                {
                    foreach (var entry in _activeAudioStreams.Values)
                    {
                        if (entry.Source != source)
                        {
                            try { entry.Stream?.Dispose(); } catch { }
                            entry.Source = source;
                            entry.NpcBindingId = npcBindingId;
                            entry.Stream = CreateAudioStream(entry.Track, source);
                            Debug.Log($"[LiveKitService] 🔄 Re-targeted active stream '{entry.Track?.Sid}' to newly registered active route '{npcBindingId}'");
                        }
                    }
                }

                // Drain any pending tracks matching this route (handles both new routes and route swaps)
                var matchingPendingSids = new List<string>();
                foreach (var kvp in _pendingAudioTracks)
                {
                    if (string.Equals(kvp.Value.ParticipantIdentity, npcBindingId, StringComparison.Ordinal) ||
                        string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                    {
                        matchingPendingSids.Add(kvp.Key);
                    }
                }

                foreach (var sid in matchingPendingSids)
                {
                    if (_pendingAudioTracks.TryGetValue(sid, out var pending))
                    {
                        _pendingAudioTracks.Remove(sid);
                        Debug.Log($"[LiveKitService] 🔗 Binding deferred pending audio track '{sid}' to route '{npcBindingId}'");
                        BindV2AudioTrack(pending.Track, source, npcBindingId, sid);
                    }
                }
            }
        }

        public void UnregisterNpcAudioRoute(string npcBindingId)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId)) return;

            lock (_audioRoutingLock)
            {
                if (string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                {
                    _activeNpcBindingId = string.Empty;
                }

                if (_npcAudioRoutes.Remove(npcBindingId))
                {
                    Debug.Log($"[LiveKitService] 🔇 Unregistered NPC audio route '{npcBindingId}'");
                }

                // Cleanup active streams for this route and preserve tracks in pending so re-registering rebinds them
                var activeToRemove = new List<string>();
                foreach (var kvp in _activeAudioStreams)
                {
                    if (string.Equals(kvp.Value.NpcBindingId, npcBindingId, StringComparison.Ordinal))
                    {
                        try { kvp.Value.Stream?.Dispose(); } catch { }
                        if (kvp.Value.Track != null)
                        {
                            _pendingAudioTracks[kvp.Key] = new PendingTrackEntry(kvp.Value.Track, npcBindingId);
                        }
                        activeToRemove.Add(kvp.Key);
                    }
                }
                foreach (var sid in activeToRemove)
                {
                    _activeAudioStreams.Remove(sid);
                }
            }
        }

        public bool SetActiveNpcRoute(string npcBindingId)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                Debug.LogWarning("[LiveKitService] SetActiveNpcRoute: npcBindingId must not be empty or whitespace.");
                return false;
            }

            lock (_audioRoutingLock)
            {
                _activeNpcBindingId = npcBindingId;

                if (_npcAudioRoutes.TryGetValue(npcBindingId, out var targetSource) && targetSource != null)
                {
                    foreach (var entry in _activeAudioStreams.Values)
                    {
                        if (entry.Source != targetSource)
                        {
                            try { entry.Stream?.Dispose(); } catch { }
                            entry.Source = targetSource;
                            entry.NpcBindingId = npcBindingId;
                            entry.Stream = CreateAudioStream(entry.Track, targetSource);
                            Debug.Log($"[LiveKitService] 🔄 Dynamically re-routed active stream '{entry.Track?.Sid}' to NPC '{npcBindingId}' ({targetSource.gameObject.name})");
                        }
                    }

                    var drainedSids = new List<string>(_pendingAudioTracks.Keys);
                    foreach (var sid in drainedSids)
                    {
                        var pending = _pendingAudioTracks[sid];
                        _pendingAudioTracks.Remove(sid);
                        Debug.Log($"[LiveKitService] 🔗 Binding deferred pending audio track '{sid}' to active route '{npcBindingId}'");
                        BindV2AudioTrack(pending.Track, targetSource, npcBindingId, sid);
                    }
                    return true;
                }
                else
                {
                    if (_npcAudioRoutes.Count == 0)
                    {
                        if (npcAudioSource != null)
                        {
                            Debug.LogWarning($"[LiveKitService] No V2 routes registered; active route '{npcBindingId}' will fallback to primary scene AudioSource.");
                        }
                        else
                        {
                            Debug.LogWarning($"[LiveKitService] Route '{npcBindingId}' not yet registered. Audio tracks will remain pending.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[LiveKitService] Route '{npcBindingId}' not yet registered. Active stream deferred until route registration.");
                    }
                    return false;
                }
            }
        }

        public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                source = null;
                return false;
            }
            lock (_audioRoutingLock)
            {
                return _npcAudioRoutes.TryGetValue(npcBindingId, out source) && source != null;
            }
        }

        private void BindV2AudioTrack(RemoteAudioTrack audioTrack, AudioSource targetSource, string npcBindingId, string trackSid = null)
        {
            string sid = audioTrack?.Sid ?? trackSid;
            if (string.IsNullOrWhiteSpace(sid) || targetSource == null) return;

            if (_activeAudioStreams.TryGetValue(sid, out var existingEntry))
            {
                try { existingEntry.Stream?.Dispose(); } catch { }
                _activeAudioStreams.Remove(sid);
                Debug.Log($"[LiveKitService] 🧹 Disposed duplicate V2 AudioStream for track '{sid}'");
            }

            var stream = CreateAudioStream(audioTrack, targetSource);
            _activeAudioStreams[sid] = new ActiveTrackEntry
            {
                Track = audioTrack,
                Stream = stream,
                Source = targetSource,
                NpcBindingId = npcBindingId
            };

            Debug.Log($"[LiveKitService] 🔊 Bound V2 audio track '{sid}' to NPC AudioSource '{targetSource.gameObject.name}' (route '{npcBindingId}')");
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
                    lock (_audioRoutingLock)
                    {
                        _pendingAudioTracks.Remove(pendingAudioTrack.Sid);
                    }
                    BindAudioTrack(pendingAudioTrack, source);
                    pendingAudioTrack = null;
                }
            }
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                string identity = participant?.Identity;
                Debug.Log($"[LiveKitService] 🔊 Remote audio track subscribed: Participant={identity} | Track={audioTrack.Sid}");

                if (string.IsNullOrWhiteSpace(identity))
                {
                    Debug.LogWarning($"[LiveKitService] ❌ Rejected remote audio track '{audioTrack.Sid}': participant identity is null or empty.");
                    return;
                }

                lock (_audioRoutingLock)
                {
                    string targetRouteId = !string.IsNullOrEmpty(_activeNpcBindingId) && _npcAudioRoutes.ContainsKey(_activeNpcBindingId)
                        ? _activeNpcBindingId
                        : identity;

                    // Check V2 routes
                    if (_npcAudioRoutes.TryGetValue(targetRouteId, out var targetSource))
                    {
                        if (targetSource != null)
                        {
                            BindV2AudioTrack(audioTrack, targetSource, targetRouteId);
                            return;
                        }
                        else
                        {
                            Debug.LogWarning($"[LiveKitService] ⏳ AudioSource for route '{targetRouteId}' is destroyed or null. Storing track '{audioTrack.Sid}' as pending.");
                            _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, targetRouteId);
                            return;
                        }
                    }

                    // If V2 routes are registered in the scene, DO NOT fallback to legacy global npcAudioSource.
                    // Queue the track as pending for this target route.
                    if (_npcAudioRoutes.Count > 0)
                    {
                        Debug.Log($"[LiveKitService] ⏳ Unknown or pending V2 route '{targetRouteId}'. Storing track '{audioTrack.Sid}' in pending routes queue.");
                        _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, targetRouteId);
                        return;
                    }

                    // Legacy fallback: only if no V2 routes are registered
                    if (npcAudioSource == null)
                    {
                        Debug.Log("[LiveKitService] ⏳ Chưa có NPC AudioSource tại thời điểm đăng ký. Đang lưu luồng âm thanh vào hàng chờ (Pending)...");
                        pendingAudioTrack = audioTrack;
                        _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, identity);
                        return;
                    }

                    BindAudioTrack(audioTrack, npcAudioSource);
                }
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
                lock (_audioRoutingLock)
                {
                    if (_activeAudioStreams.TryGetValue(audioTrack.Sid, out var activeEntry))
                    {
                        try { activeEntry.Stream?.Dispose(); } catch { }
                        _activeAudioStreams.Remove(audioTrack.Sid);
                        Debug.Log($"[LiveKitService] 🔇 Cleaned up active V2 AudioStream for unsubscribed track '{audioTrack.Sid}' (route '{activeEntry.NpcBindingId}')");
                    }

                    _pendingAudioTracks.Remove(audioTrack.Sid);
                }

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
