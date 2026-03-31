using System;

namespace VRAutism.Cloud.Models
{
    [Serializable]
    public class PairingData
    {
        public string pin_6_digit;
        public string device_id;
        public string status; // "waiting" | "paired"
        public string child_profile_id;
        public string lesson_id; // Bài học giáo viên chọn trên Web
        public long created_at_utc; // Unix timestamp
        
        public PairingData(string pin, string deviceId)
        {
            pin_6_digit = pin;
            device_id = deviceId;
            status = "waiting";
            child_profile_id = "";
            lesson_id = "";
            created_at_utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
