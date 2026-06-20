using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class HoldTouchQuest : Quest
    {
        private float _progress = 0f;

        public override void OnStartInteraction(IQuestFlowController controller)
        {
            _progress = 0f;
            RaiseUIStarted();
            RaiseUIProgressChanged(0f);
        }

        public override void OnCancelInteraction(IQuestFlowController controller)
        {
            _progress = 0f;
            RaiseUIFinished();
        }

        public override void OnUpdateInteraction(IQuestFlowController controller)
        {
            _progress += Time.deltaTime / Duration;
            RaiseUIProgressChanged(_progress);

            if (_progress >= 1f)
            {
                _progress = 1f;
                controller.CompleteActiveQuest();
                RaiseUIFinished();
            }
        }
    }
}
