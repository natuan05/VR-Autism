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
                if (quest is VisualQuest visual)
                {
                    visual.OnUIStarted += HandleQuestStarted;
                    visual.OnUIProgressChanged += HandleQuestProgressChanged;
                    visual.OnUIFinished += HandleQuestFinished;
                }
            }
        }

        private void UnsubscribeQuests()
        {
            if (questController == null || questController.Quests == null) return;
            foreach (var quest in questController.Quests)
            {
                if (quest is VisualQuest visual)
                {
                    visual.OnUIStarted -= HandleQuestStarted;
                    visual.OnUIProgressChanged -= HandleQuestProgressChanged;
                    visual.OnUIFinished -= HandleQuestFinished;
                }
            }
        }

        private void HandleQuestStarted(VisualQuest quest)
        {
            if (bubbleQuestion != null && quest is IQuestHasVisual visual)
            {
                bubbleQuestion.SetActive(_activeParams.Actions.EnableBubbleHints);
                bubbleQuestion.transform.position = visual.BubblePosition;
            }
        }

        private void HandleQuestProgressChanged(VisualQuest quest, float progress)
        {
            if (questProgressUI != null)
            {
                if (!questProgressUI.gameObject.activeSelf)
                {
                    questProgressUI.gameObject.SetActive(true);
                }
                if (quest is IQuestHasVisual visual)
                {
                    questProgressUI.transform.position = visual.ProgressBarPosition;
                }
                questProgressUI.SetProgress(progress);
            }
        }

        private void HandleQuestFinished(VisualQuest quest)
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
