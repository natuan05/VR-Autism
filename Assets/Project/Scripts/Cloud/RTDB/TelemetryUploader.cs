using UnityEngine;
using System;
using VRAutism.Core.Models;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Đẩy BehaviorSnapshot lên nhánh behavior_snapshots/ của RTDB.
    /// Đứng giữa TelemetryStreamer và Firebase RTDB.
    ///
    /// Được gọi từ TelemetryStreamer.StreamRoutine() mỗi 2 giây.
    /// Payload sẽ mở rộng khi tính năng Gaze Cone & Proximity (TELEMETRY_GAZE_DESIGN.md) được triển khai.
    ///
    /// (Tương lai) Buffer/Retry khi mạng không ổn định.
    /// </summary>
    public class TelemetryUploader : MonoBehaviour
    {
        public static TelemetryUploader Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ══════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Bắn một mẫu dữ liệu hành vi lên RTDB.
        /// Đường dẫn: behavior_snapshots/{sessionId}/{timestamp}
        /// Truyền JSON thô cho tối ưu hiệu năng.
        /// </summary>
        public async void PushAggregatedSnapshot(string sessionId, AggregatedSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            var root = GetRoot();
            if (root == null) return;

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string json = UnityEngine.JsonUtility.ToJson(snapshot);

            try
            {
                await root.Child("behavior_snapshots").Child(sessionId).Child(timestamp)
                          .SetRawJsonValueAsync(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryUploader] Lỗi bắn Telemetry snapshot: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  TODO (Buffer/Retry — Tương lai)
        //  Nếu mạng bị ngắt giữa chừng, cần queue snapshot và retry khi có mạng trở lại.
        // ══════════════════════════════════════════════════════════════

        private Firebase.Database.DatabaseReference GetRoot() => RTDBConnection.Instance?.RootRef;
    }
}
