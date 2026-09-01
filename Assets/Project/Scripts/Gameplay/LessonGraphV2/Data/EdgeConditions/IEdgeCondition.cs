using System;

namespace VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions
{
    /// <summary>
    /// Marker interface for all Phase 1 edge condition types.
    /// Phase 2 types (VariableCondition, CompositeCondition) must implement this interface
    /// but will be rejected by LessonGraphValidator.
    /// </summary>
    public interface IEdgeCondition
    {
    }
}
