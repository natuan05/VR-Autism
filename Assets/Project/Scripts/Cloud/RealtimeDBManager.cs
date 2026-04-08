using UnityEngine;
using Firebase.Database;
using VRAutism.Cloud.Models;
using System.Threading.Tasks;
using System;
using UnityEngine.SceneManagement;

namespace VRAutism.Cloud
{
    public class RealtimeDBManager : MonoBehaviour
    {
        public static RealtimeDBManager Instance { get; private set; }

        private DatabaseReference _rootRef;
        private string _currentPin;
        private string _deviceId;
        private string _lastProcessedSessionId = "";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Generate basic device ID based on System Info
            _deviceId = SystemInfo.deviceUniqueIdentifier;
        }

        private DatabaseReference GetRootRef()
        {
            if (_rootRef == null)
            {
                if (string.IsNullOrEmpty(FirebasePaths.DatabaseUrl))
                {
                    Debug.LogError("[RTDB] Link Database URL không tồn tại trong FirebasePaths!");
                    return null;
                }
                _rootRef = FirebaseDatabase.GetInstance(FirebasePaths.DatabaseUrl).RootReference;
            }
            return _rootRef;
        }

        private void Start()
        {
            // Trống. Đã chuyển logic sang GetRootRef() dể lazy-load
        }

        public event Action<string> OnPinGenerated;
        public event Action OnPairedSuccess;
        public event Action<string, string, string> OnNewSessionCommand; // (childId, lessonId, sessionId)

        /// <summary>Sinh mã PIN 6 số và đưa lên Realtime DB làm phòng chờ.</summary>
        public async Task<string> GenerateAndPushPIN()
        {
            _currentPin = UnityEngine.Random.Range(100000, 999999).ToString();
            
            PairingData newPairing = new PairingData(_currentPin, _deviceId);
            string jsonPayload = JsonUtility.ToJson(newPairing);

            var root = GetRootRef();
            if (root == null) return "ERROR";

            // Ghi đè vào nhánh pairing_codes/PIN
            await root.Child("pairing_codes").Child(_currentPin).SetRawJsonValueAsync(jsonPayload);
            Debug.Log($"[RTDB] Đã tạo phiên ghép nối với mã PIN: {_currentPin}");

            OnPinGenerated?.Invoke(_currentPin);
            ListenToPairingStatus(_currentPin);

            return _currentPin;
        }

        private void ListenToPairingStatus(string pin)
        {
            GetRootRef().Child("pairing_codes").Child(pin).Child("status").ValueChanged += HandleStatusChanged;
        }

        private void HandleStatusChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError(args.DatabaseError.Message);
                return;
            }

            if (args.Snapshot != null && args.Snapshot.Value != null)
            {
                string newStatus = args.Snapshot.Value.ToString();
                if (newStatus == "paired")
                {
                    // Bước 1: Dừng nghe status, bắn sự kiện cho UI biết đã Paired
                    GetRootRef().Child("pairing_codes").Child(_currentPin).Child("status").ValueChanged -= HandleStatusChanged;
                    Debug.Log($"[RTDB] Đã ghép nối thiết bị. Chờ giáo viên chọn bài học...");
                    
                    OnPairedSuccess?.Invoke();
                    
                    // Bước 2: Bắt đầu lắng nghe current_session_id (Lắng nghe VĨNH VIỄN)
                    GetRootRef().Child("pairing_codes").Child(_currentPin).Child("current_session_id").ValueChanged += HandleSessionIdChanged;
                }
            }
        }

        private void HandleSessionIdChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot?.Value == null) return;

            string sessionId = args.Snapshot.Value.ToString();

            // Chỉ xử lý khi session_id KHÁC với session đã xử lý lần trước
            // Tránh load lại bài cũ khi Firebase reconnect/flicker
            if (!string.IsNullOrEmpty(sessionId) && sessionId != _lastProcessedSessionId)
            {
                _lastProcessedSessionId = sessionId;
                Debug.Log($"[RTDB] Nhận lệnh Session mới từ Web: {sessionId}");

                FetchPairedChildInformationAsync(_currentPin);
            }
        }

        private async void FetchPairedChildInformationAsync(string pin)
        {
            DataSnapshot snapshot = await GetRootRef().Child("pairing_codes").Child(pin).GetValueAsync();
            if (snapshot.Exists)
            {
                string childId = snapshot.Child("current_child_id").Value?.ToString();
                string lessonId = snapshot.Child("current_lesson_id").Value?.ToString();
                string sessionId = snapshot.Child("current_session_id").Value?.ToString();

                Debug.Log($"[RTDB] Thông số ván học -> Bé: {childId}, Bài: {lessonId}, Session: {sessionId}");

                OnNewSessionCommand?.Invoke(childId, lessonId, sessionId);
            }
        }

        private void OnDestroy()
        {
            if (_rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                // Unsubscribe listener
                _rootRef.Child("pairing_codes").Child(_currentPin).Child("status").ValueChanged -= HandleStatusChanged;
                _rootRef.Child("pairing_codes").Child(_currentPin).Child("current_session_id").ValueChanged -= HandleSessionIdChanged;
            }
        }
    }
}
