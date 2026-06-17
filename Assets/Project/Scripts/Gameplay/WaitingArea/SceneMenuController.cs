using UnityEngine;
using UnityEngine.SceneManagement;
using Firebase.Firestore;
using Firebase.Extensions;
using VRAutism.Core.Models;

namespace VRAutism.Gameplay.WaitingArea{
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
            if (Cloud.RTDB.PairingManager.Instance != null)
            {
                Cloud.RTDB.PairingManager.Instance.OnNewSessionCommand += LoadRemoteLesson;
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

            var db = FirebaseFirestore.DefaultInstance;

            // Fetch lesson metadata và child profile song song
            var lessonTask = db.Collection(Cloud.FirebasePaths.Lessons).Document(lessonId).GetSnapshotAsync();
            var childTask  = string.IsNullOrEmpty(childId)
                ? System.Threading.Tasks.Task.FromResult<DocumentSnapshot>(null)
                : db.Collection("child_profiles").Document(childId).GetSnapshotAsync();

            // ── Fetch lesson metadata ──────────────────────────────────────
            try 
            {
                DocumentSnapshot doc = await lessonTask;
                
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

            // ── Fetch child profile → đồng bộ default_lesson_params ────────────
            // Khối này độc lập hoàn toàn: lỗi không bao giờ block việc load Scene.
            try
            {
                DocumentSnapshot childDoc = await childTask;

                if (childDoc != null && childDoc.Exists && ctx != null)
                {
                    // ── Đọc giới hạn âm lượng max_volume hoặc map từ sound_sensitivity ──
                    float parsedMaxVolume = 0.5f;
                    bool hasMaxVolume = false;

                    if (childDoc.ContainsField("max_volume"))
                    {
                        try {
                            parsedMaxVolume = System.Convert.ToSingle(childDoc.GetValue<object>("max_volume"));
                            hasMaxVolume = true;
                        } catch {}
                    }
                    else if (childDoc.ContainsField("maxVolume"))
                    {
                        try {
                            parsedMaxVolume = System.Convert.ToSingle(childDoc.GetValue<object>("maxVolume"));
                            hasMaxVolume = true;
                        } catch {}
                    }

                    if (!hasMaxVolume && childDoc.ContainsField("sound_sensitivity"))
                    {
                        try
                        {
                            int sensitivity = System.Convert.ToInt32(childDoc.GetValue<object>("sound_sensitivity"));
                            // Nhạy cảm âm thanh càng cao (5) thì âm lượng tối đa càng nhỏ (0.2)
                            // Nhạy cảm âm thanh càng thấp (1) thì âm lượng tối đa có thể lớn (0.8)
                            parsedMaxVolume = Mathf.Clamp(1.0f - (sensitivity * 0.15f), 0.1f, 1.0f);
                        }
                        catch {}
                    }

                    ctx.MaxVolume = parsedMaxVolume;
                    Debug.Log($"[SceneMenuController] Đã thiết lập MaxVolume = {ctx.MaxVolume} cho bé '{childId}'");

                    System.Collections.Generic.Dictionary<string, object> lessonParamsMap = null;

                    // Firestore SDK trả về IDictionary — giải mã an toàn qua GetValue hoặc fallback IDictionary
                    if (childDoc.ContainsField("default_lesson_params"))
                    {
                        try
                        {
                            lessonParamsMap = childDoc.GetValue<System.Collections.Generic.Dictionary<string, object>>("default_lesson_params");
                        }
                        catch
                        {
                            var rawMap = childDoc.GetValue<object>("default_lesson_params");
                            if (rawMap is System.Collections.IDictionary idict)
                            {
                                lessonParamsMap = new System.Collections.Generic.Dictionary<string, object>();
                                foreach (System.Collections.DictionaryEntry entry in idict)
                                {
                                    lessonParamsMap[entry.Key.ToString()] = entry.Value;
                                }
                            }
                        }
                    }

                    if (lessonParamsMap != null && lessonParamsMap.Count > 0)
                    {
                        ctx.CurrentParams = LessonParameters.FromDictionary(lessonParamsMap);
                        Debug.Log($"[SceneMenuController] Đã sync default_lesson_params cho bé '{childId}'. " +
                                  $"VisualGuidance={ctx.CurrentParams.Actions.EnableVisualGuidance}, " +
                                  $"BubbleHints={ctx.CurrentParams.Actions.EnableBubbleHints}, " +
                                  $"SpeechTimeout={ctx.CurrentParams.Actions.SpeechSilenceTimeout}");
                    }
                    else
                    {
                        ctx.CurrentParams = LessonParameters.Default;
                        Debug.Log($"[SceneMenuController] default_lesson_params rỗng hoặc chưa có cho bé '{childId}'. Dùng Inspector defaults.");
                    }
                }
                else
                {
                    if (ctx != null) ctx.CurrentParams = LessonParameters.Default;
                    Debug.LogWarning($"[SceneMenuController] Không tìm thấy hồ sơ child_profiles/{childId}. Fallback về Inspector defaults.");
                }
            }
            catch (System.Exception ex)
            {
                if (ctx != null) ctx.CurrentParams = LessonParameters.Default;
                Debug.LogWarning($"[SceneMenuController] Lỗi fetch child profile: {ex.Message}. Fallback về Inspector defaults.");
            }
            
            // ⚠️ Luôn chuyển Scene khi đã nhận lệnh của Web để đồng bộ trạng thái,
            // kể cả khi rớt mạng không lấy được thông tin metadata bài học từ Firestore.
            Debug.Log($"[SceneMenuController] Chuyển tới Scene: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }

        private void OnDestroy()
        {
            if (Cloud.RTDB.PairingManager.Instance != null)
            {
                Cloud.RTDB.PairingManager.Instance.OnNewSessionCommand -= LoadRemoteLesson;
            }
        }
    }
}
