using System;
using Firebase.Firestore;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Firestore projection for one Lesson Graph V2 node activation.
    /// Duration is measured with the monotonic lesson clock; the UTC fields are observation timestamps.
    /// </summary>
    [Serializable]
    [FirestoreData]
    public sealed class NodeLogData
    {
        [FirestoreProperty] public string event_id { get; set; }
        [FirestoreProperty] public string session_id { get; set; }
        [FirestoreProperty] public string run_id { get; set; }
        [FirestoreProperty] public string graph_id { get; set; }
        [FirestoreProperty] public string lesson_id { get; set; }
        [FirestoreProperty] public string launch_token { get; set; }
        [FirestoreProperty] public int lesson_voice_revision { get; set; }
        [FirestoreProperty] public int child_phrase_revision { get; set; }
        [FirestoreProperty] public string node_id { get; set; }
        [FirestoreProperty] public string node_type { get; set; }
        [FirestoreProperty] public string node_name { get; set; }
        [FirestoreProperty] public int node_index { get; set; }
        [FirestoreProperty] public string activation_id { get; set; }
        [FirestoreProperty] public string entered_at_utc { get; set; }
        [FirestoreProperty] public string exited_at_utc { get; set; }
        [FirestoreProperty] public double duration_seconds { get; set; }
        [FirestoreProperty] public double elapsed_seconds { get; set; }
        [FirestoreProperty] public string status { get; set; }
        [FirestoreProperty] public string completion_channel { get; set; }
    }
}
