using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Quest giữ chạm — tiến độ tăng dần theo thời gian, hoàn thành khi đạt 100%.
    /// </summary>
    public class HoldTouchQuest : VisualQuest
    {
        private float _progress;

        protected override void OnBegin()
        {
            base.OnBegin();
            RaiseUIStarted();
        }

        protected override void OnCharacterEnter()
        {
            _progress = 0f;
            RaiseUIStarted();
            RaiseUIProgressChanged(0f);
        }

        protected override void OnCharacterExit()
        {
            _progress = 0f;
            RaiseUIFinished();
        }

        public override void Tick()
        {
            if (!IsCharacterInside) return;

            _progress += Time.deltaTime / Duration;
            RaiseUIProgressChanged(_progress);

            if (_progress >= 1f)
            {
                _progress = 1f;
                Controller.CompleteActiveQuest();
                RaiseUIFinished();
            }
        }
    }
}
