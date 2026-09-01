using System;

namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>
    /// Marker interface for all Phase 1 node config types.
    /// Implemented by Serializable classes that use [SerializeReference] polymorphism.
    /// Do NOT derive from UnityEngine.Object.
    /// </summary>
    public interface INodeConfig
    {
    }
}
