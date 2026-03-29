using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using VRAutism.Cloud.Models;
using VRAutism.Cloud;
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
        public static ActionManager Instance { get; private set; }

        // [SerializeField]: Cho phép biến 'private' hiển thị được trên Inspector của Unity để chỉnh sửa
        [SerializeField] private List<ActionEvent> actionEvents;

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

                yield return new WaitForSeconds(actionEvent.duration);

                if (actionEvent.isConditionMet is not null)
                    yield return new WaitUntil(() => actionEvent.isConditionMet.Value);

                actionEvent.onFinished?.Invoke();
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
        public float duration; // Thời gian chờ tối thiểu (giây)
        public UnityEvent onStart; // Sự kiện chạy lúc bắt đầu (kéo thả trong Editor)
        public UnityEvent onFinished; // Sự kiện chạy lúc kết thúc
        public BooleanVariable isConditionMet; // Biến điều kiện để qua màn (True/False)
    }
}

