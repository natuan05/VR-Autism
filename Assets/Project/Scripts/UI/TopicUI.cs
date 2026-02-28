using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TopicUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI txtTopicName;
    [SerializeField] private GameObject lessonUIGroups;
    [SerializeField] private LessonUI lessonUI;

    public void Init(Topic topic)
    {
        txtTopicName.text = topic.name;
        
        var children = lessonUIGroups.GetComponentsInChildren<LessonUI>();
        foreach (var child in children)
        {
            Destroy(child.gameObject);
        }

        foreach (var lesson in topic.lessons)
        {
            Instantiate(lessonUI, lessonUIGroups.transform).Init(lesson);
        }
    }
}
