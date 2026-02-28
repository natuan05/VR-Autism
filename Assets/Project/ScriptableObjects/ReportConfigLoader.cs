/*using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ReportConfigLoader : MonoBehaviour
{
    [SerializeField] private ReportConfig reportConfig;

    void Awake()
    {
        GetComponent<ReportConfigLoader>().LoadFromJson("ReportData");
        Debug.LogWarning("load data");
        //LogLoadedData();
    }


    public void LoadFromJson(string fileName)
    {
        // Tải file JSON từ thư mục Resources
        TextAsset jsonTextAsset = Resources.Load<TextAsset>(fileName);

        if (jsonTextAsset == null)
        {
            Debug.LogError("File JSON không tồn tại: " + fileName);
            return;
        }

        // Chuyển đổi JSON thành đối tượng ReportData
        ReportData reportData = JsonUtility.FromJson<ReportData>(jsonTextAsset.text);

        // Gán dữ liệu từ ReportData vào ScriptableObject
        reportConfig.topics = new List<Topic>();

        foreach (var topicData in reportData.topics)
        {
            Topic topic = new Topic
            {
                id = topicData.id,
                name = topicData.name,
                lessons = new List<Lesson>()
            };

            foreach (var lessonData in topicData.lessons)
            {
                Lesson lesson = new Lesson
                {
                    lesson_index = lessonData.lesson_index,
                    topicId = lessonData.topicId,
                    sceneName = lessonData.sceneName,
                    title = lessonData.title,
                    description = lessonData.description,
                    cover = Resources.Load<Sprite>($"Thumbnails/{lessonData.cover}"),
                    sessionTimes = new List<float>(lessonData.sessionTimes)
                };

                topic.lessons.Add(lesson);
            }

            reportConfig.topics.Add(topic);
        }

        Debug.Log("Dữ liệu JSON đã được tải thành công vào ScriptableObject.");
    }


    public void LogLoadedData()
    {
        //Debug.LogWarning("hêhê");
        if (reportConfig == null || reportConfig.topics == null)
        {
            Debug.LogError("ReportConfig hoặc danh sách topics chưa được khởi tạo.");
            return;
        }

        foreach (var topic in reportConfig.topics)
        {
            Debug.Log($"Topic ID: {topic.id}, Name: {topic.name}");
            foreach (var lesson in topic.lessons)
            {
                Debug.LogWarning($"  Lesson ID: {lesson.id}, Title: {lesson.title}, Scene: {lesson.sceneName}");
                Debug.LogWarning($"  Description: {lesson.description}");
                Debug.LogWarning($"  Cover: {(lesson.cover != null ? lesson.cover.name : "No Cover Found")}");
                Debug.LogWarning($"  Session Times: {string.Join(", ", lesson.sessionTimes)}");
            }
        }
    }

}

// Lớp trung gian để ánh xạ JSON
[System.Serializable]
public class ReportData
{
    public List<TopicData> topics;
}

[System.Serializable]
public class TopicData
{
    public int id;
    public string name;
    public List<LessonData> lessons;
}

[System.Serializable]
public class LessonData
{
    public int id;
    public int topicId;
    public string sceneName;
    public string title;
    public string description;
    public string cover;
    public List<float> sessionTimes;
}

*/