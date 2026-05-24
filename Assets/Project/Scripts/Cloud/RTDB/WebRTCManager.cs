using UnityEngine;
using System.Collections;
using VRAutism.Core.Telemetry;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Quản lý toàn bộ vòng đời WebRTC stream POV.
    /// Chỉ cần gắn vào cùng GameObject với LiveSessionReporter.
    ///
    /// Lifecycle:
    ///   StartStream(sessionId) → tạo Offer → gửi lên RTDB → nhận Answer → kết nối
    ///   StopStream()           → dọn dẹp PeerConnection + Signaling
    ///   Thất bại 3 lần        → quay về GameMenu
    /// </summary>
    public class WebRTCManager : MonoBehaviour
    {
        public static WebRTCManager Instance { get; private set; }

        private WebRTCStreamer streamer;
        private WebRTCSignaling signaling;

        private string activeSessionId;
        private int retryCount = 0;
        private const int MAX_RETRIES = 3;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // ── Public API ──────────────────────────────────────────────────

        public void StartStream(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            activeSessionId = sessionId;
            retryCount = 0;
            EnsureComponents();
            BeginStream();
        }

        public void StopStream()
        {
            streamer?.StopStreaming();
            signaling?.Cleanup();
        }

        // ── Private ─────────────────────────────────────────────────────

        private void EnsureComponents()
        {
            if (streamer == null)  streamer  = gameObject.AddComponent<WebRTCStreamer>();
            if (signaling == null) signaling = gameObject.AddComponent<WebRTCSignaling>();

            streamer.OnLocalIceCandidate  = cand   => signaling.SendIceCandidate(cand);
            streamer.OnStreamConnected    = ()      => { Debug.Log("[WebRTCManager] ✅ Stream connected."); retryCount = 0; };
            streamer.OnStreamDisconnected = ()      => Debug.Log("[WebRTCManager] Stream disconnected.");

            signaling.OnAnswerReceived       = answer => streamer.SetRemoteAnswer(answer);
            signaling.OnIceCandidateReceived = cand   => streamer.AddIceCandidate(cand);
            signaling.OnSignalingFailed      = OnFailed;
        }

        private void BeginStream()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[WebRTCManager] Camera.main is null — cannot start stream.");
                return;
            }

            StartCoroutine(streamer.StartStreaming(cam, offer =>
            {
                signaling.InitiateSignaling(activeSessionId, offer);
            }));
        }

        private void OnFailed()
        {
            retryCount++;
            Debug.LogWarning($"[WebRTCManager] Handshake failed. Attempt {retryCount}/{MAX_RETRIES}");

            if (retryCount >= MAX_RETRIES)
            {
                Debug.LogError("[WebRTCManager] Max retries reached. Returning to GameMenu.");
                StopStream();
                UnityEngine.SceneManagement.SceneManager.LoadScene("GameMenu");
                return;
            }

            Debug.Log("[WebRTCManager] Retrying...");
            StopStream();
            BeginStream();
        }

        private void OnDestroy() => StopStream();
    }
}
