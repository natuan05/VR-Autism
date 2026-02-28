using System;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ReportController : MonoBehaviour
{
    public static ReportController Instance;
    public GameObject reportPanel;
    public GameObject lessonReportedPanel;
    public ReportDetailUI reportDetailUI;
    [SerializeField] private TopicReportedUI[] topics;
    public ReportConfig config;

    public Lesson Lesson { get; set; }
    public Action<object> ShowLessonReporteds;

    private void Start()
    {
        Debug.LogWarning("amen");
        Instance = this;
        reportPanel.SetActive(false);
        lessonReportedPanel.SetActive(true);

        //ShowLessonReporteds = param => ShowLessonReported((Lesson)param);
        ShowLessonReporteds = param =>
        {
            Lesson lesson = (Lesson)param;
            Topic topic = config.topics.Find(x => x.id == lesson.topicId); 
            ShowLessonReported(lesson, topic);
        };
        this.SubscribeListener(EventID.ShowLessonReported, ShowLessonReporteds);

        Init();
    }

    private void OnDestroy()
    {
        this.UnsubscribeListener(EventID.ShowLessonReported, ShowLessonReporteds);
    }


    private void Init()
    {
        //Debug.LogWarning("thu nheee" + string.Join(", ", config.topics[0].lessons[0].sessionTimes));
        for (var i = 0; i < topics.Length; i++)
        {
            topics[i].Init(config.topics.Find(x => x.id == i));
        }
    }

    public void ShowLessonReported(Lesson lesson, Topic topic)
    {
        if (lesson != null)
        {
            Lesson = lesson;
            lessonReportedPanel.SetActive(false);
            reportDetailUI.Show(lesson.lesson_name, lesson.description, topic.name, lesson.sessionTimes);

        }
        else
        {
            Debug.LogWarning("Report not found");
        }
    }
}