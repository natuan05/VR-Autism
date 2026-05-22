using System;
using KBCore.Refs;
using Plugins.QuickOutline.Scripts;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;
using UnityEngine.Events;

namespace VRAutism.Gameplay.Actions
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

            // Đọc tham số tùy chỉnh từ SessionContext nếu có, fallback về default.
            LessonParameters p = SessionContext.Instance != null
                ? SessionContext.Instance.CurrentParams
                : LessonParameters.Default;  // Dùng singleton để tránh GC allocation

            Vector3 bubblePos  = posBubbleQuestion != null ? posBubbleQuestion.position : Vector3.zero;
            Vector3 progressPos = posProgressBar   != null ? posProgressBar.position   : Vector3.zero;

            // --- Bubble: chỉ hiện ở State.Enable khi EnableBubbleHints = true ---
            bool showBubble = (state == State.Enable) && p.EnableBubbleHints;
            RequestShowBubble?.Invoke(showBubble, bubblePos);

            // --- Progress bar: chỉ hiện khi đang HoldTouch (State.Start) ---
            RequestShowProgressBar?.Invoke(state == State.Start, progressPos);

            // --- Outline: bật từ Enable, tắt khi Completed/Disable ---
            if (outline)
            {
                bool showOutline = (state == State.Enable || state == State.Start) && p.EnableVisualGuidance;
                outline.enabled = showOutline;
            }

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

            // --- Reminder: ghi đè reminderCycle từ params nếu ActionReminderCycle >= 0 (non-sentinel) ---
            if (state == State.Enable)
            {
                float overrideCycle = p.ActionReminderCycle;
                // Sentinel -1f = không ghi đè; chỉ ghi đè khi giá trị params >= 0f hợp lệ
                timeReminder = overrideCycle >= 0f ? overrideCycle : reminderCycle;
            }
        }
        

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Character") || state == State.Disable) return;
            onQuestTriggerEnter?.Invoke();

            if (state == State.Enable)
            {
                switch (questType)
                {
                    case QuestType.Touch:
                        SetState(State.Completed);
                        break;
                    case QuestType.HoldTouch:
                        SetState(State.Start);
                        break;
                    // QuestType.Condition ignored (handled via WaitUntil elsewhere)
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
            // Reminder: sử dụng timeReminder đã được set trong SetState (từ params hoặc Inspector).
            // Dùng effectiveCycle để gate — không dùng reminderCycle cứng vì nếu Inspector = 0
            // nhưng params override > 0 thì vòng lặp này sẽ bị chặn sai (dead code).
            LessonParameters pUpdate = SessionContext.Instance != null
                ? SessionContext.Instance.CurrentParams
                : LessonParameters.Default;  // Dùng singleton để tránh GC allocation
            float overrideCycleUpdate = pUpdate.ActionReminderCycle;
            float effectiveCycle = overrideCycleUpdate >= 0f ? overrideCycleUpdate : reminderCycle;

            if (state == State.Enable && effectiveCycle > 0f)
            {
                timeReminder -= Time.deltaTime;
                if (timeReminder < 0)
                {
                    timeReminder = effectiveCycle;
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


