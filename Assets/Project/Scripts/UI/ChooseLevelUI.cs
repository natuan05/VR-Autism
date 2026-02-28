using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using UnityEngine.SceneManagement;
using System.Collections;
using Newtonsoft.Json;

public class ChooseLevelUI : MonoBehaviour
{
    public GameObject lessonDetailPanel;
    public GameObject chooseTypePanel;
    public GameObject loadingPanel; 
    public Slider loadingSlider;
    public TextMeshProUGUI loadingText;
    public TextMeshProUGUI missionText;
    public Button level1;
    public Button level2;
    public Button level3;
    public Button closeButton;
    public Button backButton;

    void Start()
    {
        chooseTypePanel.SetActive(false);
        level1.onClick.AddListener(() => SetEnvironmentLevel(1));
        level2.onClick.AddListener(() => SetEnvironmentLevel(2));
        level3.onClick.AddListener(() => SetEnvironmentLevel(3));
        closeButton.onClick.AddListener(HideChooseLevelPanel);
        backButton.onClick.AddListener(BackChooseType);
    }

    public void ShowChooseLevelPanel(string mission)
    {
        missionText.text = mission;
        gameObject.SetActive(true);
    }

    private void SetEnvironmentLevel(int level)
    {
        GameSession.Instance.Level = level;
        Debug.Log("Level Set to: " + level);
    }

    void HideChooseLevelPanel()
    {
        gameObject.SetActive(false);
    }

    void BackChooseType()
    {
        gameObject.SetActive(false);
        chooseTypePanel.SetActive(true);
    }

    public void StartGame()
    {
        StartCoroutine(LoadSceneAsync());
    }

    private IEnumerator LoadSceneAsync()
    {
        var gameData = JsonConvert.SerializeObject(GameSession.Instance);
        PlayerPrefs.SetString("GameSession", gameData);
        var lesson = GetLesson();
        var sceneName = lesson.sceneName;
        //AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        var asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        Debug.Log("Loading scene: " + sceneName);

        // Hide the current panel and show the loading panel
        gameObject.SetActive(false);
        lessonDetailPanel.SetActive(false);
        chooseTypePanel.SetActive(false);
        loadingPanel.SetActive(true);
        //loadBackground.sprite = GameSession.Instance.BackgroundCover;

        // While the asynchronous operation to load the new scene is not yet complete, continue updating the slider
        while (!asyncLoad.isDone)
        {
            var progress = Mathf.Clamp01(asyncLoad.progress / 0.9f); 
            loadingSlider.value = progress;
            loadingText.text = (progress * 100).ToString("F0") + "%";
            yield return new WaitForSeconds(0.01f);
        }
    }

    private string GetSceneName()
    {
        return SceneMenuController.Instance.Lesson.sceneName;
    }

    private Lesson GetLesson()
    {
        //return SceneMenuController.Instance.Lesson;
        Lesson lesson = SceneMenuController.Instance.Lesson;

        Debug.Log($"Lesson info:\n" +
                  $"- lesson_index: {lesson.lesson_index}\n" +
                  $"- lesson_id: {lesson.lesson_id}\n" +
                  $"- topicId: {lesson.topicId}\n" +
                  $"- sceneName: {lesson.sceneName}\n" +
                  $"- lesson_name: {lesson.lesson_name}\n" +
                  $"- type: {lesson.type}\n" +
                  $"- description: {lesson.description}\n" +
                  $"- level_index: {lesson.level_index}\n" +
                  $"- level_id: {lesson.level_id}\n" +
                  $"- level_name: {lesson.level_name}");

        return lesson;
    }
}
