using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using VRAutism.Cloud.Models;
using UnityEngine;
using UnityEngine.Events;

// namespace: Gom nhóm code lại để tránh trùng tên với các thư viện khác
namespace VRAutism.Core
{
    // public class: Khai báo một lớp (khuôn mẫu) có thể truy cập từ bên ngoài
    // : MonoBehaviour: Kế thừa từ Unity, nghĩa là script này có thể gắn được vào GameObject
    public class ActionManager : MonoBehaviour
    {
        // static: Biến tĩnh, tồn tại duy nhất trong game.
        // Instance: Pattern "Singleton", giúp các script khác gọi ActionManager.Instance mà không cần tìm
        public static ActionManager Instance;

        // [SerializeField]: Cho phép biến 'private' hiển thị được trên Inspector của Unity để chỉnh sửa
        [SerializeField] private IntVariable hintCount;
        [SerializeField] private List<ActionEvent> actionEvents;
        [SerializeField] private TimeManager timeManager;

        private double _startTime;
        private double _endTime;
        private int _index;

        // Awake: Hàm chạy đầu tiên khi GameObject được khởi tạo (trước Start)
        void Awake()
        {
            Instance = this; // Gán chính bản thân script này vào biến Static để làm Singleton
        }
        
        // Start: Hàm chạy 1 lần khi game bắt đầu (sau Awake)
        private void Start()
        {
            // StartCoroutine: Bắt đầu một luồng chạy song song (để xử lý việc chờ đợi theo thời gian)
            StartCoroutine(ActionLoop());
            _index = 0;
        }

        // Hàm trả về danh sách tên các nhiệm vụ (Quest)
        public List<string> GetQuestName()
        {
            var result = new List<string>(); // Tạo một danh sách mới
            foreach (var action in actionEvents) // Vòng lặp: Duyệt qua từng hành động trong danh sách
            {
                // Nếu hành động này có đánh dấu "onSendData" (Gửi dữ liệu báo cáo) thì mới thêm vào list
                if (action.onSendData) result.Add(action.name);
            }
            return result; // Trả kết quả về
        }

        // IEnumerator: Kiểu trả về bắt buộc cho Coroutine (để hỗ trợ yield return)
        private IEnumerator ActionLoop()
        {
            // Vòng lặp chính: Chạy lần lượt từng ActionEvent từ đầu đến cuối danh sách
            foreach (var actionEvent in actionEvents)
            {
                // Nếu action bị tắt (on == false) thì bỏ qua, sang cái tiếp theo
                if (!actionEvent.on) continue;

                // Debug.Log: In dòng chữ màu mè ra Console để kiểm tra
                Debug.Log("[Debug] <color=#00ff48>Event </color> <color=#ffea00>" + actionEvent.name + "</color> is starting...");
                
                // ?.: Toán tử Null-check. Nếu onStart không null thì mới Invoke (chạy)
                actionEvent.onStart?.Invoke();

                _startTime = TimeUtils.CurrentSecond;
                hintCount.Value = 0;

                yield return new WaitForSeconds(actionEvent.duration);

                if (actionEvent.isConditionMet is not null)
                    yield return new WaitUntil(() => actionEvent.isConditionMet.Value);

                actionEvent.onFinished?.Invoke();
                _endTime = TimeUtils.CurrentSecond;

                if (actionEvent.onSendData)
                {
                    var log = new QuestLogData
                    {
                        index             = _index,
                        quest_name        = actionEvent.name,
                        response_time     = _endTime - _startTime,
                        completion_status = "success",
                        hints_verbal      = hintCount.Value,
                        hints_visual      = 0,
                        hints_physical    = 0
                    };

                    if (FirebaseManager.Instance != null)
                        FirebaseManager.Instance.AccumulateQuestLog(log);
                    else
                        Debug.LogError("[ActionManager] FirebaseManager not ready.");

                    _index++;
                }
            }
            
            Debug.Log("[Debug] <color=#00ff48>All actions have been finished...</color>");

        }
    }

    // [Serializable]: Cho phép class này hiện ra dạng bảng nhập liệu trong Unity Inspector
    [Serializable]
    public class ActionEvent
    {
        public string name; // Tên hành động (để hiển thị)
        public bool on; // Bật/Tắt action này
        public bool onSendData; // Có gửi báo cáo hay không?
        public float duration; // Thời gian chờ tối thiểu (giây)
        public UnityEvent onStart; // Sự kiện chạy lúc bắt đầu (kéo thả trong Editor)
        public UnityEvent onFinished; // Sự kiện chạy lúc kết thúc
        public BooleanVariable isConditionMet; // Biến điều kiện để qua màn (True/False)
    }
}

