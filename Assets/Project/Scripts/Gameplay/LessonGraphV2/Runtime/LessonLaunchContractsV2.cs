using System;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>Identity for one asynchronous request received by the existing lesson menu.</summary>
    public sealed class LessonLaunchAttemptV2
    {
        internal string AttemptId { get; }
        public string SessionId { get; }
        public string LessonId { get; }
        public string SceneName { get; }
        public string LaunchToken { get; }

        internal LessonLaunchAttemptV2(string attemptId, string sessionId, string lessonId,
            string sceneName, string launchToken)
        {
            AttemptId = attemptId;
            SessionId = sessionId;
            LessonId = lessonId;
            SceneName = sceneName;
            LaunchToken = launchToken;
        }
    }

    /// <summary>Immutable launch accepted after the matching phrase snapshot is available.</summary>
    public sealed class AcceptedLessonLaunchV2
    {
        public string SessionId { get; }
        public string LessonId { get; }
        public string SceneName { get; }
        public string LaunchToken { get; }
        public string ChildId { get; }
        public string HostId { get; }
        public int LessonVoiceRevision { get; }
        public int ChildPhraseRevision { get; }

        internal AcceptedLessonLaunchV2(LessonLaunchAttemptV2 attempt, string childId, string hostId,
            int lessonVoiceRevision, int childPhraseRevision)
        {
            SessionId = attempt.SessionId;
            LessonId = attempt.LessonId;
            SceneName = attempt.SceneName;
            LaunchToken = attempt.LaunchToken;
            ChildId = childId ?? string.Empty;
            HostId = hostId ?? string.Empty;
            LessonVoiceRevision = lessonVoiceRevision;
            ChildPhraseRevision = childPhraseRevision;
        }

        public LessonSessionContextV2 CreateSessionContext() => new LessonSessionContextV2(
            SessionId, LessonId, LaunchToken, LessonVoiceRevision, ChildPhraseRevision);

        public bool HasIdentity(string sessionId, string lessonId, string launchToken) =>
            string.Equals(SessionId, sessionId, StringComparison.Ordinal) &&
            string.Equals(LessonId, lessonId, StringComparison.Ordinal) &&
            string.Equals(LaunchToken, launchToken, StringComparison.Ordinal);
    }

    /// <summary>Explicit authority required by every LessonGraphRunner start.</summary>
    public interface ILessonRunnerStartOwnerV2
    {
        bool IsAvailable { get; }
        bool CanAuthorizeLessonStart(out string reason);
        void MarkLessonStartRequested();
    }
}
