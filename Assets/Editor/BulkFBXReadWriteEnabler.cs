using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Plugins.QuickOutline.Scripts;

namespace VRAutism.EditorTools
{
    public class BulkFBXReadWriteEnabler : EditorWindow
    {
        [MenuItem("Tools/VR Autism/1. Tối ưu RAM (TẮT Toàn bộ cờ Read-Write)")]
        public static void DisableReadWriteAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model");
            int count = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (path.ToLower().EndsWith(".fbx"))
                    {
                        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                        if (importer != null && importer.isReadable)
                        {
                            importer.isReadable = false;
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.Default);
                            count++;
                        }
                    }
                    EditorUtility.DisplayProgressBar("Giải phóng RAM", $"Đang TẮT Read/Write: {path}", (float)i / guids.Length);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            
            Debug.Log($"<color=yellow>[Tối ưu]</color> Đã TẮT Read/Write cho {count} FBX. (RAM đã được giải phóng).");
        }

        [MenuItem("Tools/VR Autism/2. Quét TỰ ĐỘNG (Dựa trên Prefabs)")]
        public static void EnableReadWriteForTargetOnly()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            HashSet<string> modelsToEnable = new HashSet<string>();

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                
                var outlines = prefab.GetComponentsInChildren<Outline>(true);
                if (outlines.Length > 0)
                {
                    var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                    foreach (var f in filters)
                    {
                        if (f.sharedMesh != null)
                        {
                            string meshPath = AssetDatabase.GetAssetPath(f.sharedMesh);
                            if (meshPath.ToLower().EndsWith(".fbx") && !modelsToEnable.Contains(meshPath))
                            {
                                modelsToEnable.Add(meshPath);
                            }
                        }
                    }
                }
            }
            ProcessModels(modelsToEnable, "Quét Prefab");
        }

        [MenuItem("Tools/VR Autism/3. Quét THỦ CÔNG (Dựa trên Scene ĐANG MỞ)")]
        public static void EnableReadWriteForActiveScene()
        {
            HashSet<string> modelsToEnable = new HashSet<string>();

            // Lấy tất cả object trong Scene hiện tại
            Outline[] outlinesInScene = GameObject.FindObjectsOfType<Outline>(true);

            foreach (var outline in outlinesInScene)
            {
                var filters = outline.GetComponentsInChildren<MeshFilter>(true);
                foreach (var f in filters)
                {
                    if (f.sharedMesh != null)
                    {
                        string meshPath = AssetDatabase.GetAssetPath(f.sharedMesh);
                        if (meshPath.ToLower().EndsWith(".fbx") && !modelsToEnable.Contains(meshPath))
                        {
                            modelsToEnable.Add(meshPath);
                        }
                    }
                }
            }
            ProcessModels(modelsToEnable, "Quét Scene Hiện Tại");
        }

        private static void ProcessModels(HashSet<string> modelsToEnable, string logicName)
        {
            int count = 0;
            try
            {
                int index = 0;
                foreach (string meshPath in modelsToEnable)
                {
                    ModelImporter importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
                    if (importer != null && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.Default);
                        count++;
                    }
                    EditorUtility.DisplayProgressBar(logicName, $"Đang bật Read/Write: {meshPath}", (float)index++ / modelsToEnable.Count);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log($"<color=green>[Hoàn tất {logicName}]</color> Đã bật Read/Write cho <b>{count}</b> FBX!");
            EditorUtility.DisplayDialog($"Tối ưu Hoàn tất ({logicName})", $"Đã bật Read/Write cho {count} FBX.", "OK");
        }
    }
}
