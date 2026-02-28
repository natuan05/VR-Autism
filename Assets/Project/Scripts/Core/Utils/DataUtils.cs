using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRAutism.Core
{
    public static class DataUtils<T>
    {
        public static T LoadData(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"File not found at: {path}");
                return default; // Trả về giá trị mặc định nếu file không tồn tại
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading data from {path}: {e.Message}");
                return default;
            }
        }

        public static void SaveData(string path, T data)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
                Debug.Log($"Data saved to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error saving data to {path}: {e.Message}");
            }
        }
    }
}

