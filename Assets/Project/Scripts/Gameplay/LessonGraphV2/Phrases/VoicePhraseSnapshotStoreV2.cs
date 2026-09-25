using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    public sealed class VoicePhraseSessionMetadataV2
    {
        public string LaunchToken { get; }
        public string LessonId { get; }
        public int LessonVoiceRevision { get; }
        public int ChildPhraseRevision { get; }

        internal VoicePhraseSessionMetadataV2(string launchToken, string lessonId, int lessonVoiceRevision, int childPhraseRevision)
        {
            LaunchToken = launchToken;
            LessonId = lessonId;
            LessonVoiceRevision = lessonVoiceRevision;
            ChildPhraseRevision = childPhraseRevision;
        }
    }

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

        public static bool TryGetSessionMetadata(out VoicePhraseSessionMetadataV2 metadata)
        {
            metadata = null;
            if (!IsValid || _session.lesson_voice_revision < 0 || _session.child_phrase_revision < 0)
                return false;

            metadata = new VoicePhraseSessionMetadataV2(
                _session.launch_token,
                _session.lesson_id,
                _session.lesson_voice_revision,
                _session.child_phrase_revision);
            return true;
        }

        public static bool TryGet(string bindingId, out VoiceQuestPhraseSnapshotV2 snapshot)
        {
            snapshot = null;
            return _session?.quests != null && _session.quests.TryGetValue(bindingId ?? string.Empty, out snapshot);
        }

        public static void Clear() => _session = null;
    }
}
