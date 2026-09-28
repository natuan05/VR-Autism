using System;
using UnityEngine;
using UnityEngine.Playables;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    public enum TimelineTimeoutOutcome { Timeout, Failed }

    [Serializable]
    public sealed class TimelineNodeConfig : INodeConfig
    {
        [SerializeField] private PlayableAsset _timelineAsset;
        [SerializeField] private string _expectedSignalName = string.Empty;
        [SerializeField] private float _timeoutSeconds = 30f;
        [SerializeField] private TimelineTimeoutOutcome _timeoutOutcome = TimelineTimeoutOutcome.Timeout;

        public PlayableAsset TimelineAsset => _timelineAsset;
        public string ExpectedSignalName => _expectedSignalName;
        public float TimeoutSeconds => _timeoutSeconds;
        public TimelineTimeoutOutcome TimeoutOutcome => _timeoutOutcome;

        public TimelineNodeConfig(PlayableAsset timelineAsset, string expectedSignalName, float timeoutSeconds,
            TimelineTimeoutOutcome timeoutOutcome)
        {
            _timelineAsset = timelineAsset;
            _expectedSignalName = expectedSignalName ?? string.Empty;
            _timeoutSeconds = timeoutSeconds;
            _timeoutOutcome = timeoutOutcome;
        }

        public TimelineNodeConfig() { }
    }
}
