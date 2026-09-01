using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    /// <summary>
    /// Phase 1 wait node config. Pauses graph execution for a fixed duration.
    /// </summary>
    [Serializable]
    public sealed class WaitNodeConfig : INodeConfig
    {
        [Tooltip("Duration in seconds to wait before node completes.")]
        [SerializeField] private float _duration = 1f;

        public float Duration => _duration;

        public WaitNodeConfig(float duration)
        {
            _duration = duration;
        }

        public WaitNodeConfig() { }
    }
}
