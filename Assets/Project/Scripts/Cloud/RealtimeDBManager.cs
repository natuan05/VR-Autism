using UnityEngine;
using Firebase.Database;
using VRAutism.Cloud.Models;
using System.Threading.Tasks;
using System;

namespace VRAutism.Cloud
{
    /// <summary>
    /// Quản lý vòng đời kết nối RTDB giữa Kính VR và Web Dashboard.
    /// Thiết kế theo mô hình State Machine — lắng nghe liên tục, phản ứng theo trạng thái.
    /// 
    /// Lifecycle:
    ///   [IDLE] → GenerateAndPushPIN() → [WAITING_FOR_PAIR]
    ///   Web nhập PIN → status="paired" → [PAIRED_IDLE] (chờ chọn bài)
    ///   Web bấm Start → session_id thay đổi → [SESSION_STARTING] → fire OnNewSessionCommand
    ///   Lesson xong, quay về Lobby → ResumeListening() → [PAIRED_IDLE] (chờ bài mới)
    ///   Web bấm Disconnect → status="waiting" → [DISCONNECTED_BY_WEB] → fire OnDisconnectedByWeb
    ///   VR tắt app → OnApplicationQuit + OnDisconnect tự dọn PIN
    /// </summary>
    public class RealtimeDBManager : MonoBehaviour
    {
        public static RealtimeDBManager Instance { get; private set; }

        private DatabaseReference _rootRef;
        private string _currentPin;
        private string _deviceId;
        private string _lastProcessedSessionId = "";
        private bool _isPaired = false;

        // ─── Events cho UI đăng ký ───
        public event Action<string> OnPinGenerated;           // PIN đã tạo xong
        public event Action OnPairedSuccess;                   // Web vừa ghép nối thành công  
        public event Action OnDisconnectedByWeb;               // Web chủ động ngắt kết nối (status → "waiting")
        public event Action<string, string, string, string, string> OnNewSessionCommand; // (childId, sceneName, lessonId, sessionId, hostId)

        // ─── Properties cho UI đọc trạng thái hiện tại ───
        public bool IsPaired => _isPaired;
        public string CurrentPin => _currentPin;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
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

        // ══════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Sinh mã PIN 6 số mới và đẩy lên RTDB.
        /// Gọi bởi PairingUI mỗi khi Scene Lobby được load lần đầu tiên (chưa có PIN).
        /// </summary>
        public async Task<string> GenerateAndPushPIN()
        {
            // Nếu đang có PIN cũ còn sống trên RTDB (trường hợp quay về từ lesson) → Tái sử dụng
            if (!string.IsNullOrEmpty(_currentPin) && _isPaired)
            {
                Debug.Log($"[RTDB] PIN {_currentPin} vẫn còn hiệu lực (đang paired). Tái sử dụng, không sinh mới.");
                OnPinGenerated?.Invoke(_currentPin);
                OnPairedSuccess?.Invoke();
                return _currentPin;
            }

            // Dọn listener cũ (nếu có) trước khi tạo mới
            StopListeningAll();

            _currentPin = UnityEngine.Random.Range(100000, 999999).ToString();
            _isPaired = false;
            _lastProcessedSessionId = "";

            PairingData newPairing = new PairingData(_currentPin, _deviceId);
            string jsonPayload = JsonUtility.ToJson(newPairing);

            var root = GetRootRef();
            if (root == null) return "ERROR";

            var pinRef = root.Child("pairing_codes").Child(_currentPin);
            await pinRef.SetRawJsonValueAsync(jsonPayload);

            // Đăng ký "di chúc" với Firebase Server
            pinRef.OnDisconnect().RemoveValue();

            Debug.Log($"[RTDB] Đã tạo PIN: {_currentPin}. OnDisconnect Cleanup đã thiết lập.");

            OnPinGenerated?.Invoke(_currentPin);

            // Bắt đầu lắng nghe TOÀN BỘ nhánh PIN (không phải từng field riêng lẻ)
            StartListeningToPinNode(_currentPin);

            return _currentPin;
        }

