using System;
using KBCore.Refs;
using Plugins.QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.Events;

namespace VRAutism.Quests
{
    public class Quest : MonoBehaviour
    {
        [Header("Setup quest")] 
        [SerializeField] private int id;
        [SerializeField] private string questName;
        [SerializeField] private QuestType questType;
        [SerializeField] private float duration;
        [SerializeField] private bool isSendData;

        [Header("Components")] 
        [SerializeField] private Outline outline;
        [SerializeField] private Transform posBubbleQuestion;
        [SerializeField] private Transform posProgressBar;
        
        [Header("Events")]
        [SerializeField] private UnityEvent onQuestStarted;
        [SerializeField] private UnityEvent onQuestFinished;
        [SerializeField] private UnityEvent onQuestCanceled;
        [SerializeField] private UnityEvent onQuestTriggerEnter;
        [SerializeField] private UnityEvent onQuestTriggerExit;
        
        [Header("Reminder")] 
        [SerializeField] private float reminderCycle;
        [SerializeField] private UnityEvent onQuestReminder;

        
        public int Id => id;
        public string Name => questName;
        public bool IsSendData => isSendData;
        
        // Observer Pattern Actions
        public Action<bool, Vector3> RequestShowBubble;
        public Action<bool, Vector3> RequestShowProgressBar;
        public Action<float> RequestSetProgress;
        public Action OnQuestCompleted;
        
        private State state;
        private float progress;

        private float timeReminder;
        
        public enum State
        {
            Disable,
            Enable,
            Start,
            Completed
        }
        
        public void Init()
        {
            state = State.Disable;
            if (outline) outline.enabled = false;
        }

        public void SetState(State newState)
        {
            state = newState;

            RequestShowBubble?.Invoke(state == State.Enable, posBubbleQuestion.position);
            RequestShowProgressBar?.Invoke(state == State.Start, posProgressBar.position);

            if (outline) outline.enabled = newState == State.Start;

            if (state == State.Start)
            {
                progress = 0;
                onQuestStarted?.Invoke();
            }
            if (state == State.Completed)
            {
                onQuestFinished?.Invoke();
                OnQuestCompleted?.Invoke();
            }

            if (state == State.Enable)
            {
                timeReminder = reminderCycle;
            }
        }
        

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Character") || state == State.Disable) return;
            onQuestTriggerEnter?.Invoke();

            if (state == State.Enable)
            {
                if (questType == QuestType.Condition)
                {
                    
                }
                
                if (questType == QuestType.Touch)
                {
                    SetState(State.Completed);
                }

                if (questType == QuestType.HoldTouch)
                {
                    SetState(State.Start);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Character") || state == State.Disable) return;
            onQuestTriggerExit?.Invoke();

            if (state != State.Completed)
            {
                SetState(State.Enable);
            }
        }

        private void Update()
        {
            if (state == State.Enable && reminderCycle > 0)
            {
                timeReminder -= Time.deltaTime;
                if (timeReminder < 0)
                {
                    timeReminder = reminderCycle;
                    onQuestReminder?.Invoke();
                }
            }
            
            if (state != State.Start) return;
            
            progress += Time.deltaTime / duration;
            RequestSetProgress?.Invoke(progress);
            if (progress >= 1)
            {
                progress = 1;
                SetState(State.Completed);
            }
            
        }
    }

    public enum QuestType
    {
        Click,
        Touch,
        HoldClick,
        HoldTouch,
        Condition
    }
}


