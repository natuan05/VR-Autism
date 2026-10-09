using System;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Integration
{
    /// <summary>Owns the Bathroom V2 tap animation, running-water particles, and local water audio.</summary>
    public class FaucetEffectsV2 : MonoBehaviour
    {
        private const string AnimatorOpenParameter = "Open";
        private const string AnimatorClosedParameter = "Closed";

        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private Animator _tapAnimator;
        [SerializeField] private ParticleSystem _runningWater;
        [SerializeField] private AudioSource _waterAudioSource;
        [SerializeField] private AudioClip _waterClip;

        private bool _subscribed;
        private bool _wiringValid;
        private bool _isOpen;
        private bool _isPaused;
        private bool _hasAppliedClosedOutputs;
        private bool _hasScope;
        private string _scopeSessionId;
        private string _scopeRunId;
        private string _lastStateStatus;

        public bool IsOpen => _isOpen;

        private void OnEnable()
        {
            _wiringValid = ValidateRequiredReferences();
            ResetEffects();
            if (_runner == null) return;

            if (!_subscribed)
            {
                _runner.StateChanged += OnRunnerStateChanged;
                _subscribed = true;
            }

            ObserveCurrentState(_runner.CurrentState);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetEffects();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ResetEffects();
        }

        /// <summary>Opens or closes the tap when the assigned runner is in a valid running lesson.</summary>
        public void SetOpen(bool open)
        {
            if (!open)
            {
                CloseEffects();
                return;
            }

            if (!isActiveAndEnabled || !_wiringValid || !IsCurrentScopeRunning()) return;
            if (_isOpen) return;

            _isOpen = true;
            _isPaused = false;
            _hasAppliedClosedOutputs = false;
            ApplyAnimatorState(true);
            ApplyRunningWater(true);
            PlayWaterAudio();
        }

        /// <summary>Closes the tap and stops its local visual and audio effects.</summary>
        public void ResetEffects()
        {
            CloseEffects();
        }

        protected virtual void ApplyAnimatorState(bool open)
        {
            if (_tapAnimator == null) return;
            _tapAnimator.SetBool(AnimatorOpenParameter, open);
            _tapAnimator.SetBool(AnimatorClosedParameter, !open);
        }

        protected virtual void ApplyRunningWater(bool enabled)
        {
            if (_runningWater == null) return;
            if (enabled)
            {
                // The legacy WaterLeak instance starts inactive in Bathroom-V2.
                // Playback must activate that scene-owned visual before starting emission.
                if (!_runningWater.gameObject.activeSelf) _runningWater.gameObject.SetActive(true);
                _runningWater.Play(true);
            }
            else _runningWater.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        protected virtual void PlayWaterAudio()
        {
            if (_waterAudioSource == null || _waterClip == null) return;
            if (_waterAudioSource.clip != _waterClip) _waterAudioSource.clip = _waterClip;
            _waterAudioSource.loop = true;
            _waterAudioSource.playOnAwake = false;
            _waterAudioSource.Play();
        }

        protected virtual void PauseWaterAudio()
        {
            if (_waterAudioSource != null) _waterAudioSource.Pause();
        }

        protected virtual void UnPauseWaterAudio()
        {
            if (_waterAudioSource != null) _waterAudioSource.UnPause();
        }

        protected virtual void StopWaterAudio()
        {
            if (_waterAudioSource != null) _waterAudioSource.Stop();
        }

        private void OnRunnerStateChanged(LessonStateV2 state)
        {
            ObserveCurrentState(state);
        }

        private void ObserveCurrentState(LessonStateV2 state)
        {
            if (_runner == null || state == null) return;

            LessonStateV2 current = _runner.CurrentState;
            if (!IsCurrentPayload(state, current)) return;

            string status = state.status ?? string.Empty;
            bool sameScope = _hasScope &&
                string.Equals(_scopeSessionId, state.session_id, StringComparison.Ordinal) &&
                string.Equals(_scopeRunId, state.run_id, StringComparison.Ordinal);

            if (!sameScope)
            {
                CloseEffects();
                _scopeSessionId = state.session_id;
                _scopeRunId = state.run_id;
                _hasScope = !string.IsNullOrEmpty(_scopeSessionId) && !string.IsNullOrEmpty(_scopeRunId);
                _isPaused = false;
            }

            if (IsTerminal(status))
            {
                CloseEffects();
                _isPaused = false;
                _lastStateStatus = status;
                return;
            }

            if (IsPausingOrPaused(status))
            {
                if (_isOpen && !_isPaused) PauseWaterAudio();
                _isPaused = true;
                _lastStateStatus = status;
                return;
            }

            if (IsRunning(status))
            {
                bool wasPaused = _isPaused || IsPausingOrPaused(_lastStateStatus);
                _isPaused = false;
                if (sameScope && wasPaused && _isOpen) UnPauseWaterAudio();
            }

            _lastStateStatus = status;
        }

        private bool IsCurrentScopeRunning()
        {
            if (!_hasScope || _runner == null || !IsRunning(_lastStateStatus)) return false;
            LessonStateV2 current = _runner.CurrentState;
            return current != null && IsRunning(current.status) &&
                string.Equals(_scopeSessionId, current.session_id, StringComparison.Ordinal) &&
                string.Equals(_scopeRunId, current.run_id, StringComparison.Ordinal);
        }

        private static bool IsCurrentPayload(LessonStateV2 payload, LessonStateV2 current)
        {
            return current != null &&
                payload.state_revision == current.state_revision &&
                string.Equals(payload.session_id, current.session_id, StringComparison.Ordinal) &&
                string.Equals(payload.run_id, current.run_id, StringComparison.Ordinal) &&
                string.Equals(payload.status, current.status, StringComparison.Ordinal);
        }

        private static bool IsRunning(string status) =>
            string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);

        private static bool IsPausingOrPaused(string status) =>
            string.Equals(status, "pausing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase);

        private static bool IsTerminal(string status) =>
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

        private bool ValidateRequiredReferences()
        {
            string missing = string.Empty;
            AppendMissing(ref missing, _runner == null, nameof(_runner));
            AppendMissing(ref missing, _tapAnimator == null, nameof(_tapAnimator));
            AppendMissing(ref missing, _runningWater == null, nameof(_runningWater));
            AppendMissing(ref missing, _waterAudioSource == null, nameof(_waterAudioSource));
            AppendMissing(ref missing, _waterClip == null, nameof(_waterClip));
            if (missing.Length == 0) return true;

            Debug.LogError("FaucetEffectsV2 requires " + missing + " to be assigned; faucet opening is disabled.", this);
            return false;
        }

        private static void AppendMissing(ref string missing, bool isMissing, string fieldName)
        {
            if (!isMissing) return;
            if (missing.Length > 0) missing += ", ";
            missing += fieldName;
        }

        private void CloseEffects()
        {
            if (!_isOpen && !_isPaused && _hasAppliedClosedOutputs) return;

            _isOpen = false;
            _isPaused = false;
            _hasAppliedClosedOutputs = true;
            ApplyAnimatorState(false);
            ApplyRunningWater(false);
            StopWaterAudio();
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_runner != null) _runner.StateChanged -= OnRunnerStateChanged;
            _subscribed = false;
        }
    }
}
