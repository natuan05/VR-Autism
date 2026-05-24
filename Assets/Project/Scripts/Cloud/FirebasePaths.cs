namespace VRAutism.Cloud
{
    public static class FirebasePaths
    {
        public const string Sessions = "sessions";
        public const string Lessons = "lessons";
        public const string QuestList = "quest_list";
        public const string Skills = "skills";
        
        // WebRTC Signaling
        public const string WebRTCSignaling = "webrtc_signaling";
        public const string WebRTCOffer = "offer";
        public const string WebRTCAnswer = "answer";
        public const string WebRTCVRCandidates = "vr_candidates";
        public const string WebRTCWebCandidates = "web_candidates";
        
        // Thêm cục Database URL vào chỗ tập trung (Thay vì tản mác trên Script khác)
        public const string DatabaseUrl = "https://vra-project-96d9c-default-rtdb.asia-southeast1.firebasedatabase.app/";
    }

    public static class LocalPaths
    {
        public static string SessionData => UnityEngine.Application.persistentDataPath + "/Data/Saved/session.json";
    }
}
