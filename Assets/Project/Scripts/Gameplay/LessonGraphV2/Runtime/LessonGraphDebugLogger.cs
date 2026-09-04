using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>Optional Console logger for validating LessonGraph V2 scene wiring.</summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphDebugLogger : MonoBehaviour
    {
        [SerializeField] private LessonGraphRunner _runner;

        private void Awake()
        {
            if (_runner == null) _runner = GetComponent<LessonGraphRunner>();
            if (_runner == null)
                Debug.LogError($"[LessonGraphV2] DebugLogger: no LessonGraphRunner found!", this);
            else
                Debug.Log($"[LessonGraphV2] DebugLogger attached to runner='{_runner.name}'", this);
        }

        private void OnEnable()
        {
            if (_runner == null) return;
            _runner.NodeEntered += OnNodeEntered;
            _runner.NodeCompleted += OnNodeCompleted;
            _runner.LessonCompleted += OnLessonCompleted;
        }

        private void OnDisable()
        {
            if (_runner == null) return;
            _runner.NodeEntered -= OnNodeEntered;
            _runner.NodeCompleted -= OnNodeCompleted;
            _runner.LessonCompleted -= OnLessonCompleted;
        }

        private static void OnNodeEntered(NodeEnteredEvent entry) =>
            Debug.Log($"[LessonGraphV2] ENTER node={entry.NodeId} activation={entry.ActivationId}");

        private static void OnNodeCompleted(NodeCompletedEvent completed) =>
            Debug.Log($"[LessonGraphV2] COMPLETE node={completed.Result.NodeId} status={completed.Result.Status} channel={completed.Result.CompletionChannel}");

        private static void OnLessonCompleted(LessonCompletedEvent completed)
        {
            if (completed.Result.IsSuccess)
                Debug.Log($"[LessonGraphV2] LESSON COMPLETE success=true final={completed.Result.FinalNodeResult?.NodeId} status={completed.Result.FinalNodeResult?.Status}");
            else
                Debug.LogWarning($"[LessonGraphV2] LESSON COMPLETE success=false reason={completed.Result.FailureReason} final={completed.Result.FinalNodeResult?.NodeId}");
        }
    }
}
