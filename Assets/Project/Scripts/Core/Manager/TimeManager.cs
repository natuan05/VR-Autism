using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using VRAutism.Core;
using UnityEngine;
using VRAutism.Quests;
using Debug = UnityEngine.Debug;

namespace VRAutism.Core
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance;
        [SerializeField] private DoubleVariable lessonTime;
        [SerializeField] private FirebaseManager firebaseManager;
        //[SerializeField] private VideoRecorder videoRecorder;
        //[SerializeField] private GoogleDriveUploader uploader;
        [SerializeField] private QuestController questController;
        [SerializeField] private LessonInfo lessonInfo;

        private Stopwatch timer;
        
        private LessonTimeData data;

        private DateTime start_time; 
        private DateTime end_time;
        


        private void Awake()
        {
            Instance = this;
            
            data = new LessonTimeData();
            if (lessonInfo != null)
            {
                data.lesson_name = lessonInfo.lesson_name;
                data.level_name = lessonInfo.level_name;
                data.lesson_index = lessonInfo.lesson_index;
                data.level_index = lessonInfo.level_index;
                data.lesson_id = lessonInfo.lesson_id;
                data.type = lessonInfo.type == LessonType.theoretical ? "theoretical" : "practical";
            }
            else
            {
                Debug.LogError("LessonInfo chưa được gán trong Inspector!");
            }
        }

        private void Start()
        {
            start_time = DateTime.Now; 
            data.start_time = start_time.ToString("yyyy-MM-ddTHH:mm:ss");


            string device_id = SystemInfo.deviceUniqueIdentifier;
            Debug.Log("Device ID: " + device_id);
            data.device_id = device_id;
            
            data.quest_list = new List<QuestTimeData>();
            var questNames = new List<string>();
            if (ActionManager.Instance != null)
            {
                questNames.AddRange(ActionManager.Instance.GetQuestName());
            }
            else
            {
                Debug.LogError("ActionManager is null!");
            }
            
            if (questController != null)
            {
                questNames.AddRange(questController.GetAllQuestNames());
            }
            else
            {
                Debug.LogError("QuestController chưa được gán vào TimeManager!");
            }
            
            for (int i = 0; i < questNames.Count; i++)
            {
                QuestTimeData questData = new QuestTimeData
                {
                    index = i,
                    quest_name = questNames[i]
                };
                data.quest_list.Add(questData);
                    
            }
            Debug.Log("Danh sách QuestTime:");
            foreach (var quest in data.quest_list)
            {
                Debug.Log($"ID: {quest.index}, Name: {quest.quest_name}");
            }


            DataUtils<LessonTimeData>.SaveData(Application.persistentDataPath + "/Data/Saved/test.txt", data);

        }

        public void StartLessonTime()
        {
            //videoRecorder.StartRecording();
            timer = new Stopwatch();
            timer.Start();
            UnityEngine.Debug.Log("Lesson started at: " + start_time.ToString("yyyy-MM-dd HH:mm:ss"));
            lessonTime.Value = TimeUtils.CurrentSecond;

            StartCoroutine(TrackSkillUpdate());
        }


        public void StartQuestTime()
        {
            data.hasQuest = true;
            data.quest_list = new List<QuestTimeData>();
        }

        public void AddQuestTime(QuestTimeData questData)
        {
            data.quest_list.Add(questData);
        }

        public void SaveDurationTime()
        {
            end_time = DateTime.Now;
            data.finish_time = end_time.ToString("yyyy-MM-ddTHH:mm:ss");
            data.duration = timer.Elapsed.TotalMilliseconds / 1000;
            firebaseManager.UpdateSessionData("finish_time", data.finish_time);
            firebaseManager.UpdateSessionData("duration", data.duration);
        }

        public void SaveLessonTimeData()
        {
            Debug.LogError("Quest: " + data.quest_list.Count);
            timer.Stop();
            SaveDurationTime();
            //videoRecorder.StopRecording();
            //string videoPath = videoRecorder.GetVideoPath();

           /* StartCoroutine(uploader.UploadVideo(videoPath, (fileId) =>
            {
                UnityEngine.Debug.Log("Start upload video");
                /*firebaseManager.Savevideo_urlToFirebase("student_001", "WashingHand", fileId);
                data.video_url = "https://drive.google.com/file/d/" + fileId + "/preview";
                DataUtils<LessonTimeData>.SaveData(Application.persistentDataPath + "/Data/Saved/test.txt", data);

                firebaseManager.UpdateSessionData("video_url", data.video_url);
                //firebaseManager.UploadLessonTimeData();
            }));*/

            //data.duration = TimeUtils.CurrentSecond - lessonTime.Value;
            
            // string filePath = Application.persistentDataPath + "/Data/Saved/test.txt";
            // File.WriteAllText(filePath, JsonUtility.ToJson(data, true));

            //firebaseManager.UploadLessonTimeData();

        }

        // IEnumerator: Kiểu trả về cho Coroutine (hàm có thể tạm dừng)
        private IEnumerator TrackSkillUpdate()
        {
            //=================================================================
            // GIAI ĐOẠN 1: Chờ 60 giây đầu (không làm gì)
            //=================================================================
            while (timer.Elapsed.TotalSeconds < 60)
            {
                yield return null;  // Chờ 1 frame, rồi check lại điều kiện
            }
            // → Thoát khỏi while khi timer >= 60 giây

            //=================================================================
            // GIAI ĐOẠN 2: Chạy mãi mãi, mỗi giây check 1 lần
            //=================================================================
            while (true)
            {
                yield return new WaitForSeconds(1f);  // Chờ 1 giây
                
                // Kiểm tra điều kiện: Đã đủ 1 phút mới chưa?
                if (timer.Elapsed.TotalSeconds >= 60 &&
                    (timer.Elapsed.TotalSeconds - 60) >= data.skills.Count * 60)
                {
                    // Tạo SkillsData mới (tất cả = 0)
                    SkillsData newSkill = new SkillsData
                    {
                        initiation = 0,
                        negotiation = 0,
                        self_identity = 0,
                        cognitive_flexibility = 0
                    };
                    
                    // Thêm vào danh sách
                    data.skills.Add(newSkill);
                    
                    // Gửi lên Firebase
                    firebaseManager.PushNewSkillData(newSkill, data.skills.Count - 1);
                    
                    // Log ra console
                    Debug.Log($"Thêm SkillsData mới sau {timer.Elapsed.TotalSeconds} giây");
                }
            }
        }
    }

    [Serializable]
    public class LessonTimeData
    {
        public String lesson_name;
        public String level_name;
        public int lesson_index;
        public String lesson_id;
        public int level_index;
        public double duration;
        public bool hasQuest;
        public String start_time;
        public String finish_time;
        public String video_url;
        public List<QuestTimeData> quest_list;
        public List<SkillsData> skills = new List<SkillsData>
        {
            new SkillsData
            {
                initiation = 0,
                negotiation = 0,
                self_identity = 0,
                cognitive_flexibility = 0
            }
        };

        public String device_id;
        public String type;
        public int score;


    }

    [Serializable]
    public class QuestTimeData
    {
        public int index;
        public string quest_name;
        public double response_time;
        public int hint_count;
    }

    [Serializable]
    public class SkillsData
    {
        public int initiation;
        public int negotiation;
        public int self_identity;
        public int cognitive_flexibility;
    }

}



