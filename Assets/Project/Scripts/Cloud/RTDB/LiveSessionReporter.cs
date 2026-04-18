using UnityEngine;
using System;
using System.Collections.Generic;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Báo cáo trạng thái vòng đời phiên học lên nhánh live_sessions/ của RTDB.
    ///
    /// Lifecycle:
    ///   TimeManager.Start() → SendLiveSessionHandshake() → vr_state.status = "ready"
    ///   SessionSyncTracker  → UpdateCurrentActivity()    → vr_state.current_activity
    ///   TimeManager.SaveLessonTimeData() → SendLiveSessionEnded() → vr_state.status = "ended"
    ///
    /// (Tương lai) StartHeartbeat() / StopHeartbeat() → Task C2 Watchdog
    /// </summary>
    public class LiveSessionReporter : MonoBehaviour
    {
        public static LiveSessionReporter Instance { get; private set; }

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
        /// Gọi ngay sau khi Scene bài học load xong (từ TimeManager.Start).
        /// Ghi vr_state với status="ready" để Web Dashboard biết trẻ đã vào scene thành công.
        /// </summary>
        public async void SendLiveSessionHandshake(string sessionId, string sceneName)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[LiveSessionReporter] SendLiveSessionHandshake: sessionId trống, bỏ qua.");
                return;
            }

            var root = GetRoot();
            if (root == null) return;

            long confirmedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var vrStateData = new Dictionary<string, object>
            {
                { "status",       "ready" },
                { "scene_name",   sceneName },
                { "confirmed_at", confirmedAt }
            };

            try
            {
                await root.Child("live_sessions").Child(sessionId).Child("vr_state")
                          .UpdateChildrenAsync(vrStateData);

                Debug.Log($"[LiveSessionReporter] ✅ Handshake gửi thành công → live_sessions/{sessionId}/vr_state (scene: {sceneName})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveSessionReporter] Handshake thất bại: {ex.Message}");
            }
        }

        /// <summary>
        /// Update hoạt động hiện tại để Web thay đổi các nút tương tác Hint Remote.
        /// Được gọi từ SessionSyncTracker — không gọi trực tiếp từ Gameplay.
        /// </summary>
        public async void UpdateCurrentActivity(string sessionId, string activityName)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            var root = GetRoot();
            if (root == null) return;

            try
            {
                await root.Child("live_sessions").Child(sessionId).Child("vr_state")
                          .Child("current_activity").SetValueAsync(activityName);

                Debug.Log($"[LiveSessionReporter] Đã cập nhật current_activity → {activityName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveSessionReporter] Lỗi khi cập nhật current_activity: {ex.Message}");
            }
        }

        /// <summary>
        /// Gọi khi bài học kết thúc (từ TimeManager.SaveLessonTimeData).
        /// Ghi vr_state.status = "ended" để Web Dashboard tự động đóng trang Session.
        /// </summary>
        public async void SendLiveSessionEnded(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[LiveSessionReporter] SendLiveSessionEnded: sessionId trống, bỏ qua.");
                return;
            }

            var root = GetRoot();
            if (root == null) return;

            var endData = new Dictionary<string, object>
            {
                { "status",   "ended" },
                { "ended_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            try
            {
                await root.Child("live_sessions").Child(sessionId).Child("vr_state")
                          .UpdateChildrenAsync(endData);

                Debug.Log($"[LiveSessionReporter] ✅ Session ended signal gửi thành công → live_sessions/{sessionId}/vr_state");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveSessionReporter] SendLiveSessionEnded thất bại: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  TODO (Task C2 — Watchdog / Heartbeat)
        //  Sẽ triển khai theo kế hoạch RTDB_REFACTORING_PLAN.md §3.3
        // ══════════════════════════════════════════════════════════════

        // public void StartHeartbeat(string sessionId) { ... }
        // public void StopHeartbeat() { ... }
        // private IEnumerator HeartbeatRoutine(string sessionId) { ... }

        private Firebase.Database.DatabaseReference GetRoot() => RTDBConnection.Instance?.RootRef;
    }
}
