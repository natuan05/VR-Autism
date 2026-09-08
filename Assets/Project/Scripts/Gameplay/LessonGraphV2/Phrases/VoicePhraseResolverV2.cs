using System;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Phrases
{
    public static class VoicePhraseResolverV2
    {
        public static IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> Resolve(
            IReadOnlyList<VoiceQuestPhraseV2> quests,
            IReadOnlyList<VoiceQuestPhraseAdditionV2> additions)
        {
            var result = new Dictionary<string, VoiceQuestPhraseSnapshotV2>(StringComparer.Ordinal);
            var additionMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var addition in additions ?? Array.Empty<VoiceQuestPhraseAdditionV2>())
            {
                if (addition == null || string.IsNullOrWhiteSpace(addition.binding_id)) continue;
                if (additionMap.ContainsKey(addition.binding_id)) throw new ArgumentException("Duplicate child phrase binding_id.");
                additionMap.Add(addition.binding_id, addition.phrases ?? new List<string>());
            }

            foreach (var quest in quests ?? Array.Empty<VoiceQuestPhraseV2>())
            {
                if (quest == null || string.IsNullOrWhiteSpace(quest.binding_id) || result.ContainsKey(quest.binding_id))
                    throw new ArgumentException("Missing or duplicate lesson binding_id.");
                var phrases = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Add(quest.default_phrases, phrases, seen);
                if (additionMap.TryGetValue(quest.binding_id, out var extra)) Add(extra, phrases, seen);
                result.Add(quest.binding_id, new VoiceQuestPhraseSnapshotV2(quest.binding_id, quest.goal, phrases.AsReadOnly()));
            }
            return result;
        }

        private static void Add(IEnumerable<string> values, ICollection<string> output, ISet<string> seen)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                var phrase = value?.Trim();
                if (string.IsNullOrWhiteSpace(phrase) || phrase.Length > 240 || !seen.Add(phrase)) continue;
                output.Add(phrase);
            }
        }
    }
}
