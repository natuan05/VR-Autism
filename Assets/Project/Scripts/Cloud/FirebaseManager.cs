using System;
using System.IO;
using System.Collections.Generic;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine;
using VRAutism.Cloud.Models;

namespace VRAutism.Cloud
{
    /// <summary>
    /// Manages all Firebase read/write for the VR app.
    /// 
    /// Architecture:
    ///   - Cloud Firestore: Persistent storage (sessions, quest_logs). Written ONCE in bulk at session end.
    ///   - Realtime DB:     Reserved for Phase 2 live features (pairing codes, remote commands).
    ///
    /// Data flow:
    ///   Gameplay → AccumulateQuestLog() [RAM] → SaveSession() → Firestore (one batch write)
    /// </summary>
    public class FirebaseManager : MonoBehaviour
    {
        public static FirebaseManager Instance { get; private set; }

        private FirebaseFirestore _db;
        private bool _isReady;

        // In-memory accumulator — populated during the lesson, written at end
        private SessionData _currentSession;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    _db = FirebaseFirestore.DefaultInstance;
                    _isReady = true;
                    Debug.Log("[FirebaseManager] Firestore ready.");
                }
                else
                {
                    Debug.LogError("[FirebaseManager] Firebase init failed: " + task.Result);
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // SESSION LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Call this at lesson start to initialise the in-memory session container.</summary>
        public void BeginSession(string lessonId, string lessonName, string levelName, int levelIndex, string lessonType, string sessionId = null, string childId = null)
        {
            _currentSession = new SessionData
            {
                session_id       = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString() : sessionId,
                child_profile_id = childId ?? "",
                device_id        = SystemInfo.deviceUniqueIdentifier,
                lesson_id        = lessonId,
                lesson_name      = lessonName,
                level_name       = levelName,
                level_index      = levelIndex,
                type             = lessonType,
                start_time       = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                completion_status = "aborted",   // default; overwritten on success
                quest_logs       = new List<QuestLogData>()
            };

            Debug.Log($"[FirebaseManager] Session started: {_currentSession.session_id}");
        }

        /// <summary>
        /// Accumulates a completed quest's data into RAM.
        /// Called by QuestController after each quest finishes.
        /// </summary>
        public void AccumulateQuestLog(QuestLogData log)
        {
            if (_currentSession == null)
            {
                Debug.LogError("[FirebaseManager] AccumulateQuestLog called before BeginSession.");
                return;
            }
            _currentSession.quest_logs.Add(log);
        }

        /// <summary>
        /// Finalises and bulk-writes the entire session to Firestore.
        /// Call this once at the very end of the lesson.
        /// </summary>
        public void SaveSession(string completionStatus, int score, double durationSeconds)
        {
            if (!_isReady || _currentSession == null)
            {
                Debug.LogError("[FirebaseManager] SaveSession called before Firebase was ready or BeginSession was called.");
                return;
            }

            _currentSession.finish_time       = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            _currentSession.duration          = durationSeconds;
            _currentSession.completion_status = completionStatus;
            _currentSession.score             = score;

            // Convert to Dictionary for Firestore (Firestore SDK serialises Dictionary directly)
            var docRef = _db
                .Collection(FirebasePaths.Sessions)
                .Document(_currentSession.session_id);

            docRef.SetAsync(_currentSession).ContinueWithOnMainThread(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    Debug.Log($"[FirebaseManager] Session saved: {_currentSession.session_id}");
                    _currentSession = null;
                }
                else
                {
                    Debug.LogError("[FirebaseManager] Failed to save session: " + task.Exception);
                }
            });
        }
    }
}
