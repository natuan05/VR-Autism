using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCController : MonoBehaviour
    {
        [SerializeField] private NPCVoice voicePlayer;
        [SerializeField] private SpeechBubblePresenter bubblePresenter;
        [SerializeField] private NPCLookAtPlayer lookAtPlayer;

        private void Start()
        {
            if (voicePlayer == null) voicePlayer = GetComponent<NPCVoice>();
            if (bubblePresenter == null) bubblePresenter = GetComponent<SpeechBubblePresenter>();
            if (lookAtPlayer == null) lookAtPlayer = GetComponent<NPCLookAtPlayer>();

        }

        // public void SetNpc(int id)
        // {
        //     if (voicePlayer != null)
        //     {
        //         voicePlayer.SetNpc(id);
        //     }
        // }

        // public void SayRandomReminder(int id)
        // {
        //     if (voicePlayer != null)
        //     {
        //         voicePlayer.PlayRandomReminder(id);
        //     }
        // }

        public void PlayRemoteVoice(AudioClip clip, string subtitle)
        {
            if (lookAtPlayer != null)
            {
                lookAtPlayer.LookAtPlayerForDuration(3.0f);
            }

            if (voicePlayer != null)
            {
                voicePlayer.PlayClipWithFadeIn(clip);
            }

            if (bubblePresenter != null)
            {
                float duration = Mathf.Max(3.0f, clip.length + 0.5f);
                bubblePresenter.Show(subtitle, duration);
            }
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
