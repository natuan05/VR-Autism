using UnityEngine;
using TMPro;

namespace VRAutism.UI
{
    /// <summary>
    /// Màn hình UI hiển thị trạng thái kết nối trên Lobby VR.
    /// Đăng ký vào tất cả event của RealtimeDBManager để phản ứng liên tục.
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

        private Cloud.RealtimeDBManager _rtdb;

        async void Start()
        {
            if (pinDisplay == null)
            {
                Debug.LogError("[PairingUI] Ô Pin Display chưa được gán Object!");
                return;
            }

            _rtdb = Cloud.RealtimeDBManager.Instance;
            if (_rtdb == null)
            {
                Debug.LogError("[PairingUI] RealtimeDBManager.Instance chưa tồn tại!");
                return;
            }

            // Đăng ký TẤT CẢ event (sẽ huỷ trong OnDestroy)
            _rtdb.OnPinGenerated += HandlePinGenerated;
            _rtdb.OnPairedSuccess += HandlePaired;
            _rtdb.OnDisconnectedByWeb += HandleDisconnectedByWeb;
            _rtdb.OnNewSessionCommand += HandleLessonReady;

            // Kiểm tra: Có PIN cũ còn sống không? (trường hợp quay về từ lesson)
            if (!string.IsNullOrEmpty(_rtdb.CurrentPin))
            {
                pinDisplay.text = "Đang kiểm tra kết nối...";
                _rtdb.ResumeListening(); // Sẽ fire OnPairedSuccess hoặc OnPinGenerated tuỳ trạng thái
            }
            else
            {
                pinDisplay.text = "Đang tạo mã kết nối...";
                await _rtdb.GenerateAndPushPIN();
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
                pinDisplay.text = "⚠️ Đã bị ngắt kết nối!\nMÃ PIN: " + (_rtdb?.CurrentPin ?? "???") + 
                    "\nVui lòng yêu cầu giáo viên kết nối lại.";
        }

        // ─── Callback: Giáo viên đã chọn bài, sắp chuyển scene ───
        private void HandleLessonReady(string childId, string sceneName, string lessonId, string sessionId)
        {
            // Ưu tiên lấy tên đẹp từ SessionContext (sau khi Firestore fetch xong)
            string displayName = VRAutism.Core.SessionContext.Instance?.LessonName;
            if (string.IsNullOrEmpty(displayName)) displayName = lessonId;

            if (pinDisplay != null)
                pinDisplay.text = "🎮 Đang khởi chạy bài:\n" + displayName;
        }

        private void OnDestroy()
        {
            if (_rtdb != null)
            {
                _rtdb.OnPinGenerated -= HandlePinGenerated;
                _rtdb.OnPairedSuccess -= HandlePaired;
                _rtdb.OnDisconnectedByWeb -= HandleDisconnectedByWeb;
                _rtdb.OnNewSessionCommand -= HandleLessonReady;
            }
        }
    }
}
