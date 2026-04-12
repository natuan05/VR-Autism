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

        private Stopwatch _timer;
        private DateTime _startTime;

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

            var ctx = SessionContext.Instance;
            
            // Lấy metadata bài học từ SessionContext (đã được SceneMenuController fetch từ Firestore)
            string lessonId   = ctx != null ? ctx.LessonId : "";
            string lessonName = ctx != null ? ctx.LessonName : "";
            string levelName  = ctx != null ? ctx.LevelName : "";
            int levelIndex    = ctx != null ? ctx.LevelIndex : 0;
            string lessonType = ctx != null ? ctx.LessonType : "";
            string sessionId  = !string.IsNullOrEmpty(ctx?.SessionId) ? ctx.SessionId : Guid.NewGuid().ToString();
            string childId    = ctx != null ? ctx.ChildId : "";

            if (string.IsNullOrEmpty(lessonId))
            {
                Debug.LogWarning("[TimeManager] SessionContext không có LessonId — có thể đang chạy trực tiếp Scene để test.");
            }

            // Hand lesson metadata off to FirebaseManager to start tracking
            FirebaseManager.Instance.BeginSession(
                lessonId:   lessonId,
                lessonName: lessonName,
                levelName:  levelName,
                levelIndex: levelIndex,
                lessonType: lessonType,
                sessionId:  sessionId,
                childId:    childId,
                hostId:     ctx != null ? ctx.HostId : ""
            );

            Debug.Log("[TimeManager] Session started at: " + _startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public void StartLessonTime()
        {
            _timer = new Stopwatch();
            _timer.Start();
            lessonTime.Value = TimeUtils.CurrentSecond;
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

        /// <summary>Gọi từ UnityEvent Inspector (không tham số). Defaults: success, score=0.</summary>
        public void SaveLessonTimeData() => SaveLessonTimeData("success", 0);

        /// <summary>Call when the lesson finishes. Finalises timing and triggers Firestore write.</summary>
        public void SaveLessonTimeData(string completionStatus = "success", int score = 0)
        {
            double durationSeconds;

            if (_timer != null)
            {
                _timer.Stop();
                durationSeconds = _timer.Elapsed.TotalSeconds;
            }
            else
            {
                // Fallback: timer chưa được start (scene không gọi StartLessonTime)
                // Dùng _startTime được set trong Start() làm mốc thay thế
                durationSeconds = (DateTime.Now - _startTime).TotalSeconds;
                Debug.LogWarning("[TimeManager] SaveLessonTimeData: _timer was null, using _startTime fallback.");
            }

            FirebaseManager.Instance.SaveSession(completionStatus, score, durationSeconds);
            Debug.Log($"[TimeManager] Lesson ended. Duration: {durationSeconds:F1}s, Status: {completionStatus}");
        }

        public void StartQuestTime()
        {
            MarkQuestStart();
        }

        public double GetTotalElapsedSeconds()
        {
            if (_timer != null) return _timer.Elapsed.TotalSeconds;
            return (DateTime.Now - _startTime).TotalSeconds;
        }
    }
}
