using UnityEngine;
using Firebase.Database;
using System.Threading.Tasks;
using System;
using UnityEngine.SceneManagement;
using VRAutism.Cloud.Models;

namespace VRAutism.Cloud.RTDB
{
    /// <summary>
    /// Quản lý vòng đời kết nối Pairing giữa Kính VR và Web Dashboard.
    /// Thiết kế theo mô hình State Machine — lắng nghe liên tục, phản ứng theo trạng thái.
    ///
    /// Lifecycle:
    ///   [IDLE] → GenerateAndPushPIN() → [WAITING_FOR_PAIR]
    ///   Web nhập PIN → status="paired" → [PAIRED_IDLE] (chờ chọn bài)
    ///   Web bấm Start → session_id thay đổi → fire OnNewSessionCommand
    ///   Lesson xong, quay về Lobby → ResumeListening() → [PAIRED_IDLE]
    ///   Web bấm Disconnect → status="waiting" → fire OnDisconnectedByWeb
    ///   VR tắt app → RTDBConnection.OnApplicationQuit gọi CleanupOnQuit()
    /// </summary>
    public class PairingManager : MonoBehaviour
    {
        public static PairingManager Instance { get; private set; }

        private string _currentPin;
        private bool _isPaired = false;
        private string _lastProcessedSessionId = "";

        // ─── Events cho UI đăng ký ───
        public event Action<string> OnPinGenerated;           // PIN đã tạo xong
        public event Action OnPairedSuccess;                   // Web vừa ghép nối thành công
        public event Action OnDisconnectedByWeb;               // Web chủ động ngắt kết nối
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
            // Nếu đang có PIN cũ còn sống (trường hợp quay về từ lesson) → Tái sử dụng
            if (!string.IsNullOrEmpty(_currentPin) && _isPaired)
            {
                Debug.Log($"[PairingManager] PIN {_currentPin} vẫn còn hiệu lực (đang paired). Tái sử dụng.");
                OnPinGenerated?.Invoke(_currentPin);
                OnPairedSuccess?.Invoke();
                return _currentPin;
            }

            StopListeningAll();

            _currentPin = UnityEngine.Random.Range(100000, 999999).ToString();
            _isPaired = false;
            _lastProcessedSessionId = "";

            PairingData newPairing = new PairingData(_currentPin, RTDBConnection.Instance.DeviceId);
            string jsonPayload = UnityEngine.JsonUtility.ToJson(newPairing);

            var root = GetRoot();
            if (root == null) return "ERROR";

            var pinRef = root.Child("pairing_codes").Child(_currentPin);
            await pinRef.SetRawJsonValueAsync(jsonPayload);

            // Đăng ký "di chúc" với Firebase Server
            pinRef.OnDisconnect().RemoveValue();

            Debug.Log($"[PairingManager] Đã tạo PIN: {_currentPin}. OnDisconnect Cleanup đã thiết lập.");

            OnPinGenerated?.Invoke(_currentPin);
            StartListeningToPinNode(_currentPin);

            return _currentPin;
        }

        /// <summary>
        /// Gọi khi Scene Lobby được load lại (sau khi hoàn thành lesson).
        /// Nếu PIN vẫn còn sống và đang paired → Không tạo lại PIN, chỉ báo cho UI.
        /// </summary>
        public void ResumeListening()
        {
            if (string.IsNullOrEmpty(_currentPin))
            {
                Debug.LogWarning("[PairingManager] ResumeListening() gọi nhưng không có PIN. Bỏ qua.");
                return;
            }

            _lastProcessedSessionId = "";

            if (_isPaired)
            {
                Debug.Log($"[PairingManager] Resume: PIN {_currentPin} vẫn paired. Sẵn sàng nhận bài mới.");
                OnPairedSuccess?.Invoke();
            }
            else
            {
                Debug.Log($"[PairingManager] Resume: PIN {_currentPin} đang ở trạng thái waiting.");
                OnPinGenerated?.Invoke(_currentPin);
            }
        }

