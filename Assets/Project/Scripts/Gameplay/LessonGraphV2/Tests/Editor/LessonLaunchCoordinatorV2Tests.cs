using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonLaunchCoordinatorV2Tests
    {
        private static readonly MethodInfo ResetCoordinator = typeof(LessonLaunchCoordinatorV2)
            .GetMethod("ResetForTests", BindingFlags.Static | BindingFlags.NonPublic);
        private SessionContext _previousSession;
        private GameObject _sessionObject;

        [SetUp]
        public void SetUp()
        {
            foreach (var coordinator in Resources.FindObjectsOfTypeAll<LessonLaunchCoordinatorV2>())
                if (coordinator != null) Object.DestroyImmediate(coordinator.gameObject);
            ResetCoordinator.Invoke(null, null);
            _previousSession = SessionContext.Instance;
            _sessionObject = new GameObject("LessonLaunchCoordinatorTestSession");
            _sessionObject.SetActive(false);
            SessionContext.Instance = _sessionObject.AddComponent<SessionContext>();
            VoicePhraseSnapshotStoreV2.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var coordinator in Resources.FindObjectsOfTypeAll<LessonLaunchCoordinatorV2>())
                if (coordinator != null) Object.DestroyImmediate(coordinator.gameObject);
            ResetCoordinator.Invoke(null, null);
            VoicePhraseSnapshotStoreV2.Clear();
            if (_sessionObject != null) Object.DestroyImmediate(_sessionObject);
            SessionContext.Instance = _previousSession;
        }

        [Test]
        public void BeginLaunchRejectsDuplicatesAndCancelledAttemptsBecomeStale()
        {
            Assert.That(LessonLaunchCoordinatorV2.TryBeginLaunch("session-1", "lesson-1",
                "test-scene", "launch-1", out var first), Is.True);
            Assert.That(LessonLaunchCoordinatorV2.TryBeginLaunch("session-1", "lesson-1",
                "test-scene", "launch-2", out var duplicate), Is.False);
            Assert.That(duplicate, Is.Null);

            LessonLaunchCoordinatorV2.CancelAttempt(first);

            Assert.That(LessonLaunchCoordinatorV2.IsCurrentAttempt(first), Is.False);
            Assert.That(LessonLaunchCoordinatorV2.TryBeginLaunch("session-2", "lesson-2",
                "test-scene", "launch-3", out var next), Is.True);
            LessonLaunchCoordinatorV2.CancelAttempt(next);
        }

        [Test]
        public void AcceptLaunchRequiresMatchingSnapshotAndDoesNotMutateSessionBeforeAcceptance()
        {
            Assert.That(LessonLaunchCoordinatorV2.TryBeginLaunch("session-1", "lesson-1",
                "test-scene", "launch-1", out var attempt), Is.True);
            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "stale-token", "lesson-1", 7, 9, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));

            Assert.That(LessonLaunchCoordinatorV2.TryAcceptLaunch(attempt, "child-1", "host-1",
                out var rejected, out _), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt), Is.True);

            VoicePhraseSnapshotStoreV2.Replace(new VoicePhraseSessionSnapshotV2(
                "launch-1", "lesson-1", 7, 9, new Dictionary<string, VoiceQuestPhraseSnapshotV2>()));

            Assert.That(LessonLaunchCoordinatorV2.TryAcceptLaunch(attempt, "child-1", "host-1",
                out var accepted, out var reason), Is.True, reason);
            Assert.That(accepted.SessionId, Is.EqualTo("session-1"));
            Assert.That(accepted.LessonId, Is.EqualTo("lesson-1"));
            Assert.That(accepted.LaunchToken, Is.EqualTo("launch-1"));
            Assert.That(accepted.LessonVoiceRevision, Is.EqualTo(7));
            Assert.That(accepted.ChildPhraseRevision, Is.EqualTo(9));
            Assert.That(SessionContext.Instance.SessionId, Is.Empty,
                "Session fields are committed by the menu only after the launch contract accepts.");
            Assert.That(LessonLaunchCoordinatorV2.TryBeginLaunch("session-2", "lesson-2",
                "test-scene", "launch-2", out _), Is.False);

            LessonLaunchCoordinatorV2.CancelAcceptedLaunch(accepted);
            Assert.That(LessonLaunchCoordinatorV2.AcceptedLaunch, Is.Null);
        }
    }
}
