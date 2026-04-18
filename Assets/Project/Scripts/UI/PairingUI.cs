using UnityEngine;
using TMPro;

namespace VRAutism.UI
{
    /// <summary>
    /// Màn hình UI hiển thị trạng thái kết nối trên Lobby VR.
    /// Đăng ký vào tất cả event của PairingManager để phản ứng liên tục.
    /// 
    /// Khi Scene Lobby load lại (sau lesson):
    ///   - Nếu PIN cũ vẫn paired → Hiện "Đã kết nối. Chờ chọn bài mới..."
    ///   - Nếu PIN cũ bị Web ngắt → Hiện nút "Tạo mã kết nối mới"
    ///   - Nếu chưa có PIN → Tạo mới
    /// </summary>
    public class PairingUI : MonoBehaviour
    {
        [Header("Giao diện UI")]
        public TextMeshProUGUI pinDisplay;

        private Cloud.RTDB.PairingManager _pairing;

        async void Start()
        {
            if (pinDisplay == null)
            {
                Debug.LogError("[PairingUI] Ô Pin Display chưa được gán Object!");
                return;
            }

            _pairing = Cloud.RTDB.PairingManager.Instance;
            if (_pairing == null)
            {
                Debug.LogError("[PairingUI] PairingManager.Instance chưa tồn tại!");
                return;
            }

            // Đăng ký TẤT CẢ event (sẽ huỷ trong OnDestroy)
            _pairing.OnPinGenerated += HandlePinGenerated;
            _pairing.OnPairedSuccess += HandlePaired;
            _pairing.OnDisconnectedByWeb += HandleDisconnectedByWeb;
            _pairing.OnNewSessionCommand += HandleLessonReady;

            // Kiểm tra: Có PIN cũ còn sống không? (trường hợp quay về từ lesson)
            if (!string.IsNullOrEmpty(_pairing.CurrentPin))
            {
                pinDisplay.text = "Đang kiểm tra kết nối...";
                _pairing.ResumeListening(); // Sẽ fire OnPairedSuccess hoặc OnPinGenerated tuỳ trạng thái
            }
            else
            {
                pinDisplay.text = "Đang tạo mã kết nối...";
                await _pairing.GenerateAndPushPIN();
            }
        }

        // ─── Callback: PIN đã được tạo, đang chờ Web nhập ───
        private void HandlePinGenerated(string pin)
        {
            if (pinDisplay != null)
                pinDisplay.text = "MÃ KẾT NỐI VR LÀ:\n" + pin;
        }

        // ─── Callback: Web đã ghép nối thành công ───
        private void HandlePaired()
        {
            if (pinDisplay != null)
                pinDisplay.text = "✅ Đã Kết Nối!\nĐang chờ giáo viên chọn bài...";
        }

        // ─── Callback: Web chủ động ngắt kết nối ───
        private void HandleDisconnectedByWeb()
        {
            if (pinDisplay != null)
                pinDisplay.text = "⚠️ Đã bị ngắt kết nối!\nMÃ PIN: " + (_pairing?.CurrentPin ?? "???") + 
                    "\nVui lòng yêu cầu giáo viên kết nối lại.";
        }

        // ─── Callback: Giáo viên đã chọn bài, sắp chuyển scene ───
        private void HandleLessonReady(string childId, string sceneName, string lessonId, string sessionId, string hostId)
        {
            // Dùng tham số trực tiếp từ RTDB snapshot — không đọc SessionContext.LessonName
            // vì SceneMenuController.LoadRemoteLesson() là async và chưa fetch Firestore xong
            // tại thời điểm callback này chạy (race condition).
            string displayName = !string.IsNullOrEmpty(sceneName) ? sceneName : lessonId;

            if (pinDisplay != null)
                pinDisplay.text = "🎮 Đang khởi chạy bài:\n" + displayName;
        }

        private void OnDestroy()
        {
            if (_pairing != null)
            {
                _pairing.OnPinGenerated -= HandlePinGenerated;
                _pairing.OnPairedSuccess -= HandlePaired;
                _pairing.OnDisconnectedByWeb -= HandleDisconnectedByWeb;
                _pairing.OnNewSessionCommand -= HandleLessonReady;
            }
        }
    }
}