        /// <summary>
        /// Được gọi từ RTDBConnection.OnApplicationQuit để dọn dẹp PIN khi app thoát.
        /// </summary>
        public void CleanupOnQuit(DatabaseReference rootRef)
        {
            if (rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                Debug.Log($"[PairingManager] App đang đóng. Xoá PIN: {_currentPin}");
                rootRef.Child("pairing_codes").Child(_currentPin).RemoveValueAsync();
                rootRef.Child("pairing_codes").Child(_currentPin).OnDisconnect().Cancel();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LẮNG NGHE RTDB — Unified Listener trên toàn bộ nhánh PIN
        // ══════════════════════════════════════════════════════════════

        private void StartListeningToPinNode(string pin)
        {
            var root = GetRoot();
            if (root == null) return;

            var pinRef = root.Child("pairing_codes").Child(pin);
            pinRef.ValueChanged += HandlePinNodeChanged;
            Debug.Log($"[PairingManager] Đã bật listener trên toàn node pairing_codes/{pin}");
        }

        private void StopListeningAll()
        {
            var root = GetRoot();
            if (root != null && !string.IsNullOrEmpty(_currentPin))
            {
                var pinRef = root.Child("pairing_codes").Child(_currentPin);
                pinRef.ValueChanged -= HandlePinNodeChanged;
                Debug.Log($"[PairingManager] Đã tắt listener trên PIN: {_currentPin}");
            }
        }

        private void HandlePinNodeChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError($"[PairingManager] Lỗi listener PIN node: {args.DatabaseError.Message}");
                return;
            }

            if (args.Snapshot == null || !args.Snapshot.Exists) return;

            string newStatus = args.Snapshot.Child("status").Value?.ToString() ?? "";
            string sessionId = args.Snapshot.Child("current_session_id").Value?.ToString() ?? "";
            string childId   = args.Snapshot.Child("current_child_id").Value?.ToString() ?? "";
            string sceneName = args.Snapshot.Child("target_scene_name").Value?.ToString() ?? "";
            string lessonId  = args.Snapshot.Child("current_lesson_id").Value?.ToString() ?? "";
            string hostId    = args.Snapshot.Child("host_id").Value?.ToString() ?? "";

            // ── Xử lý thay đổi Status ──
            if (newStatus == "paired" && !_isPaired)
            {
                _isPaired = true;
                Debug.Log("[PairingManager] ✅ Ghép nối thành công! Chờ giáo viên chọn bài...");
                OnPairedSuccess?.Invoke();
            }
            else if (newStatus == "waiting" && _isPaired)
            {
                _isPaired = false;
                _lastProcessedSessionId = "";
                Debug.Log("[PairingManager] ⚠️ Web đã ngắt kết nối! Reset về trạng thái chờ.");
                OnDisconnectedByWeb?.Invoke();
            }

            // ── Xử lý Session mới ──
            if (!string.IsNullOrEmpty(sessionId) && sessionId != _lastProcessedSessionId)
            {
                if (!string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(lessonId))
                {
                    _lastProcessedSessionId = sessionId;
                    Debug.Log($"[PairingManager] Nhận lệnh Session mới → Bé: {childId}, Scene: {sceneName}, Bài: {lessonId}");
                    OnNewSessionCommand?.Invoke(childId, sceneName, lessonId, sessionId, hostId);
                }
                else
                {
                    Debug.LogWarning($"[PairingManager] sessionId mới ({sessionId}) nhưng scene/lesson chưa có. Chờ update tiếp.");
                }
            }
            else if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(_lastProcessedSessionId))
            {
                // Web xoá session → Quay về GameMenu
                _lastProcessedSessionId = "";
                Debug.Log("[PairingManager] ⚠️ Web đã kết thúc session. Trở về màn hình GameMenu...");

                if (VRAutism.Core.SessionContext.Instance != null)
                    VRAutism.Core.SessionContext.Instance.Clear();

                SceneManager.LoadScene("GameMenu");
            }
        }

        private void OnDestroy()
        {
            StopListeningAll();
        }

        private DatabaseReference GetRoot() => RTDBConnection.Instance?.RootRef;
    }
}
