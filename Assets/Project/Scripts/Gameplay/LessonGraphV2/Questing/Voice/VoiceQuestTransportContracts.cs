using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Questing.Voice
{
    public static class VoiceQuestTransportV2Constants
    {
        public const string Topic = "lesson-graph-v2.voice";
        public const int ContractVersion = 2;
    }

    [Serializable]
    public sealed class VoiceQuestActivation
    {
        public string activation_id;
        public string quest_goal;
        public List<string> phrases;

        public VoiceQuestActivation(string activationId, string goal, IEnumerable<string> effectivePhrases)
        {
            activation_id = activationId ?? string.Empty;
            quest_goal = goal ?? string.Empty;
            phrases = new List<string>(effectivePhrases ?? Array.Empty<string>());
        }
    }

    public enum VoiceQuestSignalType { Active, Matched, Cancelled, Failed }

    public sealed class VoiceQuestSignal
    {
        public string ActivationId { get; }
        public VoiceQuestSignalType Type { get; }
        public string Reason { get; }

        public VoiceQuestSignal(string activationId, VoiceQuestSignalType type, string reason = "")
        {
            ActivationId = activationId ?? string.Empty;
            Type = type;
            Reason = reason ?? string.Empty;
        }
    }

    public interface IVoiceQuestTransport
    {
        event Action<VoiceQuestSignal> SignalReceived;
        System.Threading.Tasks.Task ActivateAsync(VoiceQuestActivation request, System.Threading.CancellationToken cancellationToken);
        System.Threading.Tasks.Task CancelAsync(string activationId, string reason, System.Threading.CancellationToken cancellationToken);
    }
}
