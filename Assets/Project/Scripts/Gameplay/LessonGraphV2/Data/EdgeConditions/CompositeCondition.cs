using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions
{
    public enum CompositeConditionOperator { And, Or }

    [System.Serializable]
    public sealed class CompositeCondition : IEdgeCondition
    {
        [SerializeField] private CompositeConditionOperator _operator;
        [SerializeReference] private List<IEdgeCondition> _conditions = new List<IEdgeCondition>();
        public CompositeConditionOperator Operator => _operator;
        public IReadOnlyList<IEdgeCondition> Conditions => _conditions;
        public CompositeCondition(CompositeConditionOperator op, IReadOnlyList<IEdgeCondition> conditions)
        {
            _operator = op;
            _conditions = conditions == null ? new List<IEdgeCondition>() : new List<IEdgeCondition>(conditions);
        }
        public CompositeCondition() { }
    }
}
