using System;
using System.Collections;
using VRAutism.Core;
using UnityEngine;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Entities
{
    public class NPCVoice : MonoBehaviour
    {
        [SerializeField] private AudioSource[] npcs;
        [SerializeField] private IntVariable hintCount;

        private AudioSource npcAudioSource;

        private void Start()
        {
            // Initialize default NPC if not set
            if (npcAudioSource == null && npcs != null && npcs.Length > 0)
            {
                npcAudioSource = npcs[0];
            }
        }

        public void SetNpc(int id)
        {
            if (npcs != null && id >= 0 && id < npcs.Length)
            {
                npcAudioSource = npcs[id];
            }
        }

        public void PlayClip(AudioClip clip)
        {
            if (npcAudioSource == null) return;
            npcAudioSource.clip = clip;
            npcAudioSource.Play();
        }

        // public void PlayRandomReminder(int questionId)
        // {
            
        // }

        public Coroutine PlayClipWithFadeIn(AudioClip clip)
        {
            if (npcAudioSource == null) return null;
            return StartCoroutine(FadeInAndPlay(clip));
        }

        private IEnumerator FadeInAndPlay(AudioClip clip)
        {
            npcAudioSource.clip = clip;
            npcAudioSource.volume = 0f;
            npcAudioSource.Play();

            float maxVolume = 0.5f;
            if (SessionContext.Instance != null)
            {
                maxVolume = SessionContext.Instance.MaxVolume;
            }

            float duration = 0.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                npcAudioSource.volume = Mathf.Lerp(0f, maxVolume, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            npcAudioSource.volume = maxVolume;
        }
    }
}

