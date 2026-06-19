using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Firebase.Database;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Lắng nghe lệnh điều khiển từ Web Dashboard.
    ///
    /// Sẽ lắng nghe nhánh RTDB: live_sessions/{sessionId}/commands
    /// và dispatch ra C# Events để các hệ thống Gameplay xử lý.
    ///
    /// Các lệnh dự kiến (WEB_DASHBOARD_IDEAS.md §4):
    ///   - trigger_hint      → NPC phát gợi ý bằng âm thanh & highlight
    ///   - set_volume        → Thay đổi âm lượng môi trường
    ///   - play_npc_script   → Text-to-Speech gửi từ Web
    ///   - skip_quest        → Bỏ qua Quest hiện tại
    ///   - pause_lesson      → Fade-to-black khẩn cấp
    ///
    /// SCENE LIFECYCLE (by design):
    ///   RemoteCommandListener KHÔNG dùng DontDestroyOnLoad.
    ///   Nó được đặt trong mỗi Scene cần nhận lệnh từ xa.
    ///   Mỗi Scene cần tự chứa một instance trên một root GameObject.
    ///   Static events được giữ ở cấp Assembly và không tự reset giữa scenes —
    ///   subscribers phải tự hủy đăng ký trong OnDisable/OnDestroy.
    /// </summary>
    public class RemoteCommandListener : MonoBehaviour
    {
        public static RemoteCommandListener Instance { get; private set; }

        // ── C# Events cho các lệnh điều khiển từ xa (Story 3.1) ──
        public static event System.Action OnTriggerVerbalHint;
        public static event System.Action OnTriggerVisualHint;
        public static event System.Action<float> OnSetVolume;
        public static event System.Action<string> OnPlayNpcScript;
        public static event System.Action OnSkipQuest;
        public static event System.Action OnPauseLesson;
        public static event System.Action OnResumeLesson;

        private string _sessionId;
        private DatabaseReference _commandsRef;
        private bool _isListening = false;

        // ConcurrentQueue để truyền action từ background thread về main thread an toàn
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            var ctx = Core.SessionContext.Instance;
            if (ctx != null && !string.IsNullOrEmpty(ctx.SessionId))
            {
                StartListening(ctx.SessionId);
            }
            else
            {
                Debug.LogWarning("[RemoteCommandListener] SessionContext trống hoặc SessionId rỗng, chưa bắt đầu lắng nghe.");
            }
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void OnDestroy()
        {
            StopListening();
        }

        public void StartListening(string sessionId)
        {
            if (_isListening)
            {
                StopListening();
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[RemoteCommandListener] StartListening: Session ID trống.");
                return;
            }

            _sessionId = sessionId;
            var root = RTDBConnection.Instance?.RootRef;
            if (root == null)
            {
                Debug.LogError("[RemoteCommandListener] StartListening: RTDB Connection RootRef is null.");
                return;
            }

            _commandsRef = root.Child("live_sessions").Child(_sessionId).Child("commands");
            _commandsRef.ChildAdded += OnCommandChildAdded;
            _isListening = true;
            Debug.Log($"[RemoteCommandListener] 📡 Bắt đầu lắng nghe commands tại: live_sessions/{_sessionId}/commands");
        }

        public void StopListening()
        {
            if (!_isListening) return;

            if (_commandsRef != null)
            {
                _commandsRef.ChildAdded -= OnCommandChildAdded;
                _commandsRef = null;
            }

            _isListening = false;
            Debug.Log("[RemoteCommandListener] 📡 Đã dừng lắng nghe commands.");
        }

        private void OnCommandChildAdded(object sender, ChildChangedEventArgs args)
        {
            if (args == null || args.Snapshot == null || !args.Snapshot.Exists) return;

            var snapshot = args.Snapshot;
            string commandId = snapshot.Key;

            Debug.Log($"[RemoteCommandListener] Nhận được command mới từ RTDB: {commandId}");

            // Đọc dữ liệu lệnh
            var data = snapshot.Value as Dictionary<string, object>;
            if (data == null)
            {
                Debug.LogWarning($"[RemoteCommandListener] Dữ liệu lệnh {commandId} không đúng định dạng Dictionary.");
                return;
            }

            if (data.TryGetValue("command_type", out var typeObj) && typeObj != null)
            {
                string commandType = typeObj.ToString().ToLower();
                object paramVal = null;
                data.TryGetValue("param", out paramVal);

                // Đưa lệnh vào queue để chạy trên Main Thread
                _mainThreadQueue.Enqueue(() =>
                {
                    ProcessCommand(commandType, paramVal);
                });
            }

            // Xóa lệnh khỏi RTDB ngay lập tức để tránh lặp lại
            snapshot.Reference.RemoveValueAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogWarning($"[RemoteCommandListener] Không thể xóa command {commandId} trên RTDB: {task.Exception}");
                }
                else
                {
                    Debug.Log($"[RemoteCommandListener] Đã xóa command {commandId} trên RTDB thành công.");
                }
            });
        }

        private void ProcessCommand(string commandType, object paramObj)
        {
            switch (commandType)
            {
                case "trigger_verbal_hint":
                    TriggerVerbalHint();
                    break;

                case "trigger_visual_hint":
                    TriggerVisualHint();
                    break;

                case "skip_quest":
                case "next_step":
                    TriggerSkipQuest();
                    break;

                case "pause_lesson":
                    TriggerPauseLesson();
                    break;

                case "resume_lesson":
                case "play_lesson":
                    TriggerResumeLesson();
                    break;

                case "set_volume":
                    float volume = 1.0f;
                    if (paramObj != null)
                    {
                        if (float.TryParse(paramObj.ToString(), out float val))
                        {
                            volume = val;
                        }
                    }
                    TriggerSetVolume(volume);
                    break;

                case "play_npc_script":
                    string script = "";
                    if (paramObj != null)
                    {
                        if (paramObj is Dictionary<string, object> dict)
                        {
                            string audioUrl = dict.ContainsKey("audio_url") ? dict["audio_url"]?.ToString() : "";
                            string text = dict.ContainsKey("text") ? dict["text"]?.ToString() : "";
                            script = $"{audioUrl}|||{text}";
                        }
                        else
                        {
                            script = paramObj.ToString();
                        }
                    }
                    TriggerPlayNpcScript(script);
                    break;

                default:
                    Debug.LogWarning($"[RemoteCommandListener] Lệnh không xác định: {commandType}");
                    break;
            }
        }

        // ── Public Dispatchers (Cầu nối kích hoạt sự kiện) ──
        public void TriggerVerbalHint()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Gợi ý Lời nói (OnTriggerVerbalHint)");
            OnTriggerVerbalHint?.Invoke();
        }

        public void TriggerVisualHint()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Gợi ý Thị giác (OnTriggerVisualHint)");
            OnTriggerVisualHint?.Invoke();
        }

        public void TriggerSetVolume(float volume)
        {
            Debug.Log($"[RemoteCommandListener] 📡 Đã nhận lệnh: Thay đổi âm lượng = {volume} (OnSetVolume)");
            OnSetVolume?.Invoke(volume);
        }

        public void TriggerPlayNpcScript(string scriptText)
        {
            Debug.Log($"[RemoteCommandListener] 📡 Đã nhận lệnh: Phát hội thoại = '{scriptText}' (OnPlayNpcScript)");
            OnPlayNpcScript?.Invoke(scriptText);
        }

        public void TriggerSkipQuest()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Bỏ qua Quest (OnSkipQuest)");
            OnSkipQuest?.Invoke();
        }

        public void TriggerPauseLesson()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Tạm dừng bài học (OnPauseLesson)");
            OnPauseLesson?.Invoke();
        }

        public void TriggerResumeLesson()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Tiếp tục bài học (OnResumeLesson)");
            OnResumeLesson?.Invoke();
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Phím H = Visual Hint | V = Verbal Hint | S = Skip Quest | P = Pause | R = Resume
            if (Input.GetKeyDown(KeyCode.H)) TriggerVisualHint();
            if (Input.GetKeyDown(KeyCode.V)) TriggerVerbalHint();
            if (Input.GetKeyDown(KeyCode.C)) TriggerSkipQuest();
            if (Input.GetKeyDown(KeyCode.P)) TriggerPauseLesson();
            if (Input.GetKeyDown(KeyCode.R)) TriggerResumeLesson();
#endif

            // Xử lý các lệnh từ background thread truyền về
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteCommandListener] Lỗi khi thực thi lệnh trên Main Thread: {ex.Message}");
                }
            }
        }
    }
}
