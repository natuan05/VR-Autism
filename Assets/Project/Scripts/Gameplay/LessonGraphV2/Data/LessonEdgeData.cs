using System;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>
    /// Serializable data for a directed edge between two nodes in a LessonGraph.
    /// Lower priority value = evaluated first during transition resolution.
    /// Condition is polymorphic via SerializeReference (Phase 1: AlwaysCondition or StatusCondition only).
    /// </summary>
    [Serializable]
    public sealed class LessonEdgeData
    {
        [Tooltip("ID of the source node.")]
        [SerializeField] private string _fromNodeId = string.Empty;

        [Tooltip("ID of the destination node. Must exist in the graph.")]
        [SerializeField] private string _toNodeId = string.Empty;

        [Tooltip("Lower value = evaluated first. Ties broken by list order.")]
        [SerializeField] private int _priority = 0;

        [Tooltip("Condition controlling this edge. Phase 1: AlwaysCondition or StatusCondition only.")]
        [SerializeReference] private IEdgeCondition _condition;

        public string FromNodeId => _fromNodeId;
        public string ToNodeId => _toNodeId;
        public int Priority => _priority;
        public IEdgeCondition Condition => _condition;

        public LessonEdgeData(string fromNodeId, string toNodeId, IEdgeCondition condition, int priority = 0)
        {
            _fromNodeId = fromNodeId ?? string.Empty;
            _toNodeId = toNodeId ?? string.Empty;
            _condition = condition;
            _priority = priority;
        }

        public LessonEdgeData() { }
    }
}
