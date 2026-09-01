using System;

namespace VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions
{
    /// <summary>
    /// Edge condition that always evaluates to true. Default condition for unconditional transitions.
    /// </summary>
    [Serializable]
    public sealed class AlwaysCondition : IEdgeCondition
    {
    }
}
