using System;
using System.Collections.Generic;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Top-level Firestore document written to the "sessions" collection at end of lesson.
    /// Mirrors the SESSIONS schema in DATABASE_SCHEMA_DESIGN.md
    /// </summary>
    [Serializable]
    public class SessionData
    {
        public string session_id;
        public string child_profile_id;
        public string hosted_by;           // expert uid
        public string lesson_id;
        public string lesson_name;
        public string level_name;
        public int level_index;
        public string device_id;
        public string type;                // "practical" | "theoretical"
        public string start_time;          // ISO 8601
        public string finish_time;
        public double duration;            // seconds
        public string completion_status;   // "success" | "aborted" | "timeout"
        public int score;
        public string video_url;

        // Nested sub-collections stored inline for the bulk write
        public List<QuestLogData> quest_logs = new List<QuestLogData>();
    }
}
