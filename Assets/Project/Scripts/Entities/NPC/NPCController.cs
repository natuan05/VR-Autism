using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCController : MonoBehaviour
    {
        [SerializeField] private NPCVoicePlayer voicePlayer;
        [SerializeField] private SpeechBubblePresenter bubblePresenter;
        [SerializeField] private NPCLookAtPlayer lookAtPlayer;
        [SerializeField] private SpeechResponser[] speechResponser;

        private void Start()
        {
            if (voicePlayer == null) voicePlayer = GetComponent<NPCVoicePlayer>();
            if (bubblePresenter == null) bubblePresenter = GetComponent<SpeechBubblePresenter>();
            if (lookAtPlayer == null) lookAtPlayer = GetComponent<NPCLookAtPlayer>();

            foreach (var responser in speechResponser)
            {
                if (responser != null)
                {
                    responser.OnPrompt += SayAudio;
                }
            }
        }

        public void SetNpc(int id)
        {
            if (voicePlayer != null)
            {
                voicePlayer.SetNpc(id);
            }
        }

        public void SaySomething(int id)
        {
            if (voicePlayer != null)
            {
                voicePlayer.PlayClipById(id);
            }
        }

        public void SayAudio(AudioClip clip)
        {
            if (voicePlayer != null)
            {
                voicePlayer.PlayClip(clip);
            }
        }

        public void SayRandomReminder(int id)
        {
            if (voicePlayer != null)
            {
                voicePlayer.PlayRandomReminder(id);
            }
        }

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
