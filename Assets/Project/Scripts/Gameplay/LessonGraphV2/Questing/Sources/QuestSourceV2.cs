using System;
using System.Threading;
using UnityEngine;
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
        [Tooltip("Optional visual hint target: a dedicated indicator GameObject is activated, or an object with Outline has only its Outline.enabled toggled. Outline targets use the visual guidance profile baseline.")]
        [SerializeField] private GameObject _visualHintIndicator;

        private int _mainThreadId;
        private QuestSourceActivation _activation;
        private bool _cleanupPerformed;
        private string _lastCancellationReason = string.Empty;
        private bool _hintIndicatorStateCaptured;
        private bool _hintIndicatorWasActive;
        private Outline _visualHintOutline;

        public string BindingId => _bindingId ?? string.Empty;
        public bool CanShowVisualHint => _visualHintIndicator != null;
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
            _hintIndicatorStateCaptured = _visualHintIndicator != null && _visualHintOutline == null;
            _hintIndicatorWasActive = _hintIndicatorStateCaptured && _visualHintIndicator.activeSelf;
            if (_visualHintOutline != null)
                _visualHintOutline.enabled = SessionContext.Instance?.CurrentParams?.Actions?.EnableVisualGuidance ?? false;
            SetState(QuestSourceState.Activating);
            try
            {
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
            if (_visualHintOutline != null)
                _visualHintOutline.enabled = true;
            else
                _visualHintIndicator.SetActive(true);
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

            _activation = null;
            _lastCancellationReason = string.Empty;
            _cleanupPerformed = false;
            _hintIndicatorStateCaptured = false;
            _hintIndicatorWasActive = false;
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
            SetState(terminalState);
            Emit(Terminated, result);
            CleanupOnce();
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
            RestoreHintIndicatorState();
            try { OnSourceCleanup(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void RestoreHintIndicatorState()
        {
            ResolveVisualHintOutline();
            if (_visualHintOutline != null)
            {
                _visualHintOutline.enabled = false;
                _hintIndicatorStateCaptured = false;
                return;
            }

            if (!_hintIndicatorStateCaptured) return;
            _hintIndicatorStateCaptured = false;
            if (_visualHintIndicator != null) _visualHintIndicator.SetActive(_hintIndicatorWasActive);
        }

        private void ResolveVisualHintOutline()
        {
            _visualHintOutline = _visualHintIndicator != null
                ? _visualHintIndicator.GetComponent<Outline>()
                : null;
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

        private void OnDisable() => HandleUnavailable();
        private void OnDestroy() => HandleUnavailable();
    }
}
