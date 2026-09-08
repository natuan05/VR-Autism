using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    public sealed class FirestoreVoicePhraseLoaderV2
    {
        public IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> ResolveSessionSnapshot(
            IReadOnlyList<VoiceQuestPhraseV2> lessonQuests,
            IReadOnlyList<VoiceQuestPhraseAdditionV2> childAdditions)
        {
            var snapshot = VoicePhraseResolverV2.Resolve(lessonQuests, childAdditions);
            VoicePhraseSnapshotStoreV2.Replace(snapshot);
            return snapshot;
        }

        public VoicePhraseSessionSnapshotV2 ResolveSessionSnapshot(
            string launchToken, string lessonId, int lessonVoiceRevision, int childPhraseRevision,
            IReadOnlyList<VoiceQuestPhraseV2> lessonQuests,
            IReadOnlyList<VoiceQuestPhraseAdditionV2> childAdditions)
        {
            var resolved = VoicePhraseResolverV2.Resolve(lessonQuests, childAdditions);
            var snapshot = new VoicePhraseSessionSnapshotV2(launchToken, lessonId, lessonVoiceRevision,
                childPhraseRevision, resolved);
            VoicePhraseSnapshotStoreV2.Replace(snapshot);
            return snapshot;
        }
    }
}