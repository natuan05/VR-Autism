using UnityEngine;
using Firebase.Database;
using VRAutism.Cloud.Models;
using System.Threading.Tasks;
using System;
using UnityEngine.SceneManagement;

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

            // Một listener duy nhất trên toàn node PIN.
            // Mỗi lần bất kỳ field nào thay đổi (status, session_id, scene_name...)
            // callback đều nhận Snapshot đầy đủ → không cần GetValueAsync() riêng,
            // loại bỏ hoàn toàn race condition.
            pinRef.ValueChanged += HandlePinNodeChanged;

            Debug.Log($"[RTDB] Đã bật listener trên toàn node pairing_codes/{pin}");
        }

        private void StopListeningAll()
        {
            if (_rootRef != null && !string.IsNullOrEmpty(_currentPin))
            {
                var pinRef = _rootRef.Child("pairing_codes").Child(_currentPin);
                pinRef.ValueChanged -= HandlePinNodeChanged;
                Debug.Log($"[RTDB] Đã tắt listener trên PIN: {_currentPin}");
            }
        }

        // ─── Handler duy nhất: Toàn bộ node PIN thay đổi ───
        private void HandlePinNodeChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError($"[RTDB] Lỗi listener PIN node: {args.DatabaseError.Message}");
                return;
            }

            if (args.Snapshot == null || !args.Snapshot.Exists) return;

            // Đọc tất cả field trực tiếp từ Snapshot — không cần fetch thêm
            string newStatus  = args.Snapshot.Child("status").Value?.ToString() ?? "";
            string sessionId  = args.Snapshot.Child("current_session_id").Value?.ToString() ?? "";
            string childId    = args.Snapshot.Child("current_child_id").Value?.ToString() ?? "";
            string sceneName  = args.Snapshot.Child("target_scene_name").Value?.ToString() ?? "";
            string lessonId   = args.Snapshot.Child("current_lesson_id").Value?.ToString() ?? "";
            string hostId     = args.Snapshot.Child("host_id").Value?.ToString() ?? "";

            // ── Xử lý thay đổi Status ──
            if (newStatus == "paired" && !_isPaired)
            {
                _isPaired = true;
                Debug.Log("[RTDB] ✅ Ghép nối thành công! Chờ giáo viên chọn bài...");
                OnPairedSuccess?.Invoke();
            }
            else if (newStatus == "waiting" && _isPaired)
            {
                _isPaired = false;
                _lastProcessedSessionId = "";
                Debug.Log("[RTDB] ⚠️ Web đã ngắt kết nối! Reset về trạng thái chờ.");
                OnDisconnectedByWeb?.Invoke();
            }

            // ── Xử lý Session mới ──
            if (!string.IsNullOrEmpty(sessionId) && sessionId != _lastProcessedSessionId)
            {
                // Tất cả dữ liệu đã có trong Snapshot này — không gọi GetValueAsync()
                if (!string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(lessonId))
                {
                    _lastProcessedSessionId = sessionId;
                    Debug.Log($"[RTDB] Nhận lệnh Session mới → Bé: {childId}, Scene: {sceneName}, Bài: {lessonId}, Session: {sessionId}");
                    OnNewSessionCommand?.Invoke(childId, sceneName, lessonId, sessionId, hostId);
                }
                else
                {
                    // sessionId đã thay đổi nhưng các field khác chưa có → bỏ qua,
                    // listener sẽ fire lại khi update đầy đủ.
                    Debug.LogWarning($"[RTDB] sessionId mới ({sessionId}) nhưng scene/lesson chưa có trong snapshot. Chờ update tiếp theo.");
                }
            }
            else if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(_lastProcessedSessionId))
            {
                // Web xoá session → Quay về GameMenu
                _lastProcessedSessionId = "";
                Debug.Log("[RTDB] ⚠️ Web đã kết thúc session. Trở về màn hình GameMenu...");

                if (VRAutism.Core.SessionContext.Instance != null)
                    VRAutism.Core.SessionContext.Instance.Clear();

                SceneManager.LoadScene("GameMenu");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  HANDSHAKE — VR xác nhận đã vào scene bài học
        //  Web lắng nghe node này để biết trẻ đã sẵn sàng
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gọi ngay sau khi Scene bài học load xong (từ TimeManager.Start).
        /// Tạo node `live_sessions/{sessionId}` và ghi `vr_state` với status="ready"
        /// để Web Dashboard biết trẻ đã vào bên trong scene thành công.
        /// </summary>
        /// <param name="sessionId">Session ID nhận từ Web qua pairing_codes</param>
        /// <param name="sceneName">Tên Scene đang chạy (ví dụ: "Bathroom", "Farm")</param>
        public async void SendLiveSessionHandshake(string sessionId, string sceneName)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[RTDB] SendLiveSessionHandshake: sessionId trống, bỏ qua.");
                return;
            }

            var root = GetRootRef();
            if (root == null) return;

            long confirmedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var vrStateData = new System.Collections.Generic.Dictionary<string, object>
            {
                { "status",       "ready" },
                { "scene_name",   sceneName },
                { "confirmed_at", confirmedAt }
            };

            try
            {
                await root.Child("live_sessions").Child(sessionId).Child("vr_state")
                           .UpdateChildrenAsync(vrStateData);

                Debug.Log($"[RTDB] ✅ Handshake gửi thành công → live_sessions/{sessionId}/vr_state (scene: {sceneName})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RTDB] Handshake thất bại: {ex.Message}");
            }
        }

        /// <summary>
        /// Gọi khi bài học kết thúc (trước khi LoadScene GameMenu).
        /// Ghi vr_state.status = "ended" để Web Dashboard tự động đóng trang Session.
        /// </summary>
        public async void SendLiveSessionEnded(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[RTDB] SendLiveSessionEnded: sessionId trống, bỏ qua.");
                return;
            }

            var root = GetRootRef();
            if (root == null) return;

            var endData = new System.Collections.Generic.Dictionary<string, object>
            {
                { "status",   "ended" },
                { "ended_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            try
            {
                await root.Child("live_sessions").Child(sessionId).Child("vr_state")
                           .UpdateChildrenAsync(endData);
                Debug.Log($"[RTDB] ✅ Session ended signal gửi thành công → live_sessions/{sessionId}/vr_state");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RTDB] SendLiveSessionEnded thất bại: {ex.Message}");
            }
        }


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
