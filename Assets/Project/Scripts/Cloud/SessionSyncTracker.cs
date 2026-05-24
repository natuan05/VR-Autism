using UnityEngine;
using VRAutism.Core;
using VRAutism.Gameplay.Actions;
using VRAutism.Gameplay.Quizzes;

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

            if (!string.IsNullOrEmpty(sessionId) && Cloud.RTDB.LiveSessionReporter.Instance != null)
            {
                Cloud.RTDB.LiveSessionReporter.Instance.UpdateCurrentActivity(sessionId, activityName);
            }
        }
    }
}
