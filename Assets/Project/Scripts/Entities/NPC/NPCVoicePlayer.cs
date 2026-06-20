using System;
using System.Collections;
using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCVoicePlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource[] npcs;
        [SerializeField] private AudioClip[] audioClips;
        [SerializeField] private ReminderData[] reminders;
        [SerializeField] private IntVariable hintCount;

        private AudioSource myNPC;

        private void Start()
        {
            // Initialize default NPC if not set
            if (myNPC == null && npcs != null && npcs.Length > 0)
            {
                myNPC = npcs[0];
            }
        }

        public void SetNpc(int id)
        {
            if (npcs != null && id >= 0 && id < npcs.Length)
            {
                myNPC = npcs[id];
            }
        }

        public void PlayClip(AudioClip clip)
        {
            if (myNPC == null) return;
            myNPC.clip = clip;
            myNPC.Play();
        }

        public void PlayClipById(int id)
        {
            if (myNPC == null || audioClips == null || id < 0 || id >= audioClips.Length) return;
            myNPC.clip = audioClips[id];
            myNPC.Play();
        }

        public void PlayRandomReminder(int id)
        {
            if (myNPC == null || reminders == null || id < 0 || id >= reminders.Length) return;
            hintCount.Value++;
            var reminder = reminders[id];
            if (reminder != null && reminder.audioClips != null && reminder.audioClips.Length > 0)
            {
                myNPC.clip = reminder.audioClips[UnityEngine.Random.Range(0, reminder.audioClips.Length)];
                myNPC.Play();
            }
        }

        public Coroutine PlayClipWithFadeIn(AudioClip clip)
        {
            if (myNPC == null) return null;
            return StartCoroutine(FadeInAndPlay(clip));
        }

        private IEnumerator FadeInAndPlay(AudioClip clip)
        {
            myNPC.clip = clip;
            myNPC.volume = 0f;
            myNPC.Play();

            float maxVolume = 0.5f;
            if (SessionContext.Instance != null)
            {
                maxVolume = SessionContext.Instance.MaxVolume;
            }

            float duration = 0.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                myNPC.volume = Mathf.Lerp(0f, maxVolume, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            myNPC.volume = maxVolume;
        }
    }
}
