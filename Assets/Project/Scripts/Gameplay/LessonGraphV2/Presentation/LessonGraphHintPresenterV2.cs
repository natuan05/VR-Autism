using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRAutism.Core;
using VRAutism.Core.Models;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Presentation
{
    /// <summary>Maps active V2 quest sources to the legacy trial hint anchors and schedules audited visual reminders.</summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphHintPresenterV2 : MonoBehaviour
    {
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private GameObject _bubblePrefab;
        [SerializeField, Tooltip("Inactive sprite-backed radial progress template used for hold quests.")]
        private GameObject _progressPrefab;

        private readonly Dictionary<string, ReminderSchedule> _reminders = new Dictionary<string, ReminderSchedule>(StringComparer.Ordinal);
        private LessonStateV2 _state;
        private QuestSourceV2 _activeSource;
        private GameObject _bubble;
        private GameObject _progressRoot;
        private Slider _progressSlider;
        private string _trackedActivationId = string.Empty;
        private int _autoCommandSequence;
        private bool _warnedMissingProgressPrefab;

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.StateChanged += HandleStateChanged;
                _state = _runner.CurrentState;
            }
            RefreshPresentation();
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.StateChanged -= HandleStateChanged;
            ClearPresentation();
            _reminders.Clear();
        }

        private void Update()
        {
            if (_runner == null) return;
            // StateChanged is emitted before the executor has registered its binding; polling CurrentState
            // also observes the runner's subsequent executor-ready revision.
            var current = _runner.CurrentState;
            if (current != null && (_state == null || current.state_revision != _state.state_revision))
                _state = current;

            RefreshPresentation();
            ScheduleAutoReminder();
        }

        /// <summary>Clears scene-owned hint UI when lesson lifecycle teardown begins.</summary>
        public void ClearPresentation()
        {
            DestroyPresentationObject(_bubble);
            DestroyPresentationObject(_progressRoot);
            _bubble = null;
            _progressRoot = null;
            _progressSlider = null;
            _activeSource = null;
            _trackedActivationId = string.Empty;
            _reminders.Clear();
        }

        private void HandleStateChanged(LessonStateV2 state)
        {
            _state = state;
            if (state == null || !string.Equals(state.status, "running", StringComparison.Ordinal))
                ClearPresentation();
        }

        private void RefreshPresentation()
        {
            if (_state == null || !string.Equals(_state.status, "running", StringComparison.Ordinal) ||
                _bindings == null || string.IsNullOrEmpty(_state.activation_id))
            {
                ClearPresentation();
                return;
            }

            var source = FindActiveSource(_state);
            var expectedActivationId = GetExpectedActivationId(_state);
            if (source == null || source.State != QuestSourceState.Active ||
                !string.Equals(source.CurrentActivationId, expectedActivationId, StringComparison.Ordinal))
            {
                ClearPresentation();
                return;
            }

            if (_activeSource != source || !string.Equals(_trackedActivationId, expectedActivationId, StringComparison.Ordinal))
            {
                ClearPresentation();
                _activeSource = source;
                _trackedActivationId = expectedActivationId;
            }

            var actions = CurrentActions();
            if (actions != null && actions.EnableBubbleHints && source.HintBubbleAnchor != null)
                EnsureBubble(source.HintBubbleAnchor);
            else if (_bubble != null)
            {
                DestroyPresentationObject(_bubble);
                _bubble = null;
            }

            if (source is HoldTouchQuestSourceV2 hold && hold.HasActiveContact && source.HintProgressAnchor != null)
                SetProgress(source.HintProgressAnchor, hold.NormalizedHoldProgress);
            else
                HideProgress();
        }

        private QuestSourceV2 FindActiveSource(LessonStateV2 state, bool visualHintOnly = false)
        {
            if (state == null) return null;
            var scope = _runner?.ActiveQuestScope;
            var childScope = scope != null &&
                string.Equals(scope.ParentSessionId, state.session_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentRunId, state.run_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentNodeId, state.node_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentActivationId, state.activation_id, StringComparison.Ordinal);
            QuestSourceV2 firstEligible = null;
            var childBindingIds = childScope ? scope.BindingIds : null;
            var bindingCount = childScope ? childBindingIds.Length : (state.bindings?.Length ?? 0);
            for (int i = 0; i < bindingCount; i++)
            {
                var bindingId = childScope ? childBindingIds[i] : state.bindings[i]?.binding_id;
                if (string.IsNullOrWhiteSpace(bindingId)) continue;
                if (!childScope)
                {
                    var binding = state.bindings[i];
                    if (binding == null || !binding.can_visual_hint && !binding.can_verbal_hint) continue;
                }
                if (_bindings.TryGetBoundSource(bindingId, out var source) &&
                    source.State == QuestSourceState.Active &&
                    string.Equals(source.CurrentActivationId, childScope ? scope.ChildActivationId : state.activation_id, StringComparison.Ordinal))
                {
                    if (visualHintOnly && (!source.CanShowVisualHint ||
                        source is HoldTouchQuestSourceV2 activeHold && activeHold.HasActiveContact)) continue;
                    if (firstEligible == null) firstEligible = source;
                    if (source is HoldTouchQuestSourceV2 hold && hold.HasActiveContact) return source;
                }
            }
            return firstEligible;
        }

        private string GetExpectedActivationId(LessonStateV2 state)
        {
            var scope = _runner?.ActiveQuestScope;
            return scope != null && state != null &&
                string.Equals(scope.ParentSessionId, state.session_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentRunId, state.run_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentNodeId, state.node_id, StringComparison.Ordinal) &&
                string.Equals(scope.ParentActivationId, state.activation_id, StringComparison.Ordinal)
                ? scope.ChildActivationId
                : state?.activation_id ?? string.Empty;
        }

        private void ScheduleAutoReminder()
        {
            var actions = CurrentActions();
            if (actions == null || !actions.EnableAutoHint)
            {
                _reminders.Clear();
                return;
            }
            if (_runner == null || _state == null ||
                !string.Equals(_state.status, "running", StringComparison.Ordinal))
                return;

            var source = FindActiveSource(_state, visualHintOnly: true);
            if (source == null || source.State != QuestSourceState.Active || !source.CanShowVisualHint)
                return;

            var cycle = actions.ActionReminderCycle >= 0f
                ? actions.ActionReminderCycle
                : source.ReminderCycleSeconds;
            if (cycle <= 0f || float.IsNaN(cycle) || float.IsInfinity(cycle)) return;

            string key = source.BindingId;
            var activeActivationId = GetExpectedActivationId(_state);
            if (!_reminders.TryGetValue(key, out var schedule) ||
                !string.Equals(schedule.ActivationId, activeActivationId, StringComparison.Ordinal) ||
                !Mathf.Approximately(schedule.Interval, cycle))
            {
                schedule = new ReminderSchedule(activeActivationId, cycle, Time.unscaledTime + cycle);
                _reminders[key] = schedule;
                return;
            }
            if (Time.unscaledTime < schedule.NextAt) return;

            schedule.NextAt = Time.unscaledTime + cycle;
            _reminders[key] = schedule;
            _ = SendVisualReminderAsync(source.BindingId, _state);
        }

        private async Task SendVisualReminderAsync(string bindingId, LessonStateV2 state)
        {
            if (_runner == null || state == null) return;
            var command = new LessonCommandV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandEvent,
                command_id = $"auto-visual-{state.activation_id}-{++_autoCommandSequence}",
                session_id = state.session_id,
                run_id = state.run_id,
                node_id = state.node_id,
                activation_id = state.activation_id,
                command = LessonCommandKindV2.VisualHint,
                binding_id = bindingId,
            };

            try { await _runner.ApplyCommandAsync(command); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void EnsureBubble(Transform anchor)
        {
            if (_bubblePrefab == null || anchor == null) return;
            if (_bubble == null)
            {
                _bubble = Instantiate(_bubblePrefab);
                // The shared prefab is also used by the counting lesson and ships with its
                // Vietnamese number sequence. Quest guidance has no text contract, so retain
                // the bubble visual while clearing that unrelated exercise text.
                foreach (var text in _bubble.GetComponentsInChildren<TMP_Text>(true))
                    text.text = string.Empty;
            }
            _bubble.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            if (_bubble.GetComponent<BillboardEffect>() == null)
                _bubble.AddComponent<BillboardEffect>();
            if (!_bubble.activeSelf) _bubble.SetActive(true);
        }

        private void SetProgress(Transform anchor, float value)
        {
            if (anchor == null) return;
            if (_progressPrefab == null)
            {
                WarnMissingProgressPrefab();
                HideProgress();
                return;
            }
            if (_progressRoot == null) CreateProgressWidget();
            if (_progressRoot == null) return;
            _progressRoot.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            _progressSlider.value = Mathf.Clamp01(value);
            if (!_progressRoot.activeSelf) _progressRoot.SetActive(true);
        }

        private void CreateProgressWidget()
        {
            var progressRoot = Instantiate(_progressPrefab);
            var progressSlider = progressRoot.GetComponentInChildren<Slider>(true);
            var fillImage = progressSlider != null && progressSlider.fillRect != null
                ? progressSlider.fillRect.GetComponent<Image>()
                : null;
            if (progressSlider == null || fillImage == null || fillImage.sprite == null ||
                fillImage.type != Image.Type.Filled || fillImage.fillMethod != Image.FillMethod.Radial360)
            {
                DestroyPresentationObject(progressRoot);
                WarnMissingProgressPrefab();
                return;
            }

            progressRoot.name = "LessonGraphHoldProgressV2";
            _progressRoot = progressRoot;
            _progressSlider = progressSlider;
            _progressSlider.value = 0f;
        }

        private void WarnMissingProgressPrefab()
        {
            if (_warnedMissingProgressPrefab) return;
            Debug.LogWarning("[LessonGraphV2] Hold progress UI needs a radial progress prefab with a sprite-backed filled Image.", this);
            _warnedMissingProgressPrefab = true;
        }

        private void HideProgress()
        {
            if (_progressSlider != null) _progressSlider.SetValueWithoutNotify(0f);
            if (_progressRoot != null) _progressRoot.SetActive(false);
        }

        private static void DestroyPresentationObject(GameObject presentationObject)
        {
            if (presentationObject == null) return;
            if (Application.isPlaying) Destroy(presentationObject);
            else DestroyImmediate(presentationObject);
        }

        private static LessonParameters.ActionParams CurrentActions() =>
            SessionContext.Instance?.CurrentParams?.Actions;

        private sealed class ReminderSchedule
        {
            public readonly string ActivationId;
            public readonly float Interval;
            public float NextAt;
            public ReminderSchedule(string activationId, float interval, float nextAt)
            {
                ActivationId = activationId;
                Interval = interval;
                NextAt = nextAt;
            }
        }
    }
}
