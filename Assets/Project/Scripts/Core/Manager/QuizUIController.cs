using VRAutism.Core;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using UnityEngine.UI;
public class QuizUIController : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI questionText;

    [SerializeField]
    private TextMeshProUGUI score;
    [SerializeField]
    private TextMeshProUGUI time_completed;
    
    [Header("UI Buttons")]
    [SerializeField]
    private Button[] answerButtons;

    [SerializeField]
    private Color defaultColor = Color.white; 
    [SerializeField]
    private Color correctColor = Color.green; 
    [SerializeField]
    private Color wrongColor = Color.red;
    [SerializeField]
    private Color flashColor = Color.yellow;

    public void SetupUIForQuestion(QuizConfig.QuestionData question)
    {
        questionText.text = question.question;

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
    }

    public void ShowFinalTime(double elapsedSeconds)
    {
        int minutes = Mathf.FloorToInt((float)elapsedSeconds / 60);
        int seconds = Mathf.FloorToInt((float)elapsedSeconds % 60);

        time_completed.text = "Thời gian: " + minutes.ToString("00") + ":" + seconds.ToString("00");
    }

    public void UpdateScoreText(int currentScore)
    {
        score.text = "Số câu trả lời đúng: " + currentScore;
    }

    public void HandleSubmittedAnswer(int selectedAnswerIndex, int correctAnswerIndex, bool isCorrect, int currentScore)
    {
        var selectedButton = answerButtons[selectedAnswerIndex];
        
        if (isCorrect)
        {
            selectedButton.image.color = correctColor;
        }
        else
        {
            selectedButton.image.color = wrongColor;
        }
        
        UpdateScoreText(currentScore);

        var correctButton = answerButtons[correctAnswerIndex];

        // Dùng DOTween để chớp màu
        correctButton.image.DOColor(flashColor, 0.2f)
            .SetLoops(5, LoopType.Yoyo)
            .OnComplete(() => correctButton.image.color = correctColor);

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

    public void StopAllEffects()
    {
        foreach (var button in answerButtons)
        {
            button.image.DOKill(); // Dừng tất cả hiệu ứng DOTween trên nút
            ResetButtonState(button);
        }
    }


}


