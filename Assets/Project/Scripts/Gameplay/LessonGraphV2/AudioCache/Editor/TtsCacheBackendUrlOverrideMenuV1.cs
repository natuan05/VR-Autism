#if UNITY_EDITOR
using UnityEditor;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache.Editor
{
    internal static class TtsCacheBackendUrlOverrideMenuV1
    {
        private const string EditorPreferenceKey = "VRAutism.TtsCache.UseLocalDevelopmentBackendV1";
        private const string EnableMenuPath = "Tools/TTS Cache/Use Local Backend (127.0.0.1:3000)";
        private const string DisableMenuPath = "Tools/TTS Cache/Use Configured Backend";

        [MenuItem(EnableMenuPath)]
        private static void EnableLocalBackend()
        {
            EditorPrefs.SetBool(EditorPreferenceKey, true);
            Menu.SetChecked(EnableMenuPath, true);
            Menu.SetChecked(DisableMenuPath, false);
        }

        [MenuItem(EnableMenuPath, true)]
        private static bool ValidateEnableLocalBackend()
        {
            Menu.SetChecked(EnableMenuPath, EditorPrefs.GetBool(EditorPreferenceKey, false));
            return true;
        }

        [MenuItem(DisableMenuPath)]
        private static void DisableLocalBackend()
        {
            EditorPrefs.DeleteKey(EditorPreferenceKey);
            Menu.SetChecked(EnableMenuPath, false);
            Menu.SetChecked(DisableMenuPath, true);
        }

        [MenuItem(DisableMenuPath, true)]
        private static bool ValidateDisableLocalBackend()
        {
            Menu.SetChecked(DisableMenuPath, !EditorPrefs.GetBool(EditorPreferenceKey, false));
            return true;
        }
    }
}
#endif
