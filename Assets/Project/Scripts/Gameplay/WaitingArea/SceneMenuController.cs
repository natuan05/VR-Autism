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
            try 
            {
                var db = FirebaseFirestore.DefaultInstance;
                DocumentSnapshot doc = await db.Collection(Cloud.FirebasePaths.Lessons).Document(lessonId).GetSnapshotAsync();
                
                if (doc.Exists && ctx != null)
                {
                    ctx.LessonName = doc.ContainsField("lesson_name") ? doc.GetValue<string>("lesson_name") : sceneName;
                    ctx.LevelName = doc.ContainsField("level_name") ? doc.GetValue<string>("level_name") : "Vô danh";
                    
                    // Sử dụng Convert để xử lý an toàn mọi loại định dạng Int, Long, Float 
                    if (doc.ContainsField("level_index"))
                    {
                        try {
                            ctx.LevelIndex = System.Convert.ToInt32(doc.GetValue<object>("level_index"));
                        } catch { ctx.LevelIndex = 0; }
                    }

                    ctx.LessonType = doc.ContainsField("type") ? doc.GetValue<string>("type") : "";
                    
                    Debug.Log($"[SceneMenuController] Đã fetch Firestore thành công. Bài: {ctx.LessonName} ({ctx.LessonType}) - Mức: {ctx.LevelName}");
                }
                else
                {
                    // Fallback tên cơ bản nếu doc không tồn tại thay vì rỗng
                    if (ctx != null) ctx.LessonName = sceneName;
                    Debug.LogWarning($"[SceneMenuController] Cảnh báo: Không tìm thấy document bài học ID '{lessonId}' trong Firestore. Sẽ tiếp tục Load Scene.");
                }
            }
            catch (System.Exception ex)
            {
                if (ctx != null) ctx.LessonName = sceneName; // Fallback
                Debug.LogError($"[SceneMenuController] Lỗi fetch Firestore (có thể mất mạng/alt-tab): {ex.Message}. Vẫn sẽ tiếp tục chuyển Scene.");
            }
            
            // ⚠️ Luôn chuyển Scene khi đã nhận lệnh của Web để đồng bộ trạng thái,
            // kể cả khi rớt mạng không lấy được thông tin metadata bài học từ Firestore.
            Debug.Log($"[SceneMenuController] Chuyển tới Scene: {sceneName}");
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
