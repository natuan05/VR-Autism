using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using UnityEngine.Serialization;

public class ChooseTypeUI : MonoBehaviour
{
    public GameObject chooseLevelPanel;
    public GameObject lessonDetailPanel;
    public ChooseLevelUI chooseLevelUI;
    public TextMeshProUGUI missionText;
    public Button distractorButton; 
    public Button noDistractorButton; 
    public Button nextButton;
    public Button closeButton;
    public Button backButton;

    private void Awake()
    {
        gameObject.SetActive(false);
        distractorButton.onClick.AddListener(() => SetEnvironmentType("Distractor"));
        noDistractorButton.onClick.AddListener(() => SetEnvironmentType("NoDistractor"));
        nextButton.onClick.AddListener(ProceedToNextStep);
        closeButton.onClick.AddListener(HideChooseTypePanel);
        backButton.onClick.AddListener(BackLessonDetail);
    }

    public void ShowChooseTypePanel(string mission)
    {
        missionText.text = "Nhiệm vụ: " + mission;
        gameObject.SetActive(true);
    }

    private void SetEnvironmentType(string type)
    {
        GameSession.Instance.EnvironmentType = type;
        Debug.Log("Environment Type Set to: " + type);
    }

    private void ProceedToNextStep()
    {
        chooseLevelUI.ShowChooseLevelPanel(missionText.text);
        gameObject.SetActive(false);
        chooseLevelPanel.SetActive(true);
    }
    void HideChooseTypePanel()
    {
        gameObject.SetActive(false);
    }

    void BackLessonDetail()
    {
        gameObject.SetActive(false);
        lessonDetailPanel.SetActive(true);
    }    
}
