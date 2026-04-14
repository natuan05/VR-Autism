using UnityEngine;
using VRAutism.Core;
using VRAutism.Quests;

namespace VRAutism.Cloud
{
    /// <summary>
    /// Trạm thu phát sóng (Antenna). Chuyên lắng nghe lệnh từ Gameplay
    /// và tương tác lại với Cloud (RealtimeDB). Đã chuyển sang C# Action tiêu chuẩn (Decoupled & Type-safe).
    /// </summary>
    public class SessionSyncTracker : MonoBehaviour
    {
        private void Start()
        {
            // Bật ăng-ten để nghe tiếng hô (C# Event tiêu chuẩn, không dùng God Script EventChannel)
            QuestController.OnQuestActivityChanged += HandleActivityChanged;
            QuizController.OnQuizActivityChanged   += HandleActivityChanged;
        }

        private void OnDestroy()
        {
            QuestController.OnQuestActivityChanged -= HandleActivityChanged;
            QuizController.OnQuizActivityChanged   -= HandleActivityChanged;
        }

        private void HandleActivityChanged(string activityName)
        {
            // Lấy Session ID hiện tại
            string sessionId = SessionContext.Instance != null ? SessionContext.Instance.SessionId : "";

            // Nhờ RealtimeDBManager xách hộ valy Data lên Cloud
            if (!string.IsNullOrEmpty(sessionId) && RealtimeDBManager.Instance != null)
            {
                RealtimeDBManager.Instance.UpdateCurrentActivity(sessionId, activityName);
            }
        }
    }
}
