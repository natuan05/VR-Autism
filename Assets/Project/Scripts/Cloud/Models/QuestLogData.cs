using System;
using Firebase.Firestore;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Per-quest log entry. Mirrors the QUEST_LOGS schema in DATABASE_SCHEMA_DESIGN.md.
    /// Accumulated in RAM during the lesson, batch-written to Firestore at session end.
    /// </summary>
    [Serializable]
    [FirestoreData]
    public class QuestLogData
    {
        [FirestoreProperty] public int index { get; set; }
        [FirestoreProperty] public string quest_name { get; set; }
        [FirestoreProperty] public double response_time { get; set; }      // seconds
        [FirestoreProperty] public string completion_status { get; set; }  // "success" | "skipped" | "assisted"
        [FirestoreProperty] public int hints_verbal { get; set; }          // verbal audio cues given
        [FirestoreProperty] public int hints_visual { get; set; }          // visual cue arrows/highlights
        [FirestoreProperty] public int hints_physical { get; set; }        // physical assistance (logged manually)
    }
}
