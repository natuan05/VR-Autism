
using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Entities
{
    public class NPCController : MonoBehaviour
    {
        [SerializeField] private AudioSource[] npcs;
        [SerializeField] private AudioClip[] audioClips;
        [SerializeField] private ReminderData[] reminders;
        [SerializeField] private IntVariable hintCount;
        
        [SerializeField] private SpeechResponser[] speechResponser;

        private AudioSource myNPC;

        public void SetNpc(int id)
        {
            myNPC = npcs[id];
        }

        private void Start()
        {
            foreach (var responser in speechResponser)
            {
                responser.OnPrompt += SayAudio;
            }
        }
        
        public void SaySomething(int id)
        {
            myNPC.clip = audioClips[id];
            myNPC.Play();
           // myNPC.PlayOneShot(audioClips[id]);
        }

        public void SayAudio(AudioClip clip)
        {
            myNPC.clip = clip;
            myNPC.Play();
        }

        public void SayRandomReminder(int id)
        {
            hintCount.Value++;
            myNPC.clip = reminders[id].audioClips[Random.Range(0, reminders[id].audioClips.Length)];
            myNPC.Play();
        }
    } 
}

