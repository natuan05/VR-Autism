using UnityEngine;
using System;
using System.Collections.Generic;
using VRAutism.Cloud.LiveKit;

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
        /// Ghi vr_state với status="ready" và khởi động LiveKit POV Video stream.
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

                // Khởi động LiveKit POV Video Stream nếu camera chính có sẵn
                if (LiveKitService.Instance != null)
                {
                    var ctx = VRAutism.Core.SessionContext.Instance;
                    string token = ctx != null ? ctx.LiveKitToken : "";
                    string livekitUrl = !string.IsNullOrEmpty(ctx?.LiveKitUrl) ? ctx.LiveKitUrl : "wss://vra-9jrt51dr.livekit.cloud";

                    if (!string.IsNullOrEmpty(token))
                    {
                        Debug.Log($"[LiveSessionReporter] 🌐 Đang kết nối LiveKit Room cho phiên: {sessionId}...");
                        LiveKitService.Instance.Connect(livekitUrl, token);
                    }
                    else
                    {
                        Debug.LogWarning("[LiveSessionReporter] ⚠️ Không tìm thấy LiveKitToken trong SessionContext.");
                    }

                    var mainCamera = Camera.main;
                    if (mainCamera != null)
                    {
                        Debug.Log("[LiveSessionReporter] 📹 Khởi động LiveKit POV Stream cho camera chính...");
                        LiveKitService.Instance.EnablePOVCamera(mainCamera);
                    }
                }
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
        /// Dừng LiveKit POV stream và ghi vr_state.status = "ended".
        /// </summary>
        public async void SendLiveSessionEnded(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[LiveSessionReporter] SendLiveSessionEnded: sessionId trống, bỏ qua.");
                return;
            }

            // Dọn dẹp POV Video Stream trong LiveKitService
            LiveKitService.Instance?.DisablePOVCamera();

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

        private Firebase.Database.DatabaseReference GetRoot() => RTDBConnection.Instance?.RootRef;
    }
}
