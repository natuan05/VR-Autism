using System;
using System.Collections.Generic;
using Firebase.Firestore;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Top-level Firestore document written to the "sessions" collection at end of lesson.
    /// Mirrors the SESSIONS schema in DATABASE_SCHEMA_DESIGN.md
    /// </summary>
    [Serializable]
    [FirestoreData]
    public class SessionData
    {
        [FirestoreProperty] public string session_id { get; set; }
        [FirestoreProperty] public string child_profile_id { get; set; }
        [FirestoreProperty] public string hosted_by { get; set; }           // expert uid
        [FirestoreProperty] public string lesson_id { get; set; }
        [FirestoreProperty] public string lesson_name { get; set; }
        [FirestoreProperty] public string level_name { get; set; }
        [FirestoreProperty] public int level_index { get; set; }
        [FirestoreProperty] public string device_id { get; set; }
        [FirestoreProperty] public string type { get; set; }                // "practical" | "theoretical"
        [FirestoreProperty] public string start_time { get; set; }          // ISO 8601
        [FirestoreProperty] public string finish_time { get; set; }
        [FirestoreProperty] public double duration { get; set; }            // seconds
        [FirestoreProperty] public string completion_status { get; set; }   // "success" | "aborted" | "timeout"
        [FirestoreProperty] public int score { get; set; }
        [FirestoreProperty] public string video_url { get; set; }

        // Nested sub-collections stored inline for the bulk write
        [FirestoreProperty] public List<QuestLogData> quest_logs { get; set; } = new List<QuestLogData>();
    }
}
