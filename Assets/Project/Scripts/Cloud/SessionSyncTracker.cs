using UnityEngine;
using VRAutism.Core;
using VRAutism.Quests;

namespace VRAutism.Cloud
{
    public class SessionSyncTracker : MonoBehaviour
    {
        private void Start()
        {
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
            string sessionId = SessionContext.Instance != null ? SessionContext.Instance.SessionId : "";

            if (!string.IsNullOrEmpty(sessionId) && RealtimeDBManager.Instance != null)
            {
                RealtimeDBManager.Instance.UpdateCurrentActivity(sessionId, activityName);
            }
        }
    }
}
