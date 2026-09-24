using System;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public sealed class LessonSessionContextV2
    {
        public string SessionId { get; }
        public string LessonId { get; }
        public string LaunchToken { get; }
        public int LessonVoiceRevision { get; }
        public int ChildPhraseRevision { get; }

        public LessonSessionContextV2(string sessionId, string lessonId, string launchToken, int lessonVoiceRevision, int childPhraseRevision)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || ContainsFirebasePathKeyCharacter(sessionId)) throw new ArgumentException("A safe session ID is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(lessonId)) throw new ArgumentException("A lesson ID is required.", nameof(lessonId));
            if (string.IsNullOrWhiteSpace(launchToken)) throw new ArgumentException("A launch token is required.", nameof(launchToken));
            if (lessonVoiceRevision < 0) throw new ArgumentOutOfRangeException(nameof(lessonVoiceRevision));
            if (childPhraseRevision < 0) throw new ArgumentOutOfRangeException(nameof(childPhraseRevision));
            SessionId = sessionId;
            LessonId = lessonId;
            LaunchToken = launchToken;
            LessonVoiceRevision = lessonVoiceRevision;
            ChildPhraseRevision = childPhraseRevision;
        }

        private static bool ContainsFirebasePathKeyCharacter(string value) => value.IndexOfAny(new[] { '/', '.', '#', '$', '[', ']' }) >= 0;
    }
}
