using UnityEngine;
using TMPro;

namespace VRAutism.UI
{
    public class PairingUI : MonoBehaviour
    {
        [Header("Giao diện UI")]
        public TextMeshProUGUI pinDisplay;

        async void Start()
        {
            if (pinDisplay != null)
            {
                pinDisplay.text = "Đang tạo mã kết nối...";
                
                // Đăng ký các sự kiện 2 bước
                Cloud.RealtimeDBManager.Instance.OnPairedSuccess += HandlePaired;
                Cloud.RealtimeDBManager.Instance.OnNewSessionCommand += HandleLessonReady;

                string newPin = await Cloud.RealtimeDBManager.Instance.GenerateAndPushPIN();
                pinDisplay.text = "MÃ KẾT NỐI VR LÀ:\n" + newPin;
            }
            else
            {
                Debug.LogError("[PairingUI] Ô Pin Display chưa được gán Object!");
            }
        }

        private void HandlePaired()
        {
            pinDisplay.text = "Đã Kết Nối! Đang chờ chọn bài...";
        }

        private void HandleLessonReady(string childId, string sceneName, string lessonId, string sessionId)
        {
            pinDisplay.text = "Giáo viên đã bắt đầu bài:\n" + lessonId;
        }

        private void OnDestroy()
        {
            if (Cloud.RealtimeDBManager.Instance != null)
            {
                Cloud.RealtimeDBManager.Instance.OnPairedSuccess -= HandlePaired;
                Cloud.RealtimeDBManager.Instance.OnNewSessionCommand -= HandleLessonReady;
            }
        }
    }
}
