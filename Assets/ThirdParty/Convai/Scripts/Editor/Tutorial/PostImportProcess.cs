using System.IO;
using UnityEditor;
using UnityEngine;

namespace Convai.Scripts.Editor.Tutorial
{
    [InitializeOnLoad]
    public class PostImportProcess
    {
        private static readonly string processedFilePath = Path.Combine(Application.dataPath, "ThirdParty/Convai/Scripts/Editor/Tutorial/.TutorialInitialized");

        static PostImportProcess()
        {
            if (HasAlreadyProcessed())
                return;

            string settingsPath = Path.Combine(Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/')),
                "ProjectSettings/Packages/com.unity.learn.iet-framework/Settings.json");

            if (!File.Exists(settingsPath)) return;
            File.Delete(settingsPath);
            DestroySelf();
            MarkAsProcessed();
        }

        private static void DestroySelf()
        {
            string path = Path.Combine(Application.dataPath, "ThirdParty/Convai/Scripts/Editor/Tutorial/PostImportProcess.cs");
            File.Delete(path);
            File.Delete(path + ".meta");
            File.Delete(processedFilePath);
        }

        private static bool HasAlreadyProcessed()
        {
            return File.Exists(processedFilePath);
        }

        private static void MarkAsProcessed()
        {
            File.WriteAllText(processedFilePath, "TutorialInitialized");
        }
    }
}