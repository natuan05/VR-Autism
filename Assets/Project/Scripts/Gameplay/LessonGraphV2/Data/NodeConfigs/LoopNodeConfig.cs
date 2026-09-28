using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    [System.Serializable]
    public sealed class LoopNodeConfig : INodeConfig
    {
        [SerializeField] private string _bodyChildNodeId = string.Empty;
        [SerializeField] private string _exitNodeId = string.Empty;
        [SerializeField] private int _maximumIterations = 1;
        [SerializeReference] private IEdgeCondition _exitCondition;
        public string BodyChildNodeId => _bodyChildNodeId;
        public string ExitNodeId => _exitNodeId;
        public int MaximumIterations => _maximumIterations;
        public IEdgeCondition ExitCondition => _exitCondition;
        public LoopNodeConfig(string bodyChildNodeId, string exitNodeId, int maximumIterations, IEdgeCondition exitCondition)
        {
            _bodyChildNodeId = bodyChildNodeId ?? string.Empty;
            _exitNodeId = exitNodeId ?? string.Empty;
            _maximumIterations = maximumIterations;
            _exitCondition = exitCondition;
        }
        public LoopNodeConfig() { }
    }
}