        /// <summary>
        /// Gọi khi Scene Lobby được load lại (sau khi hoàn thành lesson).
        /// Nếu PIN vẫn còn sống và đang paired → Không tạo lại PIN, chỉ báo cho UI biết trạng thái.
        /// </summary>
        public void ResumeListening()
        {
            if (string.IsNullOrEmpty(_currentPin))
            {
                Debug.LogWarning("[RTDB] ResumeListening() gọi nhưng không có PIN. Bỏ qua.");
                return;
            }

            // Reset session tracking để sẵn sàng nhận lệnh mới
            _lastProcessedSessionId = "";

            if (_isPaired)
            {
                Debug.Log($"[RTDB] Resume: PIN {_currentPin} vẫn paired. Sẵn sàng nhận bài mới.");
                OnPairedSuccess?.Invoke();
            }
            else
            {
                Debug.Log($"[RTDB] Resume: PIN {_currentPin} đang ở trạng thái waiting.");
                OnPinGenerated?.Invoke(_currentPin);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LẮNG NGHE RTDB — Unified Listener trên toàn bộ nhánh PIN
        // ══════════════════════════════════════════════════════════════

        private void StartListeningToPinNode(string pin)
        {
            var pinRef = GetRootRef().Child("pairing_codes").Child(pin);

            // Listener 1: Nghe status LIÊN TỤC (không bao giờ huỷ cho đến khi tắt app)
            pinRef.Child("status").ValueChanged += HandleStatusChanged;

            // Listener 2: Nghe session_id LIÊN TỤC
            pinRef.Child("current_session_id").ValueChanged += HandleSessionIdChanged;

            Debug.Log($"[RTDB] Đã bật listener liên tục trên pairing_codes/{pin}");
        }

        private void StopListeningAll()
        {
            if (_rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                var pinRef = _rootRef.Child("pairing_codes").Child(_currentPin);
                pinRef.Child("status").ValueChanged -= HandleStatusChanged;
                pinRef.Child("current_session_id").ValueChanged -= HandleSessionIdChanged;
                Debug.Log($"[RTDB] Đã tắt toàn bộ listener trên PIN: {_currentPin}");
            }
        }

        // ─── Handler: status thay đổi ───
        private void HandleStatusChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError($"[RTDB] Lỗi listener status: {args.DatabaseError.Message}");
                return;
            }

            if (args.Snapshot == null || args.Snapshot.Value == null) return;

            string newStatus = args.Snapshot.Value.ToString();
            Debug.Log($"[RTDB] Status thay đổi → \"{newStatus}\"");

            switch (newStatus)
            {
                case "paired":
                    if (!_isPaired) // Chỉ fire event lần đầu chuyển sang paired
                    {
                        _isPaired = true;
                        Debug.Log("[RTDB] ✅ Ghép nối thành công! Chờ giáo viên chọn bài...");
                        OnPairedSuccess?.Invoke();
                    }
                    break;

                case "waiting":
                    if (_isPaired) // Chỉ fire event khi THỰC SỰ bị ngắt (từ paired → waiting)
                    {
                        _isPaired = false;
                        _lastProcessedSessionId = "";
                        Debug.Log("[RTDB] ⚠️ Web đã ngắt kết nối! Reset về trạng thái chờ.");
                        OnDisconnectedByWeb?.Invoke();
                    }
                    break;
            }
        }

        // ─── Handler: current_session_id thay đổi ───
        private void HandleSessionIdChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot?.Value == null) return;

            string sessionId = args.Snapshot.Value.ToString();

            // Chỉ xử lý khi session_id KHÁC với session đã xử lý lần trước
            if (!string.IsNullOrEmpty(sessionId) && sessionId != _lastProcessedSessionId)
            {
                _lastProcessedSessionId = sessionId;
                Debug.Log($"[RTDB] Nhận lệnh Session mới từ Web: {sessionId}");
                FetchPairedChildInformationAsync(_currentPin);
            }
        }

        // ─── Fetch đầy đủ thông tin từ nhánh PIN ───
        private async void FetchPairedChildInformationAsync(string pin)
        {
            DataSnapshot snapshot = await GetRootRef().Child("pairing_codes").Child(pin).GetValueAsync();
            if (snapshot.Exists)
            {
                string childId = snapshot.Child("current_child_id").Value?.ToString();
                string sceneName = snapshot.Child("target_scene_name").Value?.ToString();
                string lessonId = snapshot.Child("current_lesson_id").Value?.ToString();
                string sessionId = snapshot.Child("current_session_id").Value?.ToString();
                string hostId = snapshot.Child("host_id").Value?.ToString();

                Debug.Log($"[RTDB] Thông số buổi học -> Bé: {childId}, Scene: {sceneName}, Bài: {lessonId}, Session: {sessionId}, Host: {hostId}");
                OnNewSessionCommand?.Invoke(childId, sceneName, lessonId, sessionId, hostId);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LIFECYCLE — Dọn dẹp khi tắt app
        // ══════════════════════════════════════════════════════════════

        private void OnDestroy()
        {
            StopListeningAll();
        }

        private void OnApplicationQuit()
        {
            if (_rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                Debug.Log($"[RTDB] App đang đóng. Xoá sảnh chờ PIN: {_currentPin}");
                _rootRef.Child("pairing_codes").Child(_currentPin).RemoveValueAsync();
                _rootRef.Child("pairing_codes").Child(_currentPin).OnDisconnect().Cancel();
            }
        }
    }
}
