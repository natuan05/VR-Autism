using UnityEngine;
using VRAutism.Core.Models;

namespace VRAutism.Core
{
    /// <summary>
    /// Dữ liệu phiên học, truyền qua các Scene thông qua DontDestroyOnLoad.
    /// Được set bởi SceneMenuController khi nhận lệnh từ Web.
    /// Được đọc bởi TimeManager/FirebaseManager để ghi log.
    /// </summary>
    public class SessionContext : MonoBehaviour
    {
        public static SessionContext Instance;
        
        // --- Thông tin phiên ---
        public string SessionId { get; set; } = "";
        public string ChildId { get; set; } = "";
        
        // --- Thông tin bài học (fetch từ Firestore theo lesson_id) ---
        public string LessonId { get; set; } = "";
        public string LessonName { get; set; } = "";
        public string LevelName { get; set; } = "";
        public int LevelIndex { get; set; } = 0;
        public string LessonType { get; set; } = ""; // "theoretical" hoặc "practical"
        
        // --- Thông tin expert điều khiển ---
        public string HostId { get; set; } = "";

        // --- Cấu hình an toàn âm thanh ---
        private float _maxVolume = 0.5f;
        public float MaxVolume
        {
            get { return _maxVolume; }
            set
            {
                _maxVolume = value;
                AudioListener.volume = value;
                Debug.Log($"[SessionContext] MaxVolume set to {value}. AudioListener.volume is now {AudioListener.volume}");
            }
        }

        // --- Cấu hình bài học động (được ghi đè từ Firestore tại Story 2.3) ---
        /// <summary>
        /// Tham số cấu hình bài học hiện tại.
        /// Mặc định là LessonParameters.GetDefault() — giữ nguyên hành vi legacy.
        /// </summary>
        public LessonParameters CurrentParams { get; set; } = LessonParameters.GetDefault();
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject); 
                return; 
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Xoá toàn bộ context sau khi đã ghi log xong,
        /// tránh rác từ phiên cũ dính sang phiên mới.
        /// </summary>
        public void Clear()
        {
            SessionId = "";
            ChildId = "";
            LessonId = "";
            LessonName = "";
            LevelName = "";
            LevelIndex = 0;
            LessonType = "";
            HostId = "";
            CurrentParams = LessonParameters.GetDefault();
            MaxVolume = 0.5f;
        }

        private void OnEnable()
        {
            VRAutism.Cloud.RTDB.RemoteCommandListener.OnSetVolume += HandleSetVolume;
        }

        private void OnDisable()
        {
            VRAutism.Cloud.RTDB.RemoteCommandListener.OnSetVolume -= HandleSetVolume;
        }

        private void HandleSetVolume(float volume)
        {
            MaxVolume = volume;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
        }
    }
}
