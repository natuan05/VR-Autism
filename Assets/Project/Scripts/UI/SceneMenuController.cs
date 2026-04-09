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

        private async void LoadRemoteLesson(string childId, string sceneName, string lessonId, string sessionId)
        {
            Debug.Log($"[SceneMenuController] Nhận lệnh Session. Bé: {childId}, Bài: {lessonId}, Scene: {sceneName}, Buổi: {sessionId}");
            
            // Lưu context cơ bản trước
            var ctx = VRAutism.Core.SessionContext.Instance;
            if (ctx != null)
            {
                ctx.SessionId = sessionId;
                ctx.ChildId = childId;
                ctx.LessonId = lessonId;
            }

            // Fetch lesson metadata từ Firestore collection "lessons"
            try 
            {
                var db = FirebaseFirestore.DefaultInstance;
                DocumentSnapshot doc = await db.Collection(Cloud.FirebasePaths.Lessons).Document(lessonId).GetSnapshotAsync();
                
                if (doc.Exists && ctx != null)
                {
                    ctx.LessonName = doc.ContainsField("lesson_name") ? doc.GetValue<string>("lesson_name") : "";
                    ctx.LevelName = doc.ContainsField("level_name") ? doc.GetValue<string>("level_name") : "";
                    ctx.LevelIndex = doc.ContainsField("level_index") ? doc.GetValue<int>("level_index") : 0;
                    ctx.LessonType = doc.ContainsField("type") ? doc.GetValue<string>("type") : "";
                    
                    Debug.Log($"[SceneMenuController] Đã fetch Firestore: {ctx.LessonName} / {ctx.LevelName} / {ctx.LessonType}");
                }
                else
                {
                    Debug.LogWarning($"[SceneMenuController] Không tìm thấy document lessons/{lessonId} trên Firestore!");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SceneMenuController] Lỗi fetch Firestore: {ex.Message}");
            }
            
            // Chuyển Scene
            SceneManager.LoadScene(sceneName);
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
