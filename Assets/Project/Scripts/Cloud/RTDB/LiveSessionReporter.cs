using UnityEngine;
using System;
using System.Collections.Generic;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Báo cáo trạng thái vòng đời phiên học lên nhánh live_sessions/ của RTDB.
    ///
    /// Lifecycle:
    ///   TimeManager.Start()             → SendLiveSessionHandshake() → vr_state.status = "ready"
    ///   SessionSyncTracker              → UpdateCurrentActivity()    → vr_state.current_activity
    ///   TimeManager.SaveLessonTimeData() → SendLiveSessionEnded()    → vr_state.status = "ended"
    ///
    /// </summary>
    public class LiveSessionReporter : MonoBehaviour
    {
        public static LiveSessionReporter Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Gọi ngay sau khi Scene bài học load xong (từ TimeManager.Start).
        /// Ghi vr_state với status="ready" và khởi động WebRTC stream.
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

            var vrStateData = new Dictionary<string, object>
            {
                { "status",       "ready" },
                { "scene_name",   sceneName },
                { "confirmed_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            try
            {
                var vrStateRef = root.Child("live_sessions").Child(sessionId).Child("vr_state");

                // ĐĂNG KÝ SỰ KIỆN ONDISCONNECT: Nếu Unity Crash hoặc bấm Stop Play
                // Firebase Server sẽ TỰ ĐỘNG điền status="disconnected" giùm ta.
                vrStateRef.OnDisconnect().UpdateChildren(new Dictionary<string, object> {
                    { "status", "disconnected" },
                    { "ended_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                });

                await vrStateRef.UpdateChildrenAsync(vrStateData);

                Debug.Log($"[LiveSessionReporter] ✅ Handshake gửi thành công → live_sessions/{sessionId}/vr_state (scene: {sceneName})");

                // Đảm bảo WebRTCManager tồn tại, tự động thêm nếu người dùng quên gán trong Editor
                var webrtcManager = WebRTCManager.Instance;
                if (webrtcManager == null)
                {
                    webrtcManager = gameObject.AddComponent<WebRTCManager>();
                    Debug.Log("[LiveSessionReporter] Auto-added WebRTCManager to GameObject.");
                }

                // Uỷ quyền khởi động stream
                webrtcManager.StartStream(sessionId);
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
        /// Dừng WebRTC stream và ghi vr_state.status = "ended".
        /// </summary>
        public async void SendLiveSessionEnded(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[LiveSessionReporter] SendLiveSessionEnded: sessionId trống, bỏ qua.");
                return;
            }

            // Uỷ quyền dọn dẹp stream cho WebRTCManager
            WebRTCManager.Instance?.StopStream();

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
