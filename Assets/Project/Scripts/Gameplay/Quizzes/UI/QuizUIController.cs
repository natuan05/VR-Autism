using VRAutism.Core;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Owns all Canvas/UI elements for the quiz.
/// Exposes events instead of public methods for answer submission and navigation,
/// keeping QuizController free of direct UI references.
/// </summary>
public class QuizUIController : MonoBehaviour
{
    [Header("Text Display")]
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI timeCompletedText;

    [Header("Navigation")]
    [SerializeField] private Button nextQuestionButton;
    [SerializeField] private GameObject gameoverPanel;

    [Header("UI Buttons")]
    [SerializeField] private Button[] answerButtons;

    [Header("Colors")]
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private Color correctColor = Color.green;
    [SerializeField] private Color wrongColor   = Color.red;
    [SerializeField] private Color flashColor   = Color.yellow;

    // Cached button labels — avoids GetComponentInChildren on every question
    private TextMeshProUGUI[] _buttonLabels;

    // Events raised to QuizController — UI knows nothing about game logic
    public event Action OnNextClicked;
    public event Action<int> OnAnswerSelected;

    private void Awake()
    {
        // Cache TMP references once
        _buttonLabels = new TextMeshProUGUI[answerButtons.Length];
        for (int i = 0; i < answerButtons.Length; i++)
        {
            _buttonLabels[i] = answerButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            int index = i; // capture for lambda closure
            answerButtons[i].onClick.AddListener(() => OnAnswerSelected?.Invoke(index));
        }

        nextQuestionButton.onClick.AddListener(() => OnNextClicked?.Invoke());
    }

    // ─── Public API called by QuizController ───────────────────────────────

    public void Initialize()
    {
        gameoverPanel.SetActive(false);
        HideNextButton();
    }

    public void SetupUIForQuestion(QuizQuestionData question)
    {
        questionText.text = question.question;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            bool hasAnswer = i < question.answers.Length;
            answerButtons[i].gameObject.SetActive(hasAnswer);

            if (hasAnswer)
            {
                _buttonLabels[i].text = question.answers[i];
                ResetButtonState(answerButtons[i]);
            }
        }

        ToggleAnswerButtons(true);
    }

    public void HandleSubmittedAnswer(int selectedIndex, int correctIndex, bool isCorrect, int currentScore)
    {
        answerButtons[selectedIndex].image.color = isCorrect ? correctColor : wrongColor;

        UpdateScoreText(currentScore);

        // DOTween flash on the correct button
        answerButtons[correctIndex].image
            .DOColor(flashColor, 0.2f)
            .SetLoops(5, LoopType.Yoyo)
            .OnComplete(() => answerButtons[correctIndex].image.color = correctColor);

        ToggleAnswerButtons(false);
    }

    public void UpdateScoreText(int currentScore)
    {
        scoreText.text = $"Số câu trả lời đúng: {currentScore}";
    }

    public void ShowGameOver(double elapsedSeconds)
    {
        ShowFinalTime(elapsedSeconds);
        gameoverPanel.SetActive(true);
        HideNextButton();
    }

    public void ShowNextButton()  => nextQuestionButton.gameObject.SetActive(true);
    public void HideNextButton()  => nextQuestionButton.gameObject.SetActive(false);

    public void StopAllEffects()
    {
        foreach (var button in answerButtons)
        {
            button.image.DOKill();
            ResetButtonState(button);
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────

    private void ShowFinalTime(double elapsedSeconds)
    {
        int minutes = Mathf.FloorToInt((float)elapsedSeconds / 60);
        int seconds = Mathf.FloorToInt((float)elapsedSeconds % 60);
        timeCompletedText.text = $"Thời gian: {minutes:00}:{seconds:00}";
    }

    private void ToggleAnswerButtons(bool interactable)
    {
        foreach (var button in answerButtons)
            button.interactable = interactable;
    }

    private void ResetButtonState(Button button)
    {
        button.image.color = defaultColor;
        button.interactable = true;
    }
}
