using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class TouchQuest : Quest
    {
        public override void OnStartInteraction(IQuestFlowController controller)
        {
            RaiseUIStarted();
            // Vừa chạm vào là hoàn thành ngay lập tức
            controller.CompleteActiveQuest();
            RaiseUIFinished();
        }
    }
}
