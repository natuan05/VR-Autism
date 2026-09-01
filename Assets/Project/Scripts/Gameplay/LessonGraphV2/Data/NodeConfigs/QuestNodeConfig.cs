using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    /// <summary>
    /// Phase 1 quest node config. Stores only stable binding IDs, timeout, and voice prompt.
    /// No legacy Quest, questPrefab, channel enum, or scene MonoBehaviour references.
    /// Binding IDs are resolved at runtime by LessonGraphBindings in the scene.
    /// </summary>
    [Serializable]
    public sealed class QuestNodeConfig : INodeConfig
    {
        [Tooltip("Unique binding IDs that map to QuestSourceV2 components in the scene.")]
        [SerializeField] private List<string> _completionBindingIds = new List<string>();

        [Tooltip("Seconds before node times out. Use -1 for no timeout.")]
        [SerializeField] private float _timeoutSeconds = -1f;

        [Tooltip("Optional voice prompt sent with SET_ACTIVE_QUEST to the voice agent.")]
        [SerializeField] private string _voicePrompt = string.Empty;

        public IReadOnlyList<string> CompletionBindingIds => _completionBindingIds;
        public float TimeoutSeconds => _timeoutSeconds;
        public string VoicePrompt => _voicePrompt;

        // For test construction only — runtime code uses Inspector-populated data.
        public QuestNodeConfig(List<string> bindingIds, float timeoutSeconds = -1f, string voicePrompt = "")
        {
            _completionBindingIds = bindingIds ?? new List<string>();
            _timeoutSeconds = timeoutSeconds;
            _voicePrompt = voicePrompt ?? string.Empty;
        }

        public QuestNodeConfig() { }
    }
}
