using System;
using System.Linq;
using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Quests
{
    public class QuestController: MonoBehaviour
    {
        [SerializeField] private Quest[] quests;
        [SerializeField] private QuestProgressUI questProgressUI;
        [SerializeField] private GameObject bubbleQuestion;
        [SerializeField] private GameObject congratulationUI;
        [SerializeField] private BooleanVariable isConditionMet;


        private int curQuestId;
        private string[] questNames;

        private void Awake()
        {
            foreach (var quest in quests)
            {
                quest.Init();
                quest.RequestShowBubble += ShowBubble;
                quest.RequestShowProgressBar += ShowProgressBar;
                quest.RequestSetProgress += SetProgress;
                quest.OnQuestCompleted += OnCompleteQuest;
            }

            questNames = quests.Where(q => q.IsSendData).Select(q => q.Name).ToArray();

            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
            if (congratulationUI != null) congratulationUI.SetActive(false);

            curQuestId = 0;
        }
        
        public void ShowBubble(bool show, Vector3 position)
        {
            if (bubbleQuestion != null)
            {
                bubbleQuestion.SetActive(show);
                if (show) bubbleQuestion.transform.position = position;
            }
        }

        public void ShowProgressBar(bool show, Vector3 position)
        {
            if (questProgressUI != null)
            {
                questProgressUI.gameObject.SetActive(show);
                if (show) questProgressUI.transform.position = position;
            }
        }

        public void SetProgress(float value)
        {
            if (questProgressUI != null) questProgressUI.SetProgress(value);
        }

        public string[] GetAllQuestNames()
        {
            return questNames;
        }

        private Quest GetCurQuest()
        {
            if (curQuestId >= 0 && curQuestId < quests.Length)
                return quests[curQuestId];
            return null;
        }

        public void StartRunningQuest()
        {
            isConditionMet.Value = false;
            TimeManager.Instance?.StartLessonTime(); // Bấm giờ từ lúc trẻ bắt đầu làm bài
            StartNewQuest();
        }

        public void OnCompleteQuest()
        {
            if (TimeManager.Instance)
            {
                TimeManager.Instance.LogQuestComplete(
                    questIndex:       curQuestId,
                    questName:        GetCurQuest()?.Name ?? "",
                    completionStatus: "success"
                );
            }
            
            if (curQuestId >= quests.Length - 1)
            {
                if (congratulationUI != null) congratulationUI.SetActive(true);
                this.SendEvent(EventID.ExitScene);
                isConditionMet.Value = true;
                return;
            }
            
            curQuestId++;
            StartNewQuest();
        }


        private void StartNewQuest()
        {
            TimeManager.Instance?.StartQuestTime();   // stamp _questStartSecond before quest begins

            var quest = GetCurQuest();
            
            if (quest is null)
            {
                Debug.LogError($"Quest {curQuestId} not found in total {quests.Length} quests");
            }
            else
            {
                quest.SetState(Quest.State.Enable);
            }
        }

        private void OnDestroy()
        {
            foreach (var quest in quests)
            {
                if (quest == null) continue;
                quest.RequestShowBubble -= ShowBubble;
                quest.RequestShowProgressBar -= ShowProgressBar;
                quest.RequestSetProgress -= SetProgress;
                quest.OnQuestCompleted -= OnCompleteQuest;
            }
        }
    }
}