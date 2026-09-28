using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    public enum GateConditionMode { And, Or }

    [System.Serializable]
    public sealed class GateNodeConfig : INodeConfig
    {
        [SerializeField] private string _parallelNodeId = string.Empty;
        [SerializeField] private List<string> _completedBranchIds = new List<string>();
        [SerializeField] private GateConditionMode _mode = GateConditionMode.And;
        public string ParallelNodeId => _parallelNodeId;
        public IReadOnlyList<string> CompletedBranchIds => _completedBranchIds;
        public GateConditionMode Mode => _mode;
        public GateNodeConfig(string parallelNodeId, IReadOnlyList<string> completedBranchIds, GateConditionMode mode)
        {
            _parallelNodeId = parallelNodeId ?? string.Empty;
            _completedBranchIds = completedBranchIds == null ? new List<string>() : new List<string>(completedBranchIds);
            _mode = mode;
        }
        public GateNodeConfig() { }
    }
}
