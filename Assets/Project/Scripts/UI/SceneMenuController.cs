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
                VRAutism.Cloud.RealtimeDBManager.Instance.OnNewSessionCommand += LoadRemoteLesson;
            }
        }

        private void LoadRemoteLesson(string childId, string lessonId, string sessionId)
        {
            Debug.Log($"[SceneMenuController] Nhận lệnh Session. Bé: {childId}, Buổi: {sessionId}. Tải Scene: {lessonId}");
            
            if (VRAutism.Core.SessionContext.Instance != null)
            {
                VRAutism.Core.SessionContext.Instance.SessionId = sessionId;
                VRAutism.Core.SessionContext.Instance.ChildId = childId;
            }
            else 
            {
                Debug.LogWarning("[SceneMenuController] Không tìm thấy SessionContext! Dữ liệu sẽ không được bảo toàn qua Scene mới.");
            }
            
            
            SceneManager.LoadScene(lessonId);
        }

        private void OnDestroy()
        {
            // Xóa theo dõi khi Object bị gỡ bỏ để tránh Tồn đọng luồng bộ nhớ (Memory Leak)
            if (VRAutism.Cloud.RealtimeDBManager.Instance != null)
            {
                VRAutism.Cloud.RealtimeDBManager.Instance.OnNewSessionCommand -= LoadRemoteLesson;
            }
        }
    }
}
