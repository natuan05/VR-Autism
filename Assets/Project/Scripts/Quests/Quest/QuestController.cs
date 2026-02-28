using System;
using System.Linq;
using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Quests
{
    public class QuestController: MonoBehaviour
    {
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private FirebaseManager firebaseManager;
        [SerializeField] private Quest[] quests;
        [SerializeField] public QuestProgressUI questProgressUI;
        [SerializeField] public GameObject bubbleQuestion;
        [SerializeField] public GameObject congratulationUI;
        [SerializeField] private BooleanVariable isConditionMet;
        [SerializeField] private DoubleVariable timeVariable;


        private int curQuestId;
        private string[] questNames;

        private void Awake()
        {
            foreach (var quest in quests)
            {
                quest.Init(this);
            }

            questNames = quests.Where(q => q.IsSendData).Select(q => q.Name).ToArray();

            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
            if (congratulationUI != null) congratulationUI.SetActive(false);

            curQuestId = 0;
        }
        
        public string[] GetAllQuestNames()
        {
            return questNames;
        }

        private Quest GetCurQuest()
        {
            return quests.FirstOrDefault(x => x.Id == curQuestId);
        }

        public void StartRunningQuest()
        {
            isConditionMet.Value = false;
            StartNewQuest();
        }

        public void OnCompleteQuest()
        {
            if (timeManager)
            {
                var finishedTime = TimeUtils.CurrentSecond - timeVariable.Value;

                firebaseManager.UpdateQuestData("response_time", finishedTime, curQuestId);
                
                timeManager.AddQuestTime(
                    new QuestTimeData
                    {
                        index = curQuestId,
                        quest_name = GetCurQuest().Name,
                        response_time = finishedTime,
                    });
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
            timeVariable.Value = TimeUtils.CurrentSecond;
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
        
    }
}