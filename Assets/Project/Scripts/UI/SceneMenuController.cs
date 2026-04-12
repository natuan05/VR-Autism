using UnityEngine;
using UnityEngine.SceneManagement;
using Firebase.Firestore;
using Firebase.Extensions;

namespace VRAutism.UI{
    /// <summary>
    /// Trạm nhận lệnh trên màn hình Chờ VR (Lobby).
    /// Khi Web gửi lệnh "Bắt đầu bài học":
    /// 1. Fetch lesson metadata từ Firestore (lessons/{lessonId})
    /// 2. Lưu vào SessionContext
    /// 3. Gọi SceneManager.LoadScene
    /// </summary>
    public class SceneMenuController : MonoBehaviour
    {
        public static SceneMenuController Instance;
        
        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (Cloud.RealtimeDBManager.Instance != null)
            {
                Cloud.RealtimeDBManager.Instance.OnNewSessionCommand += LoadRemoteLesson;
            }
        }

        private async void LoadRemoteLesson(string childId, string sceneName, string lessonId, string sessionId, string hostId)
        {
            if (string.IsNullOrEmpty(lessonId) || string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[SceneMenuController] lessonId hoặc sceneName trống. Bỏ qua lệnh (có thể do sửa RTDB thủ công từng field).");
                return;
            }

            Debug.Log($"[SceneMenuController] Nhận lệnh Session. Bé: {childId}, Bài: {lessonId}, Scene: {sceneName}, Buổi: {sessionId}");
            
            // Lưu context cơ bản trước
            var ctx = VRAutism.Core.SessionContext.Instance;
            if (ctx != null)
            {
                ctx.SessionId = sessionId;
                ctx.ChildId = childId;
                ctx.LessonId = lessonId;
                ctx.HostId = hostId ?? "";
            }

            // Fetch lesson metadata từ Firestore collection "lessons"
            bool fetchSuccess = false;
            try 
            {
                var db = FirebaseFirestore.DefaultInstance;
                DocumentSnapshot doc = await db.Collection(Cloud.FirebasePaths.Lessons).Document(lessonId).GetSnapshotAsync();
                
                if (doc.Exists && ctx != null)
                {
                    ctx.LessonName = doc.ContainsField("lesson_name") ? doc.GetValue<string>("lesson_name") : "";
                    ctx.LevelName = doc.ContainsField("level_name") ? doc.GetValue<string>("level_name") : "";
                    
                    // Sửa lỗi int64: Firestore lưu số dưới dạng long (int64)
                    ctx.LevelIndex = doc.ContainsField("level_index") ? (int)doc.GetValue<long>("level_index") : 0;
                    
                    ctx.LessonType = doc.ContainsField("type") ? doc.GetValue<string>("type") : "";
                    
                    Debug.Log($"[SceneMenuController] Đã fetch Firestore thành công. Bài: {ctx.LessonName} ({ctx.LessonType}) - Mức: {ctx.LevelName}");
                    fetchSuccess = true;
                }
                else
                {
                    Debug.LogWarning($"[SceneMenuController] THẤT BẠI: Không tìm thấy document bài học có ID là '{lessonId}' trong Firestore! Hãy kiểm tra Web Panel gửi đúng ID chưa.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SceneMenuController] Lỗi fetch Firestore (có thể mất mạng/alt-tab): {ex.Message}. Không chuyển Scene.");
            }
            
            // ⚠️ Chỉ chuyển Scene nếu fetch Firestore thành công
            // Tránh trường hợp alt-tab làm Firestore timeout rồi vẫn LoadScene với dữ liệu rỗng
            if (fetchSuccess)
            {
                Debug.Log($"[SceneMenuController] Chuyển tới Scene: {sceneName}");
                SceneManager.LoadScene(sceneName);
            }
        }

        private void OnDestroy()
        {
            if (Cloud.RealtimeDBManager.Instance != null)
            {
                Cloud.RealtimeDBManager.Instance.OnNewSessionCommand -= LoadRemoteLesson;
            }
        }
    }
}
