using System;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using System.Collections.Generic;

namespace VRAutism.Cloud.LiveKit
{
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient
    {
        public static LiveKitService Instance { get; private set; }
        public event Action OnSpeechMatched;

        [Header("Test Mode (Auto Connect trong Unity Inspector)")]
        [SerializeField] private bool autoConnectOnStart = false;
        [SerializeField] private string testRoomUrl = "wss://vra-9jrt51dr.livekit.cloud";
        [SerializeField] private string testToken = "";

        private Room room;
        private LocalAudioTrack localAudioTrack;
        private GameObject micGameObject;
        private MicrophoneSource micSource;

        private readonly Dictionary<string, (AudioStream stream, GameObject go)> remoteAudioStreams = new();
        private AudioSource npcAudioSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            else
            {
                Instance = this;
            }
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
                room.Disconnect();
                room = null;
            }
            Debug.Log("[LiveKitService] Disconnected from LiveKit room");
        }

        public void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            if (room == null || !room.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể gửi Quest: Room chưa kết nối hoặc NULL!");
                return;
            }

            string phrasesJson = "[\"" + string.Join("\",\"", defaultPhrases) + "\"]";
            string jsonPayload = $"{{\"event\":\"SET_ACTIVE_QUEST\",\"quest_name\":\"{questName}\",\"default_phrases\":{phrasesJson}}}";

            byte[] data = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            room.LocalParticipant.PublishData(data, reliable: true);
            Debug.Log($"[LiveKitService] 📡 4. ĐÃ GỬI DỮ LIỆU QUEST LÊN SERVER: {jsonPayload}");
        }

        private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            Debug.Log($"[LiveKitService] 📥 5. NHẬN GÓI TIN TỪ SERVER ({participant?.Identity}): {json}");

            if (json.Contains("QUEST_MATCHED"))
            {
                Debug.Log("[LiveKitService] 🎯 Phát hiện từ khóa QUEST_MATCHED -> Kích hoạt sự kiện OnSpeechMatched!");
                OnSpeechMatched?.Invoke();
            }
        }

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
            }
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                Debug.Log($"[LiveKitService] 🔊 AI AGENT VỪA PHÁT GIỌNG NÓI! (Participant: {participant.Identity} | Track: {audioTrack.Sid})");

                AudioSource targetSource = npcAudioSource != null ? npcAudioSource : gameObject.AddComponent<AudioSource>();
                AudioStream audiostream = new AudioStream(audioTrack, targetSource);
                remoteAudioStreams[audioTrack.Sid] = (audiostream, targetSource.gameObject);

                Debug.Log($"[LiveKitService] 🔊 Đã kết nối luồng tiếng AI vào AudioSource của NPC '{targetSource.gameObject.name}'");
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}