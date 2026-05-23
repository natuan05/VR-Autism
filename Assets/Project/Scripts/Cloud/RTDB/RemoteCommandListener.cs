using UnityEngine;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// [PLACEHOLDER] Lắng nghe lệnh điều khiển từ Web Dashboard.
    ///
    /// Sẽ lắng nghe nhánh RTDB: remote_commands/{sessionId}
    /// và dispatch ra C# Events để các hệ thống Gameplay xử lý.
    ///
    /// Các lệnh dự kiến (WEB_DASHBOARD_IDEAS.md §4):
    ///   - trigger_hint      → NPC phát gợi ý bằng âm thanh & highlight
    ///   - set_volume        → Thay đổi âm lượng môi trường
    ///   - play_npc_script   → Text-to-Speech gửi từ Web
    ///   - skip_quest        → Bỏ qua Quest hiện tại
    ///   - pause_lesson      → Fade-to-black khẩn cấp
    ///
    /// TODO: Triển khai khi bắt đầu phát triển tính năng Remote Control.
    /// </summary>
    public class RemoteCommandListener : MonoBehaviour
    {
        public static RemoteCommandListener Instance { get; private set; }

        // ── C# Events cho các lệnh điều khiển từ xa (Story 3.1) ──
        public static event System.Action OnTriggerHint;
        public static event System.Action<float> OnSetVolume;
        public static event System.Action<string> OnPlayNpcScript;
        public static event System.Action OnSkipQuest;
        public static event System.Action OnPauseLesson;
        public static event System.Action OnResumeLesson;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ── Public Dispatchers (Cầu nối kích hoạt sự kiện) ──
        public void TriggerHint()
        {
            Debug.Log("[RemoteCommandListener] 📡 Đã nhận lệnh: Gợi ý (OnTriggerHint)");
            OnTriggerHint?.Invoke();
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
            // Debug keybinds for simulating remote commands during Phase 4 verification
            if (Input.GetKeyDown(KeyCode.H))
            {
                TriggerHint();
            }
            if (Input.GetKeyDown(KeyCode.S))
            {
                TriggerSkipQuest();
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                TriggerPauseLesson();
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                TriggerResumeLesson();
            }
        }

        // TODO: StartListening(string sessionId)   → Bật listener trên remote_commands/{sessionId} (Story 3.4)
        // TODO: StopListening()                    → Tắt listener khi bài học kết thúc (Story 3.4)
        // TODO: HandleRemoteCommand(...)           → Đọc lệnh từ Snapshot và dispatch Event (Story 3.4)
    }
}
