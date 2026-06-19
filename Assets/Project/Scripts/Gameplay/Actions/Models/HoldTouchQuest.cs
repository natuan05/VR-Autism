using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class HoldTouchQuest : Quest
    {
        private float _progress = 0f;

        public override void OnStartInteraction(QuestController controller)
        {
            _progress = 0f;
            RaiseStarted();
            RaiseProgressChanged(0f);
        }

        public override void OnCancelInteraction(QuestController controller)
        {
            _progress = 0f;
            RaiseFinished();
        }

        public override void OnUpdateInteraction(QuestController controller)
        {
            _progress += Time.deltaTime / Duration;
            RaiseProgressChanged(_progress);

            if (_progress >= 1f)
            {
                _progress = 1f;
                controller.CompleteActiveQuest();
                RaiseFinished();
            }
        }
    }
}
