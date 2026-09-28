using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    public enum ParallelJoinPolicy { AllSuccess, FirstCompleted }

    [Serializable]
    public sealed class ParallelBranch
    {
        [SerializeField] private string _branchId = string.Empty;
        [SerializeField] private string _childNodeId = string.Empty;
        public string BranchId => _branchId;
        public string ChildNodeId => _childNodeId;
        public ParallelBranch(string branchId, string childNodeId)
        {
            _branchId = branchId ?? string.Empty;
            _childNodeId = childNodeId ?? string.Empty;
        }
        public ParallelBranch() { }
    }

    [Serializable]
    public sealed class ParallelNodeConfig : INodeConfig
    {
        [SerializeField] private List<ParallelBranch> _branches = new List<ParallelBranch>();
        [SerializeField] private string _gateNodeId = string.Empty;
        [SerializeField] private ParallelJoinPolicy _joinPolicy = ParallelJoinPolicy.AllSuccess;
        public IReadOnlyList<ParallelBranch> Branches => _branches;
        public string GateNodeId => _gateNodeId;
        public ParallelJoinPolicy JoinPolicy => _joinPolicy;
        public ParallelNodeConfig(IReadOnlyList<ParallelBranch> branches, string gateNodeId, ParallelJoinPolicy joinPolicy)
        {
            _branches = branches == null ? new List<ParallelBranch>() : new List<ParallelBranch>(branches);
            _gateNodeId = gateNodeId ?? string.Empty;
            _joinPolicy = joinPolicy;
        }
        public ParallelNodeConfig() { }
    }
}
