using System.IO;
using VRAutism.Core;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;
    private DatabaseReference dbReference;
    private string sessionId;

    private void Awake()
    {
        Instance = this;
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                dbReference = FirebaseDatabase.DefaultInstance.RootReference;
                UploadLessonTimeData();
                Debug.Log("Firebase đã được khởi tạo thành công!");
            }
            else
            {
                Debug.LogError("Firebase không thể khởi tạo: " + task.Result);
            }
        });
        
    }

    public void UploadLessonTimeData()
    {
        string filePath = Application.persistentDataPath + "/Data/Saved/test.txt";

        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath);
            AddSessionToFirebase(json);
        }
        else
        {
            Debug.LogError("File không tồn tại: " + filePath);
        }
    }

    /*   private void AddSessionToFirebase(string jsonData)
       {
           DatabaseReference sessionsRef = dbReference.Child("sessions");

           sessionsRef.GetValueAsync().ContinueWithOnMainThread(task =>
           {
               if (task.IsFaulted || task.IsCanceled)
               {
                   Debug.LogError("Lỗi khi lấy dữ liệu sessions: " + task.Exception);
                   return;
               }

               DataSnapshot snapshot = task.Result;
               int maxId = -1;

               foreach (var child in snapshot.Children)
               {
                   if (int.TryParse(child.Key, out int id))
                   {
                       if (id > maxId)
                           maxId = id;
                   }
               }

               int nextId = maxId + 1;

               sessionId = nextId.ToString();

               sessionsRef.Child(nextId.ToString()).SetRawJsonValueAsync(jsonData).ContinueWithOnMainThread(uploadTask =>
               {
                   if (uploadTask.IsCompleted)
                   {
                       Debug.Log("Session mới đã được thêm vào Firebase với ID: " + nextId);
                   }
                   else
                   {
                       Debug.LogError("Lỗi khi thêm session mới: " + uploadTask.Exception);
                   }
               });
           });
       }*/

    private void AddSessionToFirebase(string jsonData)
    {
        DatabaseReference sessionsRef = dbReference.Child("sessions");

        DatabaseReference newSessionRef = sessionsRef.Push();
        // Lưu lại id
        sessionId = newSessionRef.Key;

        newSessionRef.SetRawJsonValueAsync(jsonData).ContinueWithOnMainThread(uploadTask =>
        {
            if (uploadTask.IsCompleted)
            {
                Debug.Log("Session mới đã được thêm vào Firebase với ID (sessionId): " + sessionId);
            }
            else
            {
                Debug.LogError("Lỗi khi thêm session mới: " + uploadTask.Exception);
            }
        });
    }


    public void UpdateQuestData(string field, object value, int index)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError("Không có sessionId để cập nhật dữ liệu!");
            return;
        }

        DatabaseReference sessionRef = dbReference.Child("sessions").Child(sessionId).Child("quest_list").Child(index.ToString());

        sessionRef.Child(field).SetValueAsync(value).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                Debug.Log($"Dữ liệu {field} đã được cập nhật thành công với giá trị: {value}");
            }
            else
            {
                Debug.LogError($"Lỗi khi cập nhật {field}: {task.Exception}");
            }
        });
    }

    public void UpdateSessionData(string field, object value)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError("Không có sessionId để cập nhật dữ liệu!");
            return;
        }

        DatabaseReference sessionRef = dbReference.Child("sessions").Child(sessionId);

        sessionRef.Child(field).SetValueAsync(value).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                Debug.Log($"Dữ liệu {field} đã được cập nhật thành công với giá trị: {value}");
            }
            else
            {
                Debug.LogError($"Lỗi khi cập nhật {field}: {task.Exception}");
            }
        });
    }


    public void PushNewSkillData(SkillsData skill, int index)
    {
        string path = $"sessions/{sessionId}/skills/{index}";

        string jsonData = JsonUtility.ToJson(skill);

        FirebaseDatabase.DefaultInstance.RootReference.Child(path).SetRawJsonValueAsync(jsonData)
            .ContinueWith(task =>
            {
                if (task.IsCompleted)
                {
                    UnityEngine.Debug.Log($"Skill {index} đã được lưu lên Firebase!");
                }
                else
                {
                    UnityEngine.Debug.LogError("Lỗi khi push dữ liệu Skills lên Firebase: " + task.Exception);
                }
            });
    }
}
