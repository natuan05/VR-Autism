// using UnityEditor.Recorder;
// using UnityEditor;
// using UnityEngine;
// using System.IO;
//
// public class VideoRecorder : MonoBehaviour
// {
//     private RecorderController recorderController;
//     private string videoPath;
//
//     public string GetVideoPath()
//     {
//         return videoPath + ".mp4";
//     }
//
//     public void StartRecording()
//     {
//         if (recorderController != null && recorderController.IsRecording())
//         {
//             Debug.Log("Recording is already in progress.");
//             return;
//         }
//
//         var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
//         recorderController = new RecorderController(controllerSettings);
//
//         videoPath = Application.persistentDataPath + "/Data/Video/LessonVideo_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") ;
//
//         var mediaOutput = ScriptableObject.CreateInstance<MovieRecorderSettings>();
//         mediaOutput.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
//         mediaOutput.VideoBitRateMode = VideoBitrateMode.High;
//         mediaOutput.OutputFile = videoPath;
//         mediaOutput.FrameRate = 30;
//
//         controllerSettings.AddRecorderSettings(mediaOutput);
//         controllerSettings.SetRecordModeToManual();
//         controllerSettings.FrameRate = 30;
//
//         recorderController.PrepareRecording();
//         recorderController.StartRecording();
//
//         Debug.Log("Recording started. Saving to: " + videoPath);
//     }
//
//     public void StopRecording()
//     {
//         if (recorderController == null || !recorderController.IsRecording())
//         {
//             Debug.Log("No recording in progress.");
//             return;
//         }
//
//         recorderController.StopRecording();
//         Debug.Log("Recording stopped. Video saved at: " + videoPath);
//     }
// }
