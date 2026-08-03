using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public interface IQuestHasVisual
    {
        Vector3 BubblePosition { get; }
        Vector3 ProgressBarPosition { get; }
        void SetOutline(bool enable);
        void BlinkHintOutline(bool restoreVisualGuidance);
        void PlayHintSound(Vector3? customPosition = null);
    }
}
