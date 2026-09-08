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
            Phrases = new List<string>(phrases ?? Array.Empty<string>()).AsReadOnly();
        }
    }

    [Serializable]
    public sealed class VoicePhraseSessionSnapshotV2
    {
        public const int ContractVersion = 2;
        public int contract_version = ContractVersion;
        public string launch_token;
        public string lesson_id;
        public int voice_schema_version = 2;
        public int lesson_voice_revision;
        public int child_phrase_revision;
        public IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> quests;

        public VoicePhraseSessionSnapshotV2(string launchToken, string lessonId, int lessonRevision,
            int childRevision, IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> resolved)
        {
            launch_token = launchToken ?? string.Empty;
            lesson_id = lessonId ?? string.Empty;
            lesson_voice_revision = lessonRevision;
            child_phrase_revision = childRevision;
            quests = new Dictionary<string, VoiceQuestPhraseSnapshotV2>(resolved ??
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>(), StringComparer.Ordinal);
        }
    }
}
