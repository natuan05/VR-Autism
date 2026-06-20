using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class HoldTouchQuest : Quest
    {
        private float _progress = 0f;

        public override void OnStartInteraction(QuestController controller)
        {
            _progress = 0f;
            RaiseUIStarted();
            RaiseUIProgressChanged(0f);
        }

        public override void OnCancelInteraction(QuestController controller)
        {
            _progress = 0f;
            RaiseUIFinished();
        }

        public override void OnUpdateInteraction(QuestController controller)
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
