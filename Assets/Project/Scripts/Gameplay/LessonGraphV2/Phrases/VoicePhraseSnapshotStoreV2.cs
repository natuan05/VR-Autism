using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    public static class VoicePhraseSnapshotStoreV2
    {
        private static VoicePhraseSessionSnapshotV2 _session;

        public static string LaunchToken => _session?.launch_token ?? string.Empty;
        public static bool IsValid => _session != null && _session.contract_version == 2 &&
            !string.IsNullOrWhiteSpace(_session.launch_token) && !string.IsNullOrWhiteSpace(_session.lesson_id);

        public static void Replace(IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> snapshot)
        {
            Replace(new VoicePhraseSessionSnapshotV2(Guid.NewGuid().ToString("N"), "", 0, 0, snapshot));
        }

        public static void Replace(VoicePhraseSessionSnapshotV2 snapshot)
        {
            _session = snapshot;
        }

        public static bool TryGet(string bindingId, out VoiceQuestPhraseSnapshotV2 snapshot)
        {
            snapshot = null;
            return _session?.quests != null && _session.quests.TryGetValue(bindingId ?? string.Empty, out snapshot);
        }

        public static void Clear() => _session = null;
    }
}