using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Questing.Voice
{
    public static class VoiceQuestTransportV2Constants
    {
        public const string Topic = "lesson-graph-v2.voice";
        public const int ContractVersion = 2;
        public const string VerbalHintEvent = "VERBAL_HINT";
        public const string SpeakScriptEvent = "SPEAK_SCRIPT";
        public const string CancelSpeakScriptEvent = "CANCEL_SPEAK_SCRIPT";
        public const float DefaultSpeechSilenceTimeoutSeconds = 5f;
        public const int MaxScriptLength = 500;
    }

    [Serializable]
    public sealed class VoiceQuestActivation
    {
        public string activation_id;
        public string quest_goal;
        public List<string> phrases;
        public string npc_binding_id;
        public float speech_silence_timeout_seconds;

        public VoiceQuestActivation(string activationId, string goal, IEnumerable<string> effectivePhrases, string npcBindingId = "",
            float speechSilenceTimeoutSeconds = VoiceQuestTransportV2Constants.DefaultSpeechSilenceTimeoutSeconds)
        {
            activation_id = activationId ?? string.Empty;
            quest_goal = goal ?? string.Empty;
            phrases = new List<string>(effectivePhrases ?? Array.Empty<string>());
            npc_binding_id = npcBindingId ?? string.Empty;
            speech_silence_timeout_seconds = !float.IsNaN(speechSilenceTimeoutSeconds) && !float.IsInfinity(speechSilenceTimeoutSeconds) && speechSilenceTimeoutSeconds >= 0f
                ? speechSilenceTimeoutSeconds
                : VoiceQuestTransportV2Constants.DefaultSpeechSilenceTimeoutSeconds;
        }
    }

    [Serializable]
    public sealed class VoiceQuestVerbalHint
    {
        public int contract_version = VoiceQuestTransportV2Constants.ContractVersion;
        public string @event = VoiceQuestTransportV2Constants.VerbalHintEvent;
        public string command_id;
        public string activation_id;
        public string npc_binding_id;

        public VoiceQuestVerbalHint(string activationId, string commandId, string npcBindingId)
        {
            activation_id = activationId ?? string.Empty;
            command_id = commandId ?? string.Empty;
            npc_binding_id = npcBindingId ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class VoiceQuestSpeakScriptV2
    {
        public int contract_version = VoiceQuestTransportV2Constants.ContractVersion;
        public string @event = VoiceQuestTransportV2Constants.SpeakScriptEvent;
        public string activation_id;
        public string sequence_id;
        public string npc_binding_id;
        public string text;

        public VoiceQuestSpeakScriptV2(string activationId, string sequenceId, string npcBindingId, string scriptText)
        {
            activation_id = activationId ?? string.Empty;
            sequence_id = sequenceId ?? string.Empty;
            npc_binding_id = npcBindingId ?? string.Empty;
            text = scriptText ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class VoiceQuestCancelSpeakScriptV2
    {
        public int contract_version = VoiceQuestTransportV2Constants.ContractVersion;
        public string @event = VoiceQuestTransportV2Constants.CancelSpeakScriptEvent;
        public string activation_id;
        public string sequence_id;
        public string npc_binding_id;
        public string reason;

        public VoiceQuestCancelSpeakScriptV2(string activationId, string sequenceId, string npcBindingId, string cancellationReason)
        {
            activation_id = activationId ?? string.Empty;
            sequence_id = sequenceId ?? string.Empty;
            npc_binding_id = npcBindingId ?? string.Empty;
            reason = cancellationReason ?? string.Empty;
        }
    }

    public enum VoiceQuestSignalType { Active, Matched, Cancelled, Failed, ReminderAccepted }

    public sealed class VoiceQuestSignal
    {
        public string ActivationId { get; }
        public VoiceQuestSignalType Type { get; }
        public string Reason { get; }
        public string CommandId { get; }
        public string NpcBindingId { get; }

        public VoiceQuestSignal(string activationId, VoiceQuestSignalType type, string reason = "",
            string commandId = "", string npcBindingId = "")
        {
            ActivationId = activationId ?? string.Empty;
            Type = type;
            Reason = reason ?? string.Empty;
            CommandId = commandId ?? string.Empty;
            NpcBindingId = npcBindingId ?? string.Empty;
        }
    }

    public interface IVoiceQuestTransport
    {
        event Action<VoiceQuestSignal> SignalReceived;
        System.Threading.Tasks.Task ActivateAsync(VoiceQuestActivation request, System.Threading.CancellationToken cancellationToken);
        System.Threading.Tasks.Task CancelAsync(string activationId, string reason, System.Threading.CancellationToken cancellationToken);
        System.Threading.Tasks.Task<bool> SendVerbalHintAsync(VoiceQuestVerbalHint request, System.Threading.CancellationToken cancellationToken);
    }
}
