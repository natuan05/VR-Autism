using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCController : MonoBehaviour
    {
        [SerializeField] private SpeechBubblePresenter bubblePresenter;
        [SerializeField] private NPCLookAtPlayer lookAtPlayer;

        private void Start()
        {
            if (bubblePresenter == null) bubblePresenter = GetComponent<SpeechBubblePresenter>();
            if (lookAtPlayer == null) lookAtPlayer = GetComponent<NPCLookAtPlayer>();

        }

        public void PlayRemoteText(string text)
        {
            if (lookAtPlayer != null)
            {
                lookAtPlayer.LookAtPlayerForDuration(3.0f);
            }

            if (bubblePresenter != null)
            {
                bubblePresenter.Show(text, 5.0f);
            }
        }
    }
}
