using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TopicReportedUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI txtTopicName;
    [SerializeField] private GameObject lessonUIGroups;
    [SerializeField] private LessonReportedUI lessonReportedUI;

    public void Init(Topic topic)
    {
        txtTopicName.text = topic.name;

        var children = lessonUIGroups.GetComponentsInChildren<LessonReportedUI>();
        foreach (var child in children)
        {
            Destroy(child.gameObject);
        }

        foreach (var lesson in topic.lessons)
        {
            //Debug.LogWarning("heheheehehehhehehehe " + lesson.sessionTimes);
            Instantiate(lessonReportedUI, lessonUIGroups.transform).Init(lesson);
        }
    }
}
