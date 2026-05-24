using System.Collections;
using UnityEngine;
using VRAutism.Cloud;
using VRAutism.Core;

namespace VRAutism.Core.Telemetry
{
    /// <summary>
    /// File điều phối vòng lặp thu thập dữ liệu (Tuyến giữa SensorHarvester và TelemetryUploader).
    /// Gắn Script này vào cùng một GameObject chứa TimeManager trong Scene Bài Học.
    /// </summary>
    public class TelemetryStreamer : MonoBehaviour
    {
        public static TelemetryStreamer Instance { get; private set; }

        [Header("Tần suất lấy mẫu")]
        [Tooltip("Số giây giữa các lần bắn dữ liệu (Khuyên dùng: 2.0s)")]
        public float pushInterval = 2.0f;

        private SensorHarvester _harvester;
        private Coroutine _streamCoroutine;
        private string _sessionId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Tự động tìm SensorHarvester trong map (vd: gắn trên XR Origin)
            _harvester = FindObjectOfType<SensorHarvester>();
        }

        public void StartStreaming(string sessionId)
        {
            if (_harvester == null)
            {
                Debug.LogWarning("[TelemetryStreamer] Không tìm thấy SensorHarvester! Sẽ không bắn được Telemetry.");
                return;
            }

            _sessionId = sessionId;

            // Đảm bảo không bị chạy đè 2 coroutine
            if (_streamCoroutine != null) StopCoroutine(_streamCoroutine);
            
            _streamCoroutine = StartCoroutine(StreamRoutine());
            Debug.Log($"[TelemetryStreamer] ✅ Đã bắt đầu luồng bắn snapshot mỗi {pushInterval}s (Session: {sessionId})");
        }

        public void StopStreaming()
        {
            if (_streamCoroutine != null)
            {
                StopCoroutine(_streamCoroutine);
                _streamCoroutine = null;
                Debug.Log("[TelemetryStreamer] ⏹️ Đã dừng luồng bắn telemetry.");
            }
        }

        private IEnumerator StreamRoutine()
        {
            while (!string.IsNullOrEmpty(_sessionId) && _harvester != null)
            {
                yield return new WaitForSeconds(pushInterval);
                
                // 1. Lấy thông số thời gian hiện tại của bài học
                float elapsed = 0f;
                if (TimeManager.Instance != null)
                {
                    elapsed = (float)TimeManager.Instance.GetTotalElapsedSeconds();
                }

                // 2. Bảo XR Origin (SensorHarvester) tổng hợp dữ liệu buffer
                var snapshot = _harvester.AggregateAndFlush(elapsed);
                
                // 3. Bảo TelemetryUploader ném thẳng dữ liệu lên Cloud
                if (Cloud.RTDB.TelemetryUploader.Instance != null)
                {
                    Cloud.RTDB.TelemetryUploader.Instance.PushAggregatedSnapshot(_sessionId, snapshot);
                }
            }
        }
    }
}
