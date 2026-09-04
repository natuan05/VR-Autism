using System;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Inspector-friendly composition bridge for a LessonGraph V2 runner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphRunnerInstaller : MonoBehaviour
    {
        [SerializeField] private LessonGraph _lessonGraph;
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private bool _startOnStart;

        private INodeClock _clock;

        public LessonGraph LessonGraph => _lessonGraph;
        public LessonGraphRunner Runner => _runner;
        public LessonGraphBindings Bindings => _bindings;

        private void Awake()
        {
            if (_runner == null) _runner = GetComponent<LessonGraphRunner>();
            if (_bindings == null) _bindings = GetComponent<LessonGraphBindings>();
            Configure();
        }

        private async void Start()
        {
            if (!_startOnStart) return;
            Debug.Log($"[LessonGraphV2] Installer auto-starting lesson graph='{_lessonGraph.name}'", this);
            try
            {
                var result = await _runner.StartLessonAsync();
                if (result.IsSuccess)
                    Debug.Log($"[LessonGraphV2] Lesson finished successfully run={result.RunId}", this);
                else
                    Debug.LogWarning($"[LessonGraphV2] Lesson finished with failure reason={result.FailureReason} run={result.RunId}", this);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LessonGraphV2] Lesson start exception: {ex}", this);
            }
        }

        public void Configure()
        {
            if (_lessonGraph == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraph asset.");
            if (_runner == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphRunner component.");
            if (_bindings == null) throw new InvalidOperationException("LessonGraphRunnerInstaller needs a LessonGraphBindings component.");

            _clock = new MonotonicClock();
            _runner.Configure(
                _lessonGraph,
                new LessonGraphExecutorRegistry(_bindings, _clock),
                _bindings,
                _clock);
            Debug.Log($"[LessonGraphV2] Installer configured: graph='{_lessonGraph.name}' runner={_runner.name} bindings={_bindings.name}", this);
        }
    }
}
