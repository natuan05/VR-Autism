using System;
using VRAutism.Core;

namespace VRAutism.Gameplay.Actions
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