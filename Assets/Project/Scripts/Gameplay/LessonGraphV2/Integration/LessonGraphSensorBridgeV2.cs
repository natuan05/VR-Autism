using System;
using System.Collections.Generic;
using UnityEngine;
using VRAutism.Core;
using VRAutism.Core.Telemetry;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Integration
{
    /// <summary>
    /// Gives the existing SensorHarvester one V2 target owner and projects runner scope into
    /// additive telemetry metadata. Active targets are resolved from Runner state bindings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphSensorBridgeV2 : MonoBehaviour
    {
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private SensorHarvester _sensorHarvester;

        private LessonStateV2 _lastState;
        private bool _ownershipLost;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_sensorHarvester != null)
                _sensorHarvester.SetLessonGraphRuntimeEnabled(true);

            if (_runner == null)
            {
                Debug.LogError("[LessonGraphV2] Sensor bridge requires a LessonGraphRunner.", this);
                return;
            }

            _runner.StateChanged += OnStateChanged;
            _runner.CommandEvaluated += OnCommandEvaluated;
            _ownershipLost = false;
            ApplyState(_runner.CurrentState);
        }

        private void Update()
        {
            if (_ownershipLost || _lastState == null) return;
            if (!IsCurrentSession(_lastState.session_id))
            {
                _ownershipLost = true;
                _sensorHarvester?.ClearLessonGraphScope();
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.StateChanged -= OnStateChanged;
                _runner.CommandEvaluated -= OnCommandEvaluated;
            }

            if (_sensorHarvester != null)
            {
                _sensorHarvester.ClearLessonGraphScope();
                _sensorHarvester.SetLessonGraphRuntimeEnabled(false);
            }
            _lastState = null;
        }

        private void ResolveReferences()
        {
            if (_runner == null) _runner = GetComponentInParent<LessonGraphRunner>();
            if (_bindings == null) _bindings = GetComponentInParent<LessonGraphBindings>();
            if (_sensorHarvester == null) _sensorHarvester = FindObjectOfType<SensorHarvester>();
        }

        private void OnStateChanged(LessonStateV2 state)
        {
            if (_ownershipLost || state == null) return;
            if (!IsCurrentSession(state.session_id))
            {
                _ownershipLost = true;
                _sensorHarvester?.ClearLessonGraphScope();
                return;
            }

            ApplyState(state);
        }

        private void ApplyState(LessonStateV2 state)
        {
            if (_sensorHarvester == null || state == null) return;

            var bindingIds = new List<string>();
            var targets = new List<Transform>();
            if (string.Equals(state.status, "running", StringComparison.Ordinal) && state.bindings != null)
            {
                for (int i = 0; i < state.bindings.Length; i++)
                {
                    string bindingId = state.bindings[i]?.binding_id;
                    if (string.IsNullOrWhiteSpace(bindingId) || bindingIds.Contains(bindingId)) continue;
                    bindingIds.Add(bindingId);
                    if (_bindings != null && _bindings.TryGetBoundSource(bindingId, out QuestSourceV2 source))
                        targets.Add(source.transform);
                }
            }

            _sensorHarvester.SetLessonGraphScope(
                state.session_id,
                state.run_id,
                state.node_id,
                state.activation_id,
                state.node_index,
                state.status,
                bindingIds.ToArray(),
                targets.ToArray());
            _lastState = state;
        }

        private void OnCommandEvaluated(LessonCommandResultV2 result)
        {
            if (result == null || !result.accepted ||
                !string.Equals(result.command, LessonCommandKindV2.VisualHint, StringComparison.Ordinal) ||
                _lastState == null || !IsCurrentSession(result.session_id) ||
                !string.Equals(result.session_id, _lastState.session_id, StringComparison.Ordinal) ||
                !string.Equals(result.run_id, _lastState.run_id, StringComparison.Ordinal) ||
                !string.Equals(result.node_id, _lastState.node_id, StringComparison.Ordinal) ||
                !string.Equals(result.activation_id, _lastState.activation_id, StringComparison.Ordinal) ||
                !string.Equals(_lastState.status, "running", StringComparison.Ordinal))
                return;

            float elapsedSeconds = TimeManager.Instance != null
                ? (float)TimeManager.Instance.GetTotalElapsedSeconds()
                : 0f;
            _sensorHarvester?.SetAcceptedVisualHintTime(elapsedSeconds);
        }

        private static bool IsCurrentSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            string currentSessionId = SessionContext.Instance?.SessionId;
            return !string.IsNullOrWhiteSpace(currentSessionId) &&
                   string.Equals(currentSessionId, sessionId, StringComparison.Ordinal);
        }
    }
}
