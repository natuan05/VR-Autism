using System;
using System.Collections;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Plugins.QuickOutline.Scripts;

namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Quest có visual 3D, âm thanh, trigger vật lý, và UI events.
    /// Tự quản lý trigger enter/exit — QuestController không cần biết.
    /// </summary>
    public abstract class VisualQuest : Quest, IQuestHasVisual
    {
        [Header("Visual & Audio")]
        [SerializeField] protected Outline outline;
        [SerializeField] protected Transform posBubbleQuestion;
        [SerializeField] protected Transform posProgressBar;
        [SerializeField] protected AudioClip hintSound;

        [Header("Unity Events")]
        [SerializeField] private UnityEvent onQuestStarted;
        [SerializeField] private UnityEvent onQuestFinished;
        [FormerlySerializedAs("onTriggerEnter")]
        [SerializeField] private UnityEvent onQuestTriggerEnter;
        [FormerlySerializedAs("onTriggerExit")]
        [SerializeField] private UnityEvent onQuestTriggerExit;
        [SerializeField] private UnityEvent onQuestReminder;

        // IQuestHasVisual
        public Vector3 BubblePosition => posBubbleQuestion != null ? posBubbleQuestion.position : Vector3.zero;
        public Vector3 ProgressBarPosition => posProgressBar != null ? posProgressBar.position : Vector3.zero;

        // UI Events (cho QuestUIController subscribe)
        public event Action<VisualQuest> OnUIStarted;
        public event Action<VisualQuest, float> OnUIProgressChanged;
        public event Action<VisualQuest> OnUIFinished;

        protected void RaiseUIStarted() => OnUIStarted?.Invoke(this);
        protected void RaiseUIProgressChanged(float progress) => OnUIProgressChanged?.Invoke(this, progress);
        protected void RaiseUIFinished() => OnUIFinished?.Invoke(this);

        // ── Trigger State ──────────────────────────────────────────────────
        private Collider _triggerCollider;
        private int _colliderCount;
        protected bool IsCharacterInside { get; private set; }
        private Coroutine _hintBlinkCoroutine;

        // ── Lifecycle ──────────────────────────────────────────────────────
        public override void Init()
        {
            if (outline) outline.enabled = false;
            _triggerCollider = GetComponent<Collider>();
            if (_triggerCollider != null) _triggerCollider.enabled = false;
        }

        protected override void OnBegin()
        {
            _colliderCount = 0;
            IsCharacterInside = false;
            if (_triggerCollider != null) _triggerCollider.enabled = true;
            SetOutline(SessionContext.Instance?.CurrentParams.Actions.EnableVisualGuidance ?? false);
        }

        protected override void OnEnd()
        {
            if (_triggerCollider != null) _triggerCollider.enabled = false;
            SetOutline(false);
            onQuestFinished?.Invoke();
        }

        // ── Hint Hooks ─────────────────────────────────────────────────────
        public override bool ShouldAutoHint => !IsCharacterInside;
        public override void OnVisualHint(bool enableVisualGuidance) => BlinkHintOutline(enableVisualGuidance);
        public override void OnVerbalHint() => onQuestReminder?.Invoke();

        // ── Trigger Handling (tự quản lý) ──────────────────────────────────
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Character")) return;

            _colliderCount++;
            if (_colliderCount > 1) return;

            IsCharacterInside = true;
            onQuestTriggerEnter?.Invoke();
            onQuestStarted?.Invoke();
            OnCharacterEnter();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Character")) return;

            _colliderCount--;
            if (_colliderCount > 0) return;
            if (_colliderCount < 0) _colliderCount = 0;

            IsCharacterInside = false;
            onQuestTriggerExit?.Invoke();
            OnCharacterExit();
        }

        /// <summary>Hook cho subclass xử lý khi nhân vật bước vào trigger zone.</summary>
        protected virtual void OnCharacterEnter() {}

        /// <summary>Hook cho subclass xử lý khi nhân vật bước ra khỏi trigger zone.</summary>
        protected virtual void OnCharacterExit() {}

        // ── Visual Helpers ─────────────────────────────────────────────────
        public void SetOutline(bool enable)
        {
            if (outline) outline.enabled = enable;

            if (!enable && _hintBlinkCoroutine != null)
            {
                StopCoroutine(_hintBlinkCoroutine);
                _hintBlinkCoroutine = null;
            }
        }

        public void BlinkHintOutline(bool restoreVisualGuidance)
        {
            PlayHintSound();

            if (outline)
            {
                if (_hintBlinkCoroutine != null) StopCoroutine(_hintBlinkCoroutine);
                _hintBlinkCoroutine = StartCoroutine(BlinkRoutine(restoreVisualGuidance));
            }
        }

        public void PlayHintSound(Vector3? customPosition = null)
        {
            if (hintSound == null) return;
            Vector3 playPos = customPosition ?? transform.position;
            float volume = 0.6f;
            if (SessionContext.Instance != null)
            {
                volume *= SessionContext.Instance.MaxVolume;
            }
            AudioSource.PlayClipAtPoint(hintSound, playPos, volume);
        }

        private IEnumerator BlinkRoutine(bool restoreVisualGuidance)
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
    }
}
