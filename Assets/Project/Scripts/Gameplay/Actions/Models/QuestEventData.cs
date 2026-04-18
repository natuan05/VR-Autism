using System;
using VRAutism.Core;

namespace VRAutism.Quests
{
    public class QuestEventData: BaseSO
    {
        public Action<Quest> OnQuestCompleted;

        public void OnCompleteQuest(Quest quest)
        {
            OnQuestCompleted?.Invoke(quest);
        }
    }
}