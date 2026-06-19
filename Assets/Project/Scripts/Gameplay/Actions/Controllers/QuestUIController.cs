using UnityEngine;
using VRAutism.Core;
using VRAutism.Core.Models;

namespace VRAutism.Gameplay.Actions
{
    public class QuestUIController : MonoBehaviour
    {
        [SerializeField] private QuestController questController;
        [SerializeField] private QuestProgressUI questProgressUI;
        [SerializeField] private GameObject bubbleQuestion;
        [SerializeField] private GameObject congratulationUI;

        private LessonParameters _activeParams;

        private void OnEnable()
        {
            if (questController != null)
            {
                questController.OnAllQuestsCompleted += HandleAllQuestsCompleted;
                SubscribeQuests();
            }
        }

        private void OnDisable()
        {
            if (questController != null)
            {
                questController.OnAllQuestsCompleted -= HandleAllQuestsCompleted;
                UnsubscribeQuests();
            }
        }

        private void Start()
        {
            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
            if (congratulationUI != null) congratulationUI.SetActive(false);
            
            _activeParams = SessionContext.Instance != null 
                ? SessionContext.Instance.CurrentParams 
                : LessonParameters.Default;
        }

        private void SubscribeQuests()
        {
            if (questController == null || questController.Quests == null) return;
            foreach (var quest in questController.Quests)
            {
                if (quest == null) continue;
                quest.OnStarted += HandleQuestStarted;
                quest.OnProgressChanged += HandleQuestProgressChanged;
                quest.OnFinished += HandleQuestFinished;
            }
        }

        private void UnsubscribeQuests()
        {
            if (questController == null || questController.Quests == null) return;
            foreach (var quest in questController.Quests)
            {
                if (quest == null) continue;
                quest.OnStarted -= HandleQuestStarted;
                quest.OnProgressChanged -= HandleQuestProgressChanged;
                quest.OnFinished -= HandleQuestFinished;
            }
        }

        private void HandleQuestStarted(Quest quest)
        {
            if (bubbleQuestion != null)
            {
                bubbleQuestion.SetActive(_activeParams.Actions.EnableBubbleHints);
                bubbleQuestion.transform.position = quest.BubblePosition;
            }
        }

        private void HandleQuestProgressChanged(Quest quest, float progress)
        {
            if (questProgressUI != null)
            {
                if (!questProgressUI.gameObject.activeSelf)
                {
                    questProgressUI.gameObject.SetActive(true);
                }
                questProgressUI.transform.position = quest.ProgressBarPosition;
                questProgressUI.SetProgress(progress);
            }
        }

        private void HandleQuestFinished(Quest quest)
        {
            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
        }

        private void HandleAllQuestsCompleted()
        {
            if (congratulationUI != null) congratulationUI.SetActive(true);
            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
        }
    }
}
