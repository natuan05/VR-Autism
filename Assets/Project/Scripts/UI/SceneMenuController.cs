using System;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRAutism.UI{
    public class SceneMenuController : MonoBehaviour
    {
        public static SceneMenuController Instance;
        
        private void Awake()
        {
            Debug.LogWarning("[SceneMenuController] Đã thiết lập Trạm Nhận Lệnh trên màn hình Chờ VR (Dumb Terminal Mode)!");
            Instance = this;
        }

        private void Start()
        {
            // Kết nối vào Service Đám mây để lắng nghe yêu cầu nạp Bài học từ xa
            if (VRAutism.Cloud.RealtimeDBManager.Instance != null)
            {
                VRAutism.Cloud.RealtimeDBManager.Instance.OnLessonSelected += LoadRemoteLesson;
            }
        }

        private void LoadRemoteLesson(string lessonId)
        {
            Debug.Log($"[SceneMenuController] Nhận lệnh từ Cloud. Tiến hành tự động tải Scene: {lessonId}");
            SceneManager.LoadScene(lessonId);
        }

        private void OnDestroy()
        {
            // Xóa theo dõi khi Object bị gỡ bỏ để tránh Tồn đọng luồng bộ nhớ (Memory Leak)
            if (VRAutism.Cloud.RealtimeDBManager.Instance != null)
            {
                VRAutism.Cloud.RealtimeDBManager.Instance.OnLessonSelected -= LoadRemoteLesson;
            }
        }
    }
}
