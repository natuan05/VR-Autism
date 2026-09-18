using System;
using System.Threading;
using System.Threading.Tasks;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue
{
    public static class DialogueTransportV2Constants
    {
        public const string Topic = "lesson-graph-v2.voice";
        public const int ContractVersion = 2;
    }

    [Serializable]
    public sealed class DialogueRequestV2
    {
        public string activation_id;
        public string sequence_id;
        public string text;
        public string npc_binding_id;

        public DialogueRequestV2(string activationId, string sequenceId, string text, string npcBindingId)
        {
            activation_id = activationId ?? string.Empty;
            sequence_id = sequenceId ?? string.Empty;
            text = text ?? string.Empty;
            npc_binding_id = npcBindingId ?? string.Empty;
        }
    }

    public enum DialogueSignalType
    {
        Done,
        Cancelled,
        Failed
    }

    public sealed class DialogueSignalV2
    {
        public string ActivationId { get; }
        public string SequenceId { get; }
        public string NpcBindingId { get; }
        public DialogueSignalType Type { get; }
        public string Reason { get; }

        public DialogueSignalV2(string activationId, string sequenceId, string npcBindingId, DialogueSignalType type, string reason = "")
        {
            ActivationId = activationId ?? string.Empty;
            SequenceId = sequenceId ?? string.Empty;
            NpcBindingId = npcBindingId ?? string.Empty;
            Type = type;
            Reason = reason ?? string.Empty;
        }
    }

    public interface IDialogueTransportV2
    {
        event Action<DialogueSignalV2> SignalReceived;
        Task SpeakAsync(DialogueRequestV2 request, CancellationToken cancellationToken);
        Task CancelAsync(string activationId, string sequenceId, string reason, CancellationToken cancellationToken);
    }
}
