namespace VRAutism.Cloud
{
    public static class FirebasePaths
    {
        public const string Sessions = "sessions";
        public const string QuestList = "quest_list";
        public const string Skills = "skills";
    }

    public static class LocalPaths
    {
        public static string SessionData => UnityEngine.Application.persistentDataPath + "/Data/Saved/session.json";
    }
}
