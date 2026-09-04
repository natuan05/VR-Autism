using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    /// <summary>Completes its active quest when an allowed interactor enters this trigger.</summary>
    [DisallowMultipleComponent]
    public class TouchQuestSourceV2 : QuestSourceV2
    {
        [SerializeField] private LayerMask _interactorLayers;

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || State != QuestSourceState.Active || !IsAllowed(other)) return;
            if (TryComplete(CurrentActivationId, "touch"))
            {
                Debug.Log($"[LessonGraphV2] TOUCH accepted binding='{BindingId}' collider={other.gameObject.name} layer={other.gameObject.layer}", this);
            }
        }

        protected bool IsAllowed(Collider collider) =>
            collider != null && (_interactorLayers.value & (1 << collider.gameObject.layer)) != 0;
    }
}
