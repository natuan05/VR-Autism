using VRAutism.Core;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRAutism.Cloud;

public class QuizUIController : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI questionText;

    [SerializeField]
    private TextMeshProUGUI score;
    [SerializeField]
    private TextMeshProUGUI time_completed;
    [SerializeField] IntVariable quiz_score;
    [SerializeField]
    private FirebaseManager firebase;
    private float startTime;
    private float elapsedTime;
    private bool isTiming;
    [SerializeField]
    private Button[] answerButtons;

    private GameObject currentObject;

    [SerializeField]
    private Color defaultColor = Color.white; 
    [SerializeField]
    private Color correctColor = Color.green; 
    [SerializeField]
    private Color wrongColor = Color.red;
    [SerializeField]
    private Color flashColor = Color.yellow;

    private Coroutine currentCoroutine;
    

    public void SetupUIForQuestion(QuizConfig.QuestionData question)
    {
        questionText.text = question.question;

        if (!isTiming)
        {
            startTime = Time.time;
            quiz_score.Value = 0;
            isTiming = true;
        }

        for (int i = 0; i < answerButtons.Length; i++)
        {
            var button = answerButtons[i];
            if (i < question.answers.Length)
            {
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TextMeshProUGUI>().text = question.answers[i];
                ResetButtonState(button); 
            }
            else
            {
                button.gameObject.SetActive(false);
            }
        }
        if (currentObject != null)
        {
            Destroy(currentObject);
        }

        if (question.associatedObject != null)
        {
            currentObject = Instantiate(question.associatedObject);
            currentObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Associated Object is missing for question");
        }
    }

    public void ShowFinalTime()
    {
        elapsedTime = Time.time - startTime;
        int minutes = Mathf.FloorToInt(elapsedTime / 60);
        int seconds = Mathf.FloorToInt(elapsedTime % 60);

        time_completed.text = "Thời gian: " + minutes.ToString("00") + ":" + seconds.ToString("00");
    }

    public void HandleSubmittedAnswer(int selectedAnswerIndex, int correctAnswerIndex)
    {
        var selectedButton = answerButtons[selectedAnswerIndex];
        if (selectedAnswerIndex == correctAnswerIndex)
        {
            selectedButton.image.color = correctColor;
            quiz_score.Value++;
            firebase.UpdateSessionData("score", quiz_score.Value);
            score.text = "Số câu trả lời đúng: " + quiz_score.Value;
        }
        else
        {
            selectedButton.image.color = wrongColor;
        }

        var correctButton = answerButtons[correctAnswerIndex];
        //correctButton.image.color = correctColor;

        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
        }

        currentCoroutine = StartCoroutine(FlashCorrectAnswer(correctButton));

        ToggleAnswerButtons(false);
    }

    private void ToggleAnswerButtons(bool value)
    {
        for (int i = 0; i < answerButtons.Length; i++)
        {
            answerButtons[i].interactable = value; 
        }
    }

    private void ResetButtonState(Button button)
    {
        button.image.color = defaultColor; 
        button.interactable = true; 
    }

    private IEnumerator FlashCorrectAnswer(Button button)
    {
        float flashDuration = 1f; 
        float flashInterval = 0.2f; // Khoảng thời gian giữa các lần đổi màu

        float elapsedTime = 0f;
        bool toggle = false;

        while (elapsedTime < flashDuration)
        {
            button.image.color = toggle ? flashColor : correctColor;
            toggle = !toggle;

            elapsedTime += flashInterval;
            yield return new WaitForSeconds(flashInterval);
        }

        button.image.color = correctColor;
    }

    public void StopAllEffects()
    {
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
        }

        foreach (var button in answerButtons)
        {
            ResetButtonState(button);
        }
    }


}


