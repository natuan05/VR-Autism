// using UnityEngine;
// using System.Collections;
// using System.IO;
// using UnityGoogleDrive;
// using System.Collections.Generic;
//
// public class GoogleDriveUploader : MonoBehaviour
// {
//     public IEnumerator UploadVideo(string filePath, System.Action<string> onComplete)
//     {
//         UnityEngine.Debug.Log("Start upload video");
//
//         var file = new UnityGoogleDrive.Data.File
//         {
//             Name = Path.GetFileName(filePath),
//             MimeType = "video/mp4",
//             Content = File.ReadAllBytes(filePath),
//             Parents = new List<string> { "1l3pIPmm4NA3lAExtH1gGgcwqMyUfOSOm" } 
//         };
//
//         var request = GoogleDriveFiles.Create(file);
//         UnityEngine.Debug.Log("Create file...." + request);
//         yield return request.Send();
//         UnityEngine.Debug.Log("Upload...." + request.Error);
//
//         if (!string.IsNullOrEmpty(request.Error))
//         {
//             Debug.LogError("Upload failed: " + request.Error);
//             onComplete?.Invoke(null); 
//         }
//         else
//         {
//             string fileId = request.ResponseData.Id;
//             Debug.Log("Upload successful! File ID: " + fileId);
//
//             string fileLink = "https://drive.google.com/file/d/" + fileId;
//             Debug.Log("File link: " + fileLink);
//
//             onComplete?.Invoke(fileId); 
//         }
//     }
// }
