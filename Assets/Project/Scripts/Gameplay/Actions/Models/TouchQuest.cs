using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class TouchQuest : Quest
    {
        public override void OnStartInteraction(QuestController controller)
        {
            RaiseStarted();
            // Vừa chạm vào là hoàn thành ngay lập tức
            controller.CompleteActiveQuest();
            RaiseFinished();
        }
    }
}
