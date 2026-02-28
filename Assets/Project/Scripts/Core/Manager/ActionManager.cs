using System; // Nhập thư viện chuẩn của C# (System)
using System.Collections; // Nhập thư viện để dùng IEnumerator (cho Coroutine)
using System.Collections.Generic; // Nhập thư viện để dùng List<T> (Danh sách)
using VRAutism.Core; // Nhập các script từ thư mục Events
using VRAutism.Core; // Nhập các script tiện ích (Utils)
using UnityEngine; // Thư viện chính của Unity (để dùng MonoBehaviour, GameObject...)
using UnityEngine.Events; // Để dùng UnityEvent (kéo thả sự kiện trong Inspector)

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
        [SerializeField] private IntVariable hintCount; // Biến lưu số lần gợi ý (dạng ScriptableObject)
        [SerializeField] private List<ActionEvent> actionEvents; // Danh sách các hành động (Quest) cần làm theo thứ tự

        private double startTime; // Biến lưu thời gian bắt đầu
        private double endTime; // Biến lưu thời gian kết thúc
        private int index; // Biến đếm số thứ tự event gửi data lên server

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
            index = 0; // Đặt lại chỉ số nhiệm vụ về 0
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

                startTime = TimeUtils.CurrentSecond; // Ghi lại giờ bắt đầu
                hintCount.Value = 0; // Reset số gợi ý về 0
                
                // yield return WaitForSeconds: Tạm dừng code tại đây trong 'duration' giây rồi mới chạy tiếp
                yield return new WaitForSeconds(actionEvent.duration);

                // Nếu có điều kiện hoàn thành (isConditionMet)
                if (actionEvent.isConditionMet is not null)
                {
                    // yield return WaitUntil: Tạm dừng code MÃI MÃI cho đến khi điều kiện thành True
                    // () => ... : Biểu thức Lambda
                    yield return new WaitUntil(() => actionEvent.isConditionMet.Value);
                }

                actionEvent.onFinished?.Invoke(); // Chạy các sự kiện khi kết thúc (ví dụ: hiện chúc mừng)
                endTime = TimeUtils.CurrentSecond; // Ghi lại giờ kết thúc

                // Nếu action này yêu cầu gửi báo cáo lên mạng
                if (actionEvent.onSendData)
                {
                    if (FirebaseManager.Instance is null) // Kiểm tra xem Firebase đã sẵn sàng chưa
                    {
                        Debug.LogError("Firebase is null!"); // Báo lỗi đỏ nếu chưa có
                    }
                    else
                    {
                        // Gửi thời gian hoàn thành (endTime - startTime) lên server
                        FirebaseManager.Instance.UpdateQuestData("response_time", endTime - startTime, index);
                        // Gửi số lần dùng gợi ý lên server
                        FirebaseManager.Instance.UpdateQuestData("hint_count", hintCount.Value, index);
                    }

                    index++; // Tăng số thứ tự để chuẩn bị cho nhiệm vụ sau
                }

                // Lưu tổng thời gian học (Backup)
                if (TimeManager.Instance is null)
                {
                    Debug.LogError("TimeManager is null!");
                }
                else
                {
                    TimeManager.Instance.SaveDurationTime();
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

