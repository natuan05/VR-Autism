using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using System.Collections.Generic;

public class ReportDetailUI : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI topicText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI timesText;
    public Button closeButton;
    public GameObject lessonReportedPanel;

    [SerializeField] private Window_Graph windowGraph;


    private void Start()
    {
        closeButton.onClick.AddListener(Hide);
        Debug.LogWarning("hihihihihi report detail UI");
    }


    public void Show(string title, string description, string topic, List<float> sessionTimes)
    {
        
        titleText.text = "Tên bài học: " + title;
        descriptionText.text = "Mô tả bài học: " + description;
        topicText.text = "Chủ đề: " + topic;
        timesText.text = "Số lần tham gia học: " + sessionTimes.Count;
        Debug.LogWarning("hoho: " + sessionTimes);


        if (windowGraph != null)
        {
            Debug.LogWarning("huhu");
            windowGraph.ShowGraph(sessionTimes);
        }
        else
        {
            Debug.LogError("Window_Graph reference is missing in ReportDetailUI!");
        }

        gameObject.SetActive(true);
    }


    void Hide()
    {
        gameObject.SetActive(false);
        lessonReportedPanel.SetActive(true);
    }


}
