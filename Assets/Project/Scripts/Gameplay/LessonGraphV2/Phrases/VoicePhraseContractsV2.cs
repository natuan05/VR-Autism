using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    [Serializable]
    public sealed class VoiceQuestPhraseV2
    {
        public string binding_id;
        public string goal;
        public List<string> default_phrases = new List<string>();
    }

    [Serializable]
    public sealed class VoiceQuestPhraseAdditionV2
    {
        public string binding_id;
        public List<string> phrases = new List<string>();
    }

    public sealed class VoiceQuestPhraseSnapshotV2
    {
        public string BindingId { get; }
        public string Goal { get; }
        public IReadOnlyList<string> Phrases { get; }

        public VoiceQuestPhraseSnapshotV2(string bindingId, string goal, IReadOnlyList<string> phrases)
        {
            BindingId = bindingId ?? string.Empty;
            Goal = goal ?? string.Empty;
            Phrases = phrases ?? Array.Empty<string>();
        }
    }
}
