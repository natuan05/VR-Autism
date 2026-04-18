using UnityEngine;
using Firebase.Database;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Singleton gốc — Sở hữu DatabaseReference duy nhất cho toàn bộ module RTDB.
    /// Các module con (PairingManager, LiveSessionReporter, TelemetryUploader...)
    /// đều truy cập RootRef qua RTDBConnection.Instance.RootRef.
    ///
    /// Không chứa bất kỳ logic nghiệp vụ nào.
    /// </summary>
    public class RTDBConnection : MonoBehaviour
    {
        public static RTDBConnection Instance { get; private set; }

        public DatabaseReference RootRef { get; private set; }
        public string DeviceId { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            DeviceId = SystemInfo.deviceUniqueIdentifier;
            InitializeRootRef();
        }

        private void InitializeRootRef()
        {
            if (string.IsNullOrEmpty(FirebasePaths.DatabaseUrl))
            {
                Debug.LogError("[RTDBConnection] FirebasePaths.DatabaseUrl chưa được cấu hình!");
                return;
            }

            RootRef = FirebaseDatabase.GetInstance(FirebasePaths.DatabaseUrl).RootReference;
            Debug.Log("[RTDBConnection] ✅ DatabaseReference đã được khởi tạo.");
        }

        private void OnApplicationQuit()
        {
            // Uỷ quyền dọn dẹp PIN cho PairingManager
            if (PairingManager.Instance != null)
            {
                PairingManager.Instance.CleanupOnQuit(RootRef);
            }
        }
    }
}
