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

        private RemoteAudioTrack pendingAudioTrack;

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

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}