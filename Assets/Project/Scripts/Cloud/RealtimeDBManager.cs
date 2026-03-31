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
        public event Action<string> OnLessonSelected;

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
                    
                    // Bước 2: Bắt đầu lắng nghe lesson_id
                    GetRootRef().Child("pairing_codes").Child(_currentPin).Child("lesson_id").ValueChanged += HandleLessonIdChanged;
                }
            }
        }

        private void HandleLessonIdChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null) return;

            if (args.Snapshot != null && args.Snapshot.Value != null)
            {
                string lessonId = args.Snapshot.Value.ToString();
                
                // Chỉ kích hoạt khi lesson_id có giá trị thực (khác rỗng)
                if (!string.IsNullOrEmpty(lessonId))
                {
                    GetRootRef().Child("pairing_codes").Child(_currentPin).Child("lesson_id").ValueChanged -= HandleLessonIdChanged;
                    Debug.Log($"[RTDB] Giáo viên đã chọn bài học: {lessonId}");
                    
                    FetchPairedChildInformationAsync(_currentPin);
                }
            }
        }

        private async void FetchPairedChildInformationAsync(string pin)
        {
            DataSnapshot snapshot = await GetRootRef().Child("pairing_codes").Child(pin).GetValueAsync();
            if (snapshot.Exists)
            {
                string childId = snapshot.Child("child_profile_id").Value?.ToString();
                string lessonId = snapshot.Child("lesson_id").Value?.ToString();

                Debug.Log($"[RTDB] Hoàn tất quy trình. Bé ID: {childId}, Load bài: {lessonId}");
                OnLessonSelected?.Invoke(lessonId);
            }
        }

        private void OnDestroy()
        {
            if (_rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                // Unsubscribe listener
                _rootRef.Child("pairing_codes").Child(_currentPin).Child("status").ValueChanged -= HandleStatusChanged;
            }
        }
    }
}
