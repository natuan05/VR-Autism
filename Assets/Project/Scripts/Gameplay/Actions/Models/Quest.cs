using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Plugins.QuickOutline.Scripts;

namespace VRAutism.Gameplay.Actions
{
    public abstract class Quest : MonoBehaviour
    {
        [Header("Setup quest")] 
        [SerializeField] private int id;
        [SerializeField] private string questName;
        [SerializeField] private float duration;
        [SerializeField] private bool isSendData;

        [Header("Components")] 
        [SerializeField] private Outline outline;
        [SerializeField] private Transform posBubbleQuestion;
        [SerializeField] private Transform posProgressBar;
        
        [Header("Events")]
        [SerializeField] private UnityEvent onQuestStarted;
        [SerializeField] private UnityEvent onQuestFinished;
        [SerializeField] private UnityEvent onTriggerEnter;
        [SerializeField] private UnityEvent onTriggerExit;
        
        [Header("Reminder")] 
        [SerializeField] private float reminderCycle;
        [SerializeField] private UnityEvent onQuestReminder;

        // Getters for Controller to read
        public int Id => id;
        public string Name => questName;
        public float Duration => duration;
        public bool IsSendData => isSendData;
        public float ReminderCycle => reminderCycle;
        
        public Vector3 BubblePosition => posBubbleQuestion != null ? posBubbleQuestion.position : Vector3.zero;
        public Vector3 ProgressBarPosition => posProgressBar != null ? posProgressBar.position : Vector3.zero;

        // Event hooks for physics collisions
        public event Action<Quest> CharacterCanEnter;
        public event Action<Quest> CharacterExit;

        // UI events
        public event Action<Quest> OnStarted;
        public event Action<Quest, float> OnProgressChanged;
        public event Action<Quest> OnFinished;

        // Helper methods for subclasses to raise UI events
        protected void RaiseStarted() => OnStarted?.Invoke(this);
        protected void RaiseProgressChanged(float progress) => OnProgressChanged?.Invoke(this, progress);
        protected void RaiseFinished() => OnFinished?.Invoke(this);

        private Coroutine _hintBlinkCoroutine;

        // UnityEvents helper triggers
        public void AllowCharacterEnter() => onTriggerEnter?.Invoke();
        public void AllowCharacterExit() => onTriggerExit?.Invoke();
        public void AllowReminderEvent() => onQuestReminder?.Invoke();

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
                onTriggerEnter?.Invoke(); // For UnityEvents
                CharacterCanEnter?.Invoke(this); // For C# events
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Character"))
            {
                onTriggerExit?.Invoke();
                CharacterExit?.Invoke(this);
            }
        }

        // Virtual lifecycle methods for subclasses
        public virtual void OnQuestActive(QuestController controller)
        {
            RaiseStarted();
        }
        public virtual void OnStartInteraction(QuestController controller) {}
        public virtual void OnCancelInteraction(QuestController controller) {}
        public virtual void OnUpdateInteraction(QuestController controller) {}
    }
}
