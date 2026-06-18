using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Plugins.QuickOutline.Scripts;

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
        [SerializeField] private UnityEvent onQuestTriggerEnter;
        [SerializeField] private UnityEvent onQuestTriggerExit;
        
        [Header("Reminder")] 
        [SerializeField] private float reminderCycle;
        [SerializeField] private UnityEvent onQuestReminder;

        // Getters for Controller to read
        public int Id => id;
        public string Name => questName;
        public QuestType Type => questType;
        public float Duration => duration;
        public bool IsSendData => isSendData;
        public float ReminderCycle => reminderCycle;
        
        public Vector3 BubblePosition => posBubbleQuestion != null ? posBubbleQuestion.position : Vector3.zero;
        public Vector3 ProgressBarPosition => posProgressBar != null ? posProgressBar.position : Vector3.zero;

        // Event hooks for physics collisions
        public event Action<Quest> OnCharacterEnter;
        public event Action<Quest> OnCharacterExit;

        private Coroutine _hintBlinkCoroutine;

        // UnityEvents helper triggers
        public void TriggerStartedEvent() => onQuestStarted?.Invoke();
        public void TriggerFinishedEvent() => onQuestFinished?.Invoke();
        public void TriggerReminderEvent() => onQuestReminder?.Invoke();

        public void Init()
        {
            if (outline) outline.enabled = false;
        }

        // View logic: Toggle Outline
        public void SetOutline(bool enable)
        {
            if (outline) outline.enabled = enable;
            
            if (!enable && _hintBlinkCoroutine != null)
            {
                StopCoroutine(_hintBlinkCoroutine);
                _hintBlinkCoroutine = null;
            }
        }

        // View logic: Blink Hint Outline
        public void BlinkHintOutline(bool restoreVisualGuidance)
        {
            if (outline)
            {
                if (_hintBlinkCoroutine != null) StopCoroutine(_hintBlinkCoroutine);
                _hintBlinkCoroutine = StartCoroutine(BlinkOutlineHintRoutine(restoreVisualGuidance));
            }
        }

        private IEnumerator BlinkOutlineHintRoutine(bool restoreVisualGuidance)
        {
            for (int i = 0; i < 3; i++)
            {
                outline.enabled = true;
                yield return new WaitForSeconds(0.3f);
                outline.enabled = false;
                yield return new WaitForSeconds(0.3f);
            }
            
            SetOutline(restoreVisualGuidance);
            _hintBlinkCoroutine = null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Character"))
            {
                onQuestTriggerEnter?.Invoke();
                OnCharacterEnter?.Invoke(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Character"))
            {
                onQuestTriggerExit?.Invoke();
                OnCharacterExit?.Invoke(this);
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
