using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using VRAutism.Cloud.RTDB;
using VRAutism.Core;
using VRAutism.Core.Telemetry;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Integration
{
    /// <summary>Owns the single V2 terminal UI, RTDB ended signal, stream stop, and lobby return.</summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphLifecycleV2 : MonoBehaviour
    {
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private SensorHarvester _sensorHarvester;
        [SerializeField] private TelemetryStreamer _telemetryStreamer;
        [SerializeField] private GameObject _congratulationsRoot;
        [SerializeField] private UnityEvent _onSuccessfulCompletion = new UnityEvent();
        [SerializeField] private string _returnSceneName = "GameMenu";
        [SerializeField] private float _returnDelaySeconds = 3f;

        private readonly HashSet<string> _handledRunIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _endedSessionIds = new HashSet<string>(StringComparer.Ordinal);
        private Coroutine _returnCoroutine;
        private int _lifecycleGeneration;

        private void Awake()
        {
            ResolveReferences();
            if (_congratulationsRoot != null) _congratulationsRoot.SetActive(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_runner == null)
            {
                Debug.LogError("[LessonGraphV2] Lifecycle requires a LessonGraphRunner.", this);
                return;
            }
            _runner.LessonCompleted += OnLessonCompleted;
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.LessonCompleted -= OnLessonCompleted;
            _lifecycleGeneration++;
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }
        }

        private void ResolveReferences()
        {
            if (_runner == null) _runner = GetComponentInParent<LessonGraphRunner>();
            if (_sensorHarvester == null) _sensorHarvester = FindObjectOfType<SensorHarvester>();
            if (_telemetryStreamer == null) _telemetryStreamer = TelemetryStreamer.Instance;
        }

        private void OnLessonCompleted(LessonCompletedEvent completed)
        {
            LessonResult result = completed?.Result;
            LessonStateV2 state = _runner?.CurrentState;
            if (result == null || state == null || string.IsNullOrWhiteSpace(result.RunId) ||
                !string.Equals(result.RunId, state.run_id, StringComparison.Ordinal) ||
                !IsCurrentSession(state.session_id) || _endedSessionIds.Contains(state.session_id) ||
                !_handledRunIds.Add(state.session_id + "\n" + result.RunId))
                return;

            int generation = ++_lifecycleGeneration;
            string sessionId = state.session_id;
            string runId = result.RunId;

            if (_telemetryStreamer == null) _telemetryStreamer = TelemetryStreamer.Instance;
            _telemetryStreamer?.StopStreaming();
            _sensorHarvester?.ClearLessonGraphScope();

            if (result.IsSuccess)
            {
                if (_congratulationsRoot != null) _congratulationsRoot.SetActive(true);
                _onSuccessfulCompletion?.Invoke();
            }

            if (_endedSessionIds.Add(sessionId))
            {
                if (LiveSessionReporter.Instance != null)
                    LiveSessionReporter.Instance.SendLiveSessionEnded(sessionId);
                else
                    Debug.LogWarning("[LessonGraphV2] No LiveSessionReporter is available for the terminal ended signal.", this);
            }

            if (Application.isPlaying)
                _returnCoroutine = StartCoroutine(ReturnToLobbyAfterDelay(sessionId, runId, generation));
        }

        private IEnumerator ReturnToLobbyAfterDelay(string sessionId, string runId, int generation)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, _returnDelaySeconds));
            if (!isActiveAndEnabled || generation != _lifecycleGeneration || !IsCurrentSession(sessionId))
                yield break;

            LessonStateV2 state = _runner?.CurrentState;
            if (state == null || !string.Equals(state.session_id, sessionId, StringComparison.Ordinal) ||
                !string.Equals(state.run_id, runId, StringComparison.Ordinal))
                yield break;

            if (string.IsNullOrWhiteSpace(_returnSceneName))
            {
                Debug.LogError("[LessonGraphV2] Return scene name is empty; staying in the terminal scene.", this);
                yield break;
            }

            SceneManager.LoadScene(_returnSceneName);
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
