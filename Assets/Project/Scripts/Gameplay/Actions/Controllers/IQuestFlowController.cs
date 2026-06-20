namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Giao diện tối giản mà Quest subclass cần để điều phối luồng Quest.
    /// Tuân thủ Interface Segregation Principle (ISP): Quest con chỉ thấy
    /// đúng những phương thức mà nó thực sự cần, không phụ thuộc vào
    /// concrete QuestController và toàn bộ API của nó.
    /// </summary>
    public interface IQuestFlowController
    {
        /// <summary>Báo hiệu Quest hiện tại đã hoàn thành, chuyển sang Quest tiếp theo.</summary>
        void CompleteActiveQuest(string status = "success");

        /// <summary>Trả về Quest đang chạy, null nếu không có.</summary>
        Quest GetCurQuest();
    }
}
