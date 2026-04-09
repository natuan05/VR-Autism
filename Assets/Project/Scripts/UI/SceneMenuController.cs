using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRAutism.UI{
    /// <summary>
    /// Trạm nhận lệnh trên màn hình Chờ VR (Lobby).
    /// Khi Web gửi lệnh "Bắt đầu bài học", component này:
    /// 1. Lưu context (childId, sessionId) vào SessionContext
    /// 2. Gọi SceneManager.LoadScene trực tiếp
    /// </summary>
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
            if (VRAutism.Cloud.RealtimeDBManager.Instance != null)
            {
                VRAutism.Cloud.RealtimeDBManager.Instance.OnNewSessionCommand += LoadRemoteLesson;
            }
        }

        private void LoadRemoteLesson(string childId, string sceneName, string lessonId, string sessionId)
        {
            Debug.Log($"[SceneMenuController] Nhận lệnh Session. Bé: {childId}, Bài: {lessonId}, Scene: {sceneName}, Buổi: {sessionId}");
            
            // Lưu context cho Telemetry
            if (VRAutism.Core.SessionContext.Instance != null)
            {
                VRAutism.Core.SessionContext.Instance.SessionId = sessionId;
                VRAutism.Core.SessionContext.Instance.ChildId = childId;
            }
            else 
            {
                Debug.LogWarning("[SceneMenuController] Không tìm thấy SessionContext!");
            }
            
            // Load Scene trực tiếp - không qua trung gian
            SceneManager.LoadScene(sceneName);
        }

        private void OnDestroy()
        {
            if (VRAutism.Cloud.RealtimeDBManager.Instance != null)
            {
                VRAutism.Cloud.RealtimeDBManager.Instance.OnNewSessionCommand -= LoadRemoteLesson;
            }
        }
    }
}
