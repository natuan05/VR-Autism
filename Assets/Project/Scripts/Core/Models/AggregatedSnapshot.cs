using System;
using UnityEngine;

namespace VRAutism.Core.Models
{
    /// <summary>
    /// Bản ghi tổng hợp từ 100 mẫu dữ liệu (thực hiện qua SensorHarvester).
    /// Thay thế BehaviorSnapshot cũ, chứa các chỉ số đã qua xử lý (peak, avg, ratio) 
    /// thay vì chỉ lấy mẫu 1 điểm duy nhất.
    /// </summary>
    [Serializable]
    public class AggregatedSnapshot
    {
        [Header("Timing")]
        public float time_offset;       // Giây thứ mấy trong buổi học

        [Header("Head Velocity")]
        public float head_vel_avg;      // Vận tốc tịnh tiến đầu trung bình (m/s)
        public float head_vel_peak;     // Vận tốc tịnh tiến đầu đỉnh (m/s)
        
        [Header("Angular Velocity")]
        public float ang_vel_x_avg;     // Vận tốc góc Pitch trung bình (°/s)
        public float ang_vel_x_peak;    // Vận tốc góc Pitch đỉnh (°/s)
        public float ang_vel_y_avg;     // Vận tốc góc Yaw trung bình (°/s)
        public float ang_vel_y_peak;    // Vận tốc góc Yaw đỉnh (°/s)

        [Header("Hands Velocity")]
        public float left_hand_vel_avg;   // Vận tốc tay trái TB (m/s)
        public float left_hand_vel_peak;  // Vận tốc tay trái đỉnh (m/s)
        public float right_hand_vel_avg;  // Vận tốc tay phải TB (m/s)
        public float right_hand_vel_peak; // Vận tốc tay phải đỉnh (m/s)

        [Header("Focus (Gaze)")]
        public string focus_object;     // Vật trẻ nhìn nhiều nhất trong 2s
        public string expected_target;  // Quest mục tiêu đang yêu cầu
        public float focus_ratio;       // Tỷ lệ tập trung (0.0-1.0)

        [Header("Proximity")]
        public float hand_near_ratio;   // Tỷ lệ tay gần mục tiêu (0.0-1.0)
        public float min_hand_dist;     // Khoảng cách gần nhất tay->target (m)

        public AggregatedSnapshot() { }
    }
}
