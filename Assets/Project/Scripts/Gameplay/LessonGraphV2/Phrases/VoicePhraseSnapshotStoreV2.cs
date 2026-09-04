using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    public static class VoicePhraseSnapshotStoreV2
    {
        private static IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> _snapshot =
            new Dictionary<string, VoiceQuestPhraseSnapshotV2>(StringComparer.Ordinal);

        public static void Replace(IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> snapshot)
        {
            _snapshot = new Dictionary<string, VoiceQuestPhraseSnapshotV2>(snapshot ??
                new Dictionary<string, VoiceQuestPhraseSnapshotV2>(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        public static bool TryGet(string bindingId, out VoiceQuestPhraseSnapshotV2 snapshot) =>
            _snapshot.TryGetValue(bindingId ?? string.Empty, out snapshot);

        public static void Clear() => Replace(null);
    }
}
