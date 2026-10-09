using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    /// <summary>Completes after an allowed interactor remains in this trigger for the configured dwell time.</summary>
    [DisallowMultipleComponent]
    public class HoldTouchQuestSourceV2 : QuestSourceV2
    {
        [SerializeField] private LayerMask _interactorLayers;
        [SerializeField] private float _holdDurationSeconds = 1f;

        private readonly HashSet<Collider> _contacts = new HashSet<Collider>();
        private double _contactStartedAt = double.NaN;
        private float _lastPublishedProgress;

        protected virtual double UnscaledMonotonicSeconds => Time.realtimeSinceStartupAsDouble;
        public bool HasActiveContact => State == QuestSourceState.Active && _contacts.Count > 0;
        public float NormalizedHoldProgress
        {
            get
            {
                if (!HasActiveContact || !IsValidDuration(_holdDurationSeconds) || double.IsNaN(_contactStartedAt)) return 0f;
                return Mathf.Clamp01((float)((UnscaledMonotonicSeconds - _contactStartedAt) / _holdDurationSeconds));
            }
        }

        public event Action<bool> ContactEligibilityChanged;
        public event Action<float> HoldProgressChanged;

        private void OnTriggerEnter(Collider other)
        {
            if (State != QuestSourceState.Active || !IsAllowed(other)) return;
            PruneContacts();
            if (_contacts.Add(other) && _contacts.Count == 1)
            {
                _contactStartedAt = UnscaledMonotonicSeconds;
                _lastPublishedProgress = 0f;
                ContactEligibilityChanged?.Invoke(true);
                HoldProgressChanged?.Invoke(0f);
                Debug.Log($"[LessonGraphV2] HoldTouch timer START binding='{BindingId}' collider={other.gameObject.name}", this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            _contacts.Remove(other);
            PruneContacts();
            if (_contacts.Count == 0)
            {
                _contactStartedAt = double.NaN;
                _lastPublishedProgress = 0f;
                ContactEligibilityChanged?.Invoke(false);
                HoldProgressChanged?.Invoke(0f);
                Debug.Log($"[LessonGraphV2] HoldTouch timer RESET binding='{BindingId}' (all contacts lost)", this);
            }
        }

        private void Update()
        {
            if (State != QuestSourceState.Active) return;
            bool hadContact = _contacts.Count > 0;
            PruneContacts();
            if (_contacts.Count == 0)
            {
                _contactStartedAt = double.NaN;
                if (hadContact)
                {
                    _lastPublishedProgress = 0f;
                    ContactEligibilityChanged?.Invoke(false);
                    HoldProgressChanged?.Invoke(0f);
                }
                PublishProgress(0f);
                return;
            }
            if (!IsValidDuration(_holdDurationSeconds)) return;
            if (double.IsNaN(_contactStartedAt)) _contactStartedAt = UnscaledMonotonicSeconds;
            PublishProgress(NormalizedHoldProgress);
            if (UnscaledMonotonicSeconds - _contactStartedAt >= _holdDurationSeconds)
            {
                if (TryComplete(CurrentActivationId, "hold_touch"))
                {
                    Debug.Log($"[LessonGraphV2] HOLD_TOUCH completed binding='{BindingId}' duration={_holdDurationSeconds}s contacts={_contacts.Count}", this);
                }
            }
        }

        protected override void OnSourceActivated(QuestSourceActivation activation)
        {
            _contacts.Clear();
            _contactStartedAt = double.NaN;
            _lastPublishedProgress = 0f;
            ContactEligibilityChanged?.Invoke(false);
            HoldProgressChanged?.Invoke(0f);
            if (!IsValidDuration(_holdDurationSeconds))
            {
                Debug.LogError($"[LessonGraphV2] HoldTouch invalid duration={_holdDurationSeconds} binding='{BindingId}'", this);
                TryFail(activation.ActivationId, QuestSourceFailureCodes.ActivationFailed);
            }
        }

        protected override void OnSourceCleanup()
        {
            _contacts.Clear();
            _contactStartedAt = double.NaN;
            _lastPublishedProgress = 0f;
            ContactEligibilityChanged?.Invoke(false);
            HoldProgressChanged?.Invoke(0f);
        }

        private void PublishProgress(float progress)
        {
            if (Mathf.Abs(progress - _lastPublishedProgress) < 0.001f) return;
            _lastPublishedProgress = progress;
            HoldProgressChanged?.Invoke(progress);
        }

        private bool IsAllowed(Collider collider) => collider != null &&
            (_interactorLayers.value & (1 << collider.gameObject.layer)) != 0;
        private void PruneContacts() => _contacts.RemoveWhere(collider =>
            collider == null || collider.gameObject == null || !collider.gameObject.activeInHierarchy);
        private static bool IsValidDuration(float duration) => duration > 0f && !float.IsNaN(duration) && !float.IsInfinity(duration);
    }
}
