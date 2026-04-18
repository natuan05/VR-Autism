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

        // TODO: Khai báo Events cho từng loại lệnh
        // public static event System.Action OnTriggerHint;
        // public static event System.Action<float> OnSetVolume;
        // public static event System.Action<string> OnPlayNpcScript;
        // public static event System.Action OnSkipQuest;
        // public static event System.Action OnPauseLesson;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // TODO: StartListening(string sessionId)   → Bật listener trên remote_commands/{sessionId}
        // TODO: StopListening()                    → Tắt listener khi bài học kết thúc
        // TODO: HandleRemoteCommand(...)           → Đọc lệnh từ Snapshot và dispatch Event
    }
}
