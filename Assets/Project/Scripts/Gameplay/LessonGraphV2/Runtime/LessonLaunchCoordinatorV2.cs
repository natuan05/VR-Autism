using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.AudioCache;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Persistent application-side authority for accepted launches. It observes the existing
    /// scene load and hands one immutable request to that scene's explicit definition.
    /// </summary>
    public sealed class LessonLaunchCoordinatorV2 : MonoBehaviour
    {
        private static LessonLaunchCoordinatorV2 _instance;
        private static LessonLaunchAttemptV2 _pendingAttempt;
        private static AcceptedLessonLaunchV2 _acceptedLaunch;
        private static string _dispatchedLaunchToken;

        private LessonGraphRuntimeOwnerV2 _activeOwner;

        public static AcceptedLessonLaunchV2 AcceptedLaunch => _acceptedLaunch;

        public static bool TryBeginLaunch(string sessionId, string lessonId, string sceneName,
            string launchToken, out LessonLaunchAttemptV2 attempt)
        {
            attempt = null;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(lessonId) ||
                string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(launchToken) ||
                _pendingAttempt != null || _acceptedLaunch != null)
                return false;

            attempt = new LessonLaunchAttemptV2(Guid.NewGuid().ToString("N"), sessionId, lessonId,
                sceneName, launchToken);
            _pendingAttempt = attempt;
            return true;
        }

        public static bool IsCurrentAttempt(LessonLaunchAttemptV2 attempt) =>
            attempt != null && _pendingAttempt != null &&
            string.Equals(attempt.AttemptId, _pendingAttempt.AttemptId, StringComparison.Ordinal);

        public static bool TryAcceptLaunch(LessonLaunchAttemptV2 attempt, string childId, string hostId,
            out AcceptedLessonLaunchV2 accepted, out string reason, SessionTtsSnapshotV1 ttsSnapshot = null)
        {
            accepted = null;
            reason = null;
            if (!IsCurrentAttempt(attempt))
            {
                reason = "The lesson launch request is stale.";
                return false;
            }

            SessionContext session = SessionContext.Instance;
            if (session == null)
            {
                reason = "The active session context is unavailable.";
                return false;
            }

            if (ttsSnapshot != null &&
                (!string.Equals(ttsSnapshot.Scope.session_id, attempt.SessionId, StringComparison.Ordinal) ||
                 !string.Equals(ttsSnapshot.Scope.lesson_id, attempt.LessonId, StringComparison.Ordinal)))
            {
                reason = "The retained TTS snapshot does not match the launched session and lesson.";
                return false;
            }

            if (!VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out var phraseMetadata) ||
                !string.Equals(phraseMetadata.LaunchToken, attempt.LaunchToken, StringComparison.Ordinal) ||
                !string.Equals(phraseMetadata.LessonId, attempt.LessonId, StringComparison.Ordinal))
            {
                reason = "The matching immutable V2 phrase snapshot is unavailable.";
                return false;
            }

            try
            {
                accepted = new AcceptedLessonLaunchV2(attempt, childId, hostId,
                    phraseMetadata.LessonVoiceRevision, phraseMetadata.ChildPhraseRevision, ttsSnapshot);
            }
            catch (ArgumentException exception)
            {
                reason = exception.Message;
                accepted = null;
                return false;
            }

            _acceptedLaunch = accepted;
            _pendingAttempt = null;
            EnsurePersistentCoordinator();
            return true;
        }

        public static void CancelAttempt(LessonLaunchAttemptV2 attempt)
        {
            if (IsCurrentAttempt(attempt)) _pendingAttempt = null;
        }

        public static void CancelAcceptedLaunch(AcceptedLessonLaunchV2 launch)
        {
            if (ReferenceEquals(_acceptedLaunch, launch) && (_instance == null || _instance._activeOwner == null))
            {
                _acceptedLaunch = null;
                _dispatchedLaunchToken = null;
            }
        }

        private static void EnsurePersistentCoordinator()
        {
            if (_instance != null) return;
            var existing = FindObjectOfType<LessonLaunchCoordinatorV2>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var gameObject = new GameObject(nameof(LessonLaunchCoordinatorV2));
            _instance = gameObject.AddComponent<LessonLaunchCoordinatorV2>();
            DontDestroyOnLoad(gameObject);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var launch = _acceptedLaunch;
            if (launch == null || _activeOwner != null ||
                !string.Equals(launch.SceneName, scene.name, StringComparison.Ordinal) ||
                string.Equals(_dispatchedLaunchToken, launch.LaunchToken, StringComparison.Ordinal))
                return;

            LessonGraphSceneDefinitionV2 definition = null;
            var definitions = Resources.FindObjectsOfTypeAll<LessonGraphSceneDefinitionV2>();
            var definitionCount = 0;
            foreach (var candidate in definitions)
            {
                if (candidate == null || candidate.gameObject.scene != scene) continue;
                definition = candidate;
                definitionCount++;
            }

            _dispatchedLaunchToken = launch.LaunchToken;
            if (definitionCount != 1)
            {
                Debug.LogError($"[LessonGraphV2] Accepted scene '{scene.name}' has {definitionCount} lesson definitions; expected exactly one.");
                CancelAcceptedLaunch(launch);
                return;
            }

            var owner = definition.GetComponent<LessonGraphRuntimeOwnerV2>();
            if (owner == null) owner = definition.gameObject.AddComponent<LessonGraphRuntimeOwnerV2>();
            _activeOwner = owner;
            owner.BeginAcceptedLaunch(launch, definition, OnOwnerReleased);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_acceptedLaunch != null && string.Equals(_acceptedLaunch.SceneName, scene.name, StringComparison.Ordinal))
            {
                _acceptedLaunch = null;
                _activeOwner = null;
                _dispatchedLaunchToken = null;
            }
        }

        private void OnOwnerReleased(LessonGraphRuntimeOwnerV2 owner)
        {
            if (ReferenceEquals(_activeOwner, owner)) _activeOwner = null;
        }

        internal static void ResetForTests()
        {
            _pendingAttempt = null;
            _acceptedLaunch = null;
            _dispatchedLaunchToken = null;
            _instance = null;
        }
    }
}
