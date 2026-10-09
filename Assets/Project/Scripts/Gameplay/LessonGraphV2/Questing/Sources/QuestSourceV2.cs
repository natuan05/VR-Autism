using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using Plugins.QuickOutline.Scripts;
using VRAutism.Core;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    [DisallowMultipleComponent]
    public abstract class QuestSourceV2 : MonoBehaviour, IQuestSource, IQuestVisualHintV2
    {
        internal const string PauseCancellationReason = "pause";

        [Tooltip("Stable ID used by LessonGraph quest node completion bindings.")]
        [SerializeField] private string _bindingId = string.Empty;
        [Tooltip("Hint feedback is supported only when this object has an Outline component. The interactable GameObject is never activated/deactivated by hinting.")]
        [SerializeField] private GameObject _visualHintIndicator;
        [SerializeField] private Transform _hintBubbleAnchor;
        [SerializeField] private Transform _hintProgressAnchor;
        [SerializeField] private AudioClip _hintClip;
        [SerializeField] private float _reminderCycleSeconds;
        [Header("Legacy trial scene effects")]
        [SerializeField] private UnityEvent _onLessonActivated = new UnityEvent();
        [SerializeField] private UnityEvent _onLessonCompleted = new UnityEvent();

        private int _mainThreadId;
        private QuestSourceActivation _activation;
        private bool _cleanupPerformed;
        private bool _cleanupCompleted;
        private bool _terminationInProgress;
        private string _lastCancellationReason = string.Empty;
        private bool _hintProfileBaseline;
        private Outline _visualHintOutline;
        private Coroutine _hintBlinkRoutine;
        private AudioSource _hintAudioSource;

        public string BindingId => _bindingId ?? string.Empty;
        public bool CanShowVisualHint => ResolveVisualHintOutline() != null;
        public Transform HintBubbleAnchor => _hintBubbleAnchor;
        public Transform HintProgressAnchor => _hintProgressAnchor;
        public AudioClip HintClip => _hintClip;
        public float ReminderCycleSeconds => _reminderCycleSeconds;
        public QuestSourceState State { get; private set; } = QuestSourceState.Inactive;
        public string CurrentActivationId => _activation?.ActivationId ?? string.Empty;
        public bool IsAvailable => isActiveAndEnabled && State == QuestSourceState.Inactive;

        public event Action<QuestSourceState> StateChanged;
        public event Action<QuestSourceResult> Terminated;

        protected virtual void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            ResolveVisualHintOutline();
            if (_visualHintOutline != null) _visualHintOutline.enabled = false;
        }

        public bool TryActivate(QuestSourceActivation activation)
        {
            if (!IsMainThread() || activation == null || !IsAvailable)
            {
                Debug.LogWarning($"[LessonGraphV2] Source activation REJECTED binding='{BindingId}' available={IsAvailable} mainThread={IsMainThread()}", this);
                return false;
            }

            _activation = activation;
            _lastCancellationReason = string.Empty;
            ResolveVisualHintOutline();
            _hintProfileBaseline = SessionContext.Instance?.CurrentParams?.Actions?.EnableVisualGuidance ?? false;
            if (_visualHintOutline != null)
                _visualHintOutline.enabled = _hintProfileBaseline;
            SetState(QuestSourceState.Activating);
            try
            {
                InvokeSceneEvent(_onLessonActivated);
                OnSourceActivated(activation);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                TryFail(
                    activation.ActivationId,
                    QuestSourceFailureCodes.ActivationFailed,
                    DateTimeOffset.UtcNow,
                    Time.realtimeSinceStartupAsDouble);
                return true;
            }

            if (State == QuestSourceState.Activating)
                SetState(QuestSourceState.Active);
            
            Debug.Log($"[LessonGraphV2] Source ACTIVATED binding='{BindingId}' activation={activation.ActivationId}", this);
            return true;
        }

        public bool TryShowVisualHint(string activationId)
        {
            if (!IsMainThread() || _activation == null || State != QuestSourceState.Active ||
                !string.Equals(_activation.ActivationId, activationId, StringComparison.Ordinal) ||
                _visualHintIndicator == null)
                return false;

            ResolveVisualHintOutline();
            if (_visualHintOutline == null) return false;
            StopHintFeedback(restoreBaseline: true);
            if (Application.isPlaying)
                _hintBlinkRoutine = StartCoroutine(BlinkHintRoutine());
            else
                _visualHintOutline.enabled = true;
            PlayHintClip();
            return true;
        }

        public bool TryCancel(QuestSourceCancellation cancellation)
        {
            if (!IsMainThread() || cancellation == null) return false;
            bool result = Terminate(
                cancellation.ActivationId,
                QuestSourceTerminalStatus.Cancelled,
                QuestSourceState.Cancelled,
                string.Empty,
                string.Empty,
                cancellation.Reason,
                DateTimeOffset.UtcNow,
                Time.realtimeSinceStartupAsDouble);

            if (result)
            {
                Debug.Log($"[LessonGraphV2] Source CANCELLED binding='{BindingId}' activation={cancellation.ActivationId} reason={cancellation.Reason}", this);
            }
            return result;
        }

        public bool TryRearmAfterPause(string activationId)
        {
            if (!IsMainThread() || State != QuestSourceState.Cancelled || _activation == null ||
                !string.Equals(_activation.ActivationId, activationId, StringComparison.Ordinal) ||
                !string.Equals(_lastCancellationReason, PauseCancellationReason, StringComparison.Ordinal))
                return false;

            return TryRearmAfterExecution(activationId);
        }

        /// <summary>Releases a cleaned terminal activation after its executor has detached its listeners.</summary>
        public bool TryRearmAfterExecution(string activationId)
        {
            if (!IsMainThread() || _activation == null || _terminationInProgress || !_cleanupCompleted ||
                !string.Equals(_activation.ActivationId, activationId, StringComparison.Ordinal) ||
                (State != QuestSourceState.Completed && State != QuestSourceState.Cancelled && State != QuestSourceState.Failed))
                return false;

            _activation = null;
            _lastCancellationReason = string.Empty;
            _cleanupPerformed = false;
            _cleanupCompleted = false;
            _hintProfileBaseline = false;
            SetState(QuestSourceState.Inactive);
            return true;
        }

        protected bool TryComplete(string activationId, string completionChannel)
        {
            return TryComplete(
                activationId,
                completionChannel,
                DateTimeOffset.UtcNow,
                Time.realtimeSinceStartupAsDouble);
        }

        protected bool TryComplete(
            string activationId,
            string completionChannel,
            DateTimeOffset completedAtUtc,
            double completedAtMonotonicSeconds)
        {
            if (!CanAcceptSignal(activationId, allowCompleting: false)) return false;

            SetState(QuestSourceState.Completing);
            bool result = Terminate(
                activationId,
                QuestSourceTerminalStatus.Completed,
                QuestSourceState.Completed,
                completionChannel,
                string.Empty,
                string.Empty,
                completedAtUtc,
                completedAtMonotonicSeconds);
                
            if (result)
            {
                Debug.Log($"[LessonGraphV2] Source COMPLETED binding='{BindingId}' channel={completionChannel} activation={activationId}", this);
            }
            return result;
        }

        protected bool TryFail(string activationId, string failureCode)
        {
            return TryFail(
                activationId,
                failureCode,
                DateTimeOffset.UtcNow,
                Time.realtimeSinceStartupAsDouble);
        }

        protected bool TryFail(
            string activationId,
            string failureCode,
            DateTimeOffset completedAtUtc,
            double completedAtMonotonicSeconds)
        {
            bool result = Terminate(
                activationId,
                QuestSourceTerminalStatus.Failed,
                QuestSourceState.Failed,
                string.Empty,
                failureCode,
                string.Empty,
                completedAtUtc,
                completedAtMonotonicSeconds);
                
            if (result)
            {
                Debug.LogWarning($"[LessonGraphV2] Source FAILED binding='{BindingId}' code={failureCode} activation={activationId}", this);
            }
            return result;
        }

        protected virtual void OnSourceActivated(QuestSourceActivation activation) { }
        protected virtual void OnSourceCleanup() { }

        private bool Terminate(
            string activationId,
            QuestSourceTerminalStatus terminalStatus,
            QuestSourceState terminalState,
            string completionChannel,
            string failureCode,
            string cancellationReason,
            DateTimeOffset completedAtUtc,
            double completedAtMonotonicSeconds)
        {
            if (!CanAcceptSignal(activationId, allowCompleting: true))
            {
                Debug.LogWarning($"[LessonGraphV2] Source signal REJECTED (stale/wrong state) binding='{BindingId}' attemptedActivation={activationId} currentState={State}", this);
                return false;
            }

            var result = new QuestSourceResult(
                activationId,
                BindingId,
                completionChannel,
                terminalStatus,
                completedAtUtc,
                completedAtMonotonicSeconds,
                failureCode,
                cancellationReason);
            _lastCancellationReason = terminalStatus == QuestSourceTerminalStatus.Cancelled
                ? cancellationReason ?? string.Empty
                : string.Empty;
            // Terminal callbacks and cleanup belong to this activation. They must finish before
            // a new owner can rearm it, including reentrant state/termination callbacks.
            _terminationInProgress = true;
            try
            {
                SetState(terminalState);
                if (terminalStatus == QuestSourceTerminalStatus.Completed)
                    InvokeSceneEvent(_onLessonCompleted);
                Emit(Terminated, result);
                CleanupOnce();
            }
            finally { _terminationInProgress = false; }
            return true;
        }

        private bool CanAcceptSignal(string activationId, bool allowCompleting)
        {
            if (!IsMainThread() || _activation == null || activationId != _activation.ActivationId)
                return false;

            return State == QuestSourceState.Activating ||
                   State == QuestSourceState.Active ||
                   (allowCompleting && State == QuestSourceState.Completing);
        }

        private bool IsMainThread()
        {
            return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private void SetState(QuestSourceState next)
        {
            State = next;
            Emit(StateChanged, next);
        }

        private void CleanupOnce()
        {
            if (_cleanupPerformed) return;
            _cleanupPerformed = true;
            try
            {
                RestoreHintIndicatorState();
                OnSourceCleanup();
                _cleanupCompleted = true;
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void RestoreHintIndicatorState()
        {
            StopHintFeedback(restoreBaseline: false);
            ResolveVisualHintOutline();
            if (_visualHintOutline != null)
            {
                _visualHintOutline.enabled = _hintProfileBaseline;
                return;
            }
        }

        private Outline ResolveVisualHintOutline()
        {
            _visualHintOutline = _visualHintIndicator != null
                ? _visualHintIndicator.GetComponent<Outline>()
                : null;
            return _visualHintOutline;
        }

        private IEnumerator BlinkHintRoutine()
        {
            for (int i = 0; i < 3; i++)
            {
                if (State != QuestSourceState.Active) break;
                if (_visualHintOutline != null) _visualHintOutline.enabled = true;
                yield return new WaitForSeconds(0.3f);
                if (_visualHintOutline != null) _visualHintOutline.enabled = false;
                yield return new WaitForSeconds(0.3f);
            }

            _hintBlinkRoutine = null;
            RestoreHintProfileBaseline();
        }

        private void PlayHintClip()
        {
            if (_hintClip == null) return;
            if (_hintAudioSource == null)
            {
                var audioObject = new GameObject("QuestVisualHintAudioV2");
                audioObject.transform.SetParent(transform, false);
                _hintAudioSource = audioObject.AddComponent<AudioSource>();
                _hintAudioSource.playOnAwake = false;
                _hintAudioSource.spatialBlend = 1f;
            }

            _hintAudioSource.Stop();
            _hintAudioSource.clip = _hintClip;
            _hintAudioSource.volume = 0.6f * (SessionContext.Instance != null ? SessionContext.Instance.MaxVolume : 1f);
            _hintAudioSource.Play();
        }

        private void StopHintFeedback(bool restoreBaseline)
        {
            if (_hintBlinkRoutine != null)
            {
                StopCoroutine(_hintBlinkRoutine);
                _hintBlinkRoutine = null;
            }
            if (_hintAudioSource != null) _hintAudioSource.Stop();
            if (restoreBaseline) RestoreHintProfileBaseline();
        }

        private void RestoreHintProfileBaseline()
        {
            if (_visualHintOutline != null) _visualHintOutline.enabled = _hintProfileBaseline;
        }

        private void HandleUnavailable()
        {
            if (!IsMainThread()) return;
            if (State != QuestSourceState.Activating &&
                State != QuestSourceState.Active &&
                State != QuestSourceState.Completing)
                return;

            Debug.LogWarning($"[LessonGraphV2] Source UNAVAILABLE (disable/destroy) binding='{BindingId}' state={State}", this);
            TryFail(
                CurrentActivationId,
                QuestSourceFailureCodes.BindingUnavailable,
                DateTimeOffset.UtcNow,
                Time.realtimeSinceStartupAsDouble);
        }

        private static void Emit<T>(Action<T> callbacks, T value)
        {
            if (callbacks == null) return;
            foreach (var subscriber in callbacks.GetInvocationList())
            {
                if (!(subscriber is Action<T> callback)) continue;
                try { callback(value); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private void InvokeSceneEvent(UnityEvent sceneEvent)
        {
            try { sceneEvent?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void OnDisable() => HandleUnavailable();
        private void OnDestroy() => HandleUnavailable();
    }
}
