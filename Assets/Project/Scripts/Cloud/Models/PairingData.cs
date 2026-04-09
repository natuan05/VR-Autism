using System;

namespace VRAutism.Cloud.Models
{
    [Serializable]
    public class PairingData
    {
        public string pin_6_digit;
        public string device_id;
        public string status; // "waiting" | "paired"
        
        // --- Dữ liệu thay đổi liên tục (Device Connection) ---
        public string current_child_id;
        public string current_lesson_id; 
        public string current_session_id; // Web sinh ra khi bấm Start
        public string target_scene_name;  // Web gửi tên Scene Unity cần load (VD: "Bathroom")
        
        
        public long created_at_utc; // Unix timestamp
        
        public PairingData(string pin, string deviceId)
        {
            pin_6_digit = pin;
            device_id = deviceId;
            status = "waiting";
            current_child_id = "";
            current_lesson_id = "";
            current_session_id = "";
            target_scene_name = "";
            created_at_utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
