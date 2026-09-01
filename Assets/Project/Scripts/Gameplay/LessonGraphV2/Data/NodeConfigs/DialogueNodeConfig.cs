using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs
{
    /// <summary>
    /// Phase 1 dialogue node config. Sends a single SPEAK_SCRIPT DataPacket via LiveKit.
    /// No scene MonoBehaviour references.
    /// </summary>
    [Serializable]
    public sealed class DialogueNodeConfig : INodeConfig
    {
        [Tooltip("Correlation ID echoed back in SPEAK_SCRIPT_DONE. Must be unique per lesson.")]
        [SerializeField] private string _sequenceId = string.Empty;

        [Tooltip("Text sent to the voice agent via SPEAK_SCRIPT DataPacket.")]
        [SerializeField] private string _text = string.Empty;

        [Tooltip("When true, node waits for SPEAK_SCRIPT_DONE before completing.")]
        [SerializeField] private bool _blocking = true;

        [Tooltip("Seconds before node times out if SPEAK_SCRIPT_DONE is not received. Use -1 for no timeout.")]
        [SerializeField] private float _timeoutSeconds = 30f;

        public string SequenceId => _sequenceId;
        public string Text => _text;
        public bool Blocking => _blocking;
        public float TimeoutSeconds => _timeoutSeconds;

        public DialogueNodeConfig(string sequenceId, string text, bool blocking = true, float timeoutSeconds = 30f)
        {
            _sequenceId = sequenceId ?? string.Empty;
            _text = text ?? string.Empty;
            _blocking = blocking;
            _timeoutSeconds = timeoutSeconds;
        }

        public DialogueNodeConfig() { }
    }
}
