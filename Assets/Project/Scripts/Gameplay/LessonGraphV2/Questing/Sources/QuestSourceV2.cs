using System;
using System.Threading;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    [DisallowMultipleComponent]
    public abstract class QuestSourceV2 : MonoBehaviour, IQuestSource
    {
        [Tooltip("Stable ID used by LessonGraph quest node completion bindings.")]
        [SerializeField] private string _bindingId = string.Empty;

        private int _mainThreadId;
        private QuestSourceActivation _activation;
        private bool _cleanupPerformed;

        public string BindingId => _bindingId ?? string.Empty;
        public QuestSourceState State { get; private set; } = QuestSourceState.Inactive;
        public string CurrentActivationId => _activation?.ActivationId ?? string.Empty;
        public bool IsAvailable => isActiveAndEnabled && State == QuestSourceState.Inactive;

        public event Action<QuestSourceState> StateChanged;
        public event Action<QuestSourceResult> Terminated;

        protected virtual void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool TryActivate(QuestSourceActivation activation)
        {
            if (!IsMainThread() || activation == null || !IsAvailable) return false;

            _activation = activation;
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
            return true;
        }

        public bool TryCancel(QuestSourceCancellation cancellation)
        {
            if (!IsMainThread() || cancellation == null) return false;
            return Terminate(
                cancellation.ActivationId,
                QuestSourceTerminalStatus.Cancelled,
                QuestSourceState.Cancelled,
                string.Empty,
                string.Empty,
                cancellation.Reason,
                DateTimeOffset.UtcNow,
                Time.realtimeSinceStartupAsDouble);
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
            return Terminate(
                activationId,
                QuestSourceTerminalStatus.Completed,
                QuestSourceState.Completed,
                completionChannel,
                string.Empty,
                string.Empty,
                completedAtUtc,
                completedAtMonotonicSeconds);
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
            return Terminate(
                activationId,
                QuestSourceTerminalStatus.Failed,
                QuestSourceState.Failed,
                string.Empty,
                failureCode,
                string.Empty,
                completedAtUtc,
                completedAtMonotonicSeconds);
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
            if (!CanAcceptSignal(activationId, allowCompleting: true)) return false;

            var result = new QuestSourceResult(
                activationId,
                BindingId,
                completionChannel,
                terminalStatus,
                completedAtUtc,
                completedAtMonotonicSeconds,
                failureCode,
                cancellationReason);
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
            try { OnSourceCleanup(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void HandleUnavailable()
        {
            if (!IsMainThread()) return;
            if (State != QuestSourceState.Activating &&
                State != QuestSourceState.Active &&
                State != QuestSourceState.Completing)
                return;

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