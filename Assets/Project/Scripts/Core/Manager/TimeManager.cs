using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using VRAutism.Core;
using UnityEngine;
using VRAutism.Quests;
using Debug = UnityEngine.Debug;
using VRAutism.Cloud;
using VRAutism.Cloud.Models;

namespace VRAutism.Core
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        [SerializeField] private DoubleVariable lessonTime;
        [SerializeField] private LessonInfo lessonInfo;

        private Stopwatch _timer;
        private DateTime _startTime;
        private DateTime _endTime;

        // Tracks the current quest's start time to compute response_time
        private double _questStartSecond;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _startTime = DateTime.Now;

            if (lessonInfo == null)
            {
                Debug.LogError("[TimeManager] LessonInfo not assigned in Inspector!");
                return;
            }

            // Hand lesson metadata off to FirebaseManager to start tracking
            FirebaseManager.Instance.BeginSession(
                lessonId:   lessonInfo.lesson_id,
                lessonName: lessonInfo.lesson_name,
                levelName:  lessonInfo.level_name,
                levelIndex: lessonInfo.level_index,
                lessonType: lessonInfo.type == LessonType.theoretical ? "theoretical" : "practical"
            );

            Debug.Log("[TimeManager] Session started at: " + _startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public void StartLessonTime()
        {
            _timer = new Stopwatch();
            _timer.Start();
            lessonTime.Value = TimeUtils.CurrentSecond;
            StartCoroutine(TrackSkillUpdate());
        }

        /// <summary>Call when a new quest begins to capture its start timestamp.</summary>
        public void MarkQuestStart()
        {
            _questStartSecond = TimeUtils.CurrentSecond;
        }

        /// <summary>
        /// Call when a quest is completed. Builds a QuestLogData and hands it
        /// to FirebaseManager's in-memory accumulator.
        /// </summary>
        public void LogQuestComplete(int questIndex, string questName, string completionStatus,
                                     int hintsVerbal = 0, int hintsVisual = 0, int hintsPhysical = 0)
        {
            double responseTime = TimeUtils.CurrentSecond - _questStartSecond;

            var log = new QuestLogData
            {
                index             = questIndex,
                quest_name        = questName,
                response_time     = responseTime,
                completion_status = completionStatus,
                hints_verbal      = hintsVerbal,
                hints_visual      = hintsVisual,
                hints_physical    = hintsPhysical
            };

            FirebaseManager.Instance.AccumulateQuestLog(log);
        }

        /// <summary>
        /// Call when the lesson finishes (success or aborted).
        /// Finalises timing and triggers the single Firestore write.
        /// </summary>
        public void SaveLessonTimeData(string completionStatus = "success", int score = 0)
        {
            if (_timer == null) return;
            _timer.Stop();
            _endTime = DateTime.Now;
            double durationSeconds = _timer.Elapsed.TotalSeconds;

            FirebaseManager.Instance.SaveSession(completionStatus, score, durationSeconds);

            Debug.Log($"[TimeManager] Lesson ended. Duration: {durationSeconds:F1}s, Status: {completionStatus}");
        }

        // Duration getter for QuestController compatibility
        public void SaveDurationTime()
        {
            // Kept for compatibility — actual save now happens in SaveLessonTimeData()
        }

        public void StartQuestTime()
        {
            MarkQuestStart();
        }

        // ─────────────────────────────────────────────────────────────────────
        // SKILL TRACKING (unchanged from original — runs every 60s)
        // ─────────────────────────────────────────────────────────────────────
        private IEnumerator TrackSkillUpdate()
        {
            while (_timer.Elapsed.TotalSeconds < 60)
                yield return null;

            while (true)
            {
                yield return new WaitForSeconds(1f);
                // Reserved for future skill-tracking telemetry
            }
        }
    }
}
