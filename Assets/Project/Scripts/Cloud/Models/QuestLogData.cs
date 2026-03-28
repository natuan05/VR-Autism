using System;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Per-quest log entry. Mirrors the QUEST_LOGS schema in DATABASE_SCHEMA_DESIGN.md.
    /// Accumulated in RAM during the lesson, batch-written to Firestore at session end.
    /// </summary>
    [Serializable]
    public class QuestLogData
    {
        public int index;
        public string quest_name;
        public double response_time;       // seconds
        public string completion_status;   // "success" | "skipped" | "assisted"
        public int hints_verbal;           // verbal audio cues given
        public int hints_visual;           // visual cue arrows/highlights
        public int hints_physical;         // physical assistance (logged manually)
    }
}
