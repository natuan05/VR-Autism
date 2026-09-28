using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    /// <summary>
    /// Phase 1 checkpoint node config. Acts as a telemetry marker.
    /// Resume from checkpoint is a Phase 2 feature.
    /// </summary>
    [Serializable]
    public sealed class CheckpointNodeConfig : INodeConfig
    {
        [Tooltip("Stable identifier for this checkpoint. Used in telemetry and future resume.")]
        [SerializeField] private string _checkpointId = string.Empty;

        [Tooltip("When true, emits a NodeLogData telemetry event upon reaching this checkpoint.")]
        [SerializeField] private bool _emitTelemetry = true;

        [Tooltip("Stable compatibility key used to determine whether a saved checkpoint can resume this graph.")]
        [SerializeField] private string _resumeCompatibilityKey = string.Empty;

        public string CheckpointId => _checkpointId;
        public bool EmitTelemetry => _emitTelemetry;
        public string ResumeCompatibilityKey => _resumeCompatibilityKey;

        public CheckpointNodeConfig(string checkpointId, bool emitTelemetry = true)
        {
            _checkpointId = checkpointId ?? string.Empty;
            _emitTelemetry = emitTelemetry;
        }

        public CheckpointNodeConfig(string checkpointId, bool emitTelemetry, string resumeCompatibilityKey)
            : this(checkpointId, emitTelemetry)
        {
            _resumeCompatibilityKey = resumeCompatibilityKey ?? string.Empty;
        }

        public CheckpointNodeConfig() { }
    }
}
