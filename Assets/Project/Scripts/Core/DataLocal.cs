using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRAutism.Core
{
    public static class DataLocal<T>
    {
        public static T Load(string folder, string file)
        {
            var dataPath = GetFilePath(folder, file);

            if (!Directory.Exists(Path.GetDirectoryName(dataPath)))
            {
                return default;
            }
            
            var jsonDataAsBytes = File.ReadAllBytes(dataPath);
            var data = Encoding.ASCII.GetString(jsonDataAsBytes);
            
            var jsonData = JsonUtility.ToJson(data);

            var loadData = JsonUtility.FromJson<T>(jsonData);

            return loadData;
        }

        public static void Save(T data, string folder, string file)
        {
            var dataPath = GetFilePath(folder, file);

            var jsonData = JsonUtility.ToJson(data);

            if (!Directory.Exists(Path.GetDirectoryName(dataPath)))
            {
                var name = Path.GetDirectoryName(dataPath);
                Directory.CreateDirectory(name);
            }
            var byteData = Encoding.ASCII.GetBytes(jsonData);
            
            try
            {
                File.WriteAllBytes(dataPath, byteData);
                Debug.Log("Save data to: " + dataPath);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to save data to: " + dataPath);
                Debug.LogError("Error " + e.Message);
            }

        }

        public static string GetFilePath(string folderName, string fileName = "")
        {
            var filePath = Path.Combine(Application.persistentDataPath, "Data/" + folderName);

            if(fileName != "")
            {
                filePath = Path.Combine(filePath, (fileName + ".txt"));
            }

            return filePath;
        }
    }
}

