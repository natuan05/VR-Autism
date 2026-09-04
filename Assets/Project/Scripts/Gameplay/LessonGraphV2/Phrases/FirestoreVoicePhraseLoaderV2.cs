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
    }
}
