using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions
{
    /// <summary>
    /// Edge condition that matches when the source node's completion status equals the required value.
    /// Valid Phase 1 statuses: "success", "skipped", "timeout", "failed".
    /// </summary>
    [Serializable]
    public sealed class StatusCondition : IEdgeCondition
    {
        public const string Success = "success";
        public const string Skipped = "skipped";
        public const string Timeout = "timeout";
        public const string Failed  = "failed";

        [Tooltip("Node status that activates this edge. One of: success, skipped, timeout, failed.")]
        [SerializeField] private string _requiredStatus = Success;

        public string RequiredStatus => _requiredStatus;

        public StatusCondition(string requiredStatus)
        {
            _requiredStatus = requiredStatus ?? Success;
        }

        public StatusCondition() { }
    }
}
