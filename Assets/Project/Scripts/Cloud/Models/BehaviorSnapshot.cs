using System;

namespace VRAutism.Cloud.Models
{
    /// <summary>
    /// Chứa thông số hành vi của trẻ tại một thời điểm cụ thể (sau mỗi 2 giây).
    /// Dùng để vẽ Heatmap và đồ thị chú ý trên Web Dashboard.
    /// </summary>
    [Serializable]
    public class BehaviorSnapshot
    {
        // Thời gian kể từ khi bắt đầu bài học (giây)
        public float time_offset;

        // Góc xoay đầu của trẻ (Yaw - trục Y)
        public float head_rotation_y;

        // Tốc độ di chuyển tay (trung bình 2 tay hoặc tay chủ đạo)
        public float hand_velocity;

        // Tên vật thể trẻ đang nhìn trực diện (qua Raycast)
        public string focus_object_name;

        public BehaviorSnapshot() { }

        public BehaviorSnapshot(float timeOffset, float headYaw, float handVel, string focusObj)
        {
            this.time_offset = timeOffset;
            this.head_rotation_y = headYaw;
            this.hand_velocity = handVel;
            this.focus_object_name = focusObj;
        }
    }
}
