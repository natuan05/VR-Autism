using VRAutism.Core;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class QuizController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Button nextQuestionButton;
    [SerializeField] private GameObject gameover;
    
    [Header("Quiz Settings")]
    [SerializeField] private string quizConfigName = "QuizConfig";
    [SerializeField] private AudioClip introAudioClip;

    private QuestionCollection questionCollection;
    private QuizConfig.QuestionData currentQuestion;
    private QuizUIController uiController;
    private SoundManager soundManager;
    
    private readonly TypeSound winSound = TypeSound.Win;
    private readonly TypeSound loseSound = TypeSound.Lose;

    private void Awake()
    {
        questionCollection = FindFirstObjectByType<QuestionCollection>();
        uiController = FindFirstObjectByType<QuizUIController>();
        soundManager = FindFirstObjectByType<SoundManager>();
    }

    private void Start()
    {
        gameover.SetActive(false);
        nextQuestionButton.gameObject.SetActive(false);
        nextQuestionButton.onClick.AddListener(OnNextQuestionClicked);
        
        questionCollection.LoadQuizConfig(quizConfigName);
        TimeManager.Instance.StartLessonTime();

        StartCoroutine(PlayIntroAndStartQuiz());
    }

    private IEnumerator PlayIntroAndStartQuiz()
    {
        yield return new WaitForSeconds(2f); 

        soundManager.PlayAudioClip(introAudioClip);
        yield return new WaitForSeconds(introAudioClip.length); 

        PresentQuestion(); 
    }

    private void PresentQuestion()
    {
        uiController.StopAllEffects();
        currentQuestion = questionCollection.GetNextQuestion();

        if (currentQuestion != null)
        {
            uiController.SetupUIForQuestion(currentQuestion);
            nextQuestionButton.gameObject.SetActive(false);
            StartCoroutine(HandleQuestionSounds());
        }
        else
        {
            Debug.Log("[QuizController] No more questions available.");
            EndQuiz(); 
        }
    }

    public void SubmitAnswer(int answerNumber)
    {
        bool isCorrect = (answerNumber == currentQuestion.correctAnswer);
        uiController.HandleSubmittedAnswer(answerNumber, currentQuestion.correctAnswer);
        
        soundManager.PlaySound(isCorrect ? winSound : loseSound);
        
        nextQuestionButton.gameObject.SetActive(true);
    }

    private void OnNextQuestionClicked()
    {
        soundManager.StopLoopingSound();
        PresentQuestion(); 
    }

    private void EndQuiz()
    {
        Debug.Log("[QuizController] Quiz ended.");
        uiController.ShowFinalTime();
        soundManager.StopLoopingSound();
        TimeManager.Instance.SaveLessonTimeData();
        
        gameover.SetActive(true);
        nextQuestionButton.gameObject.SetActive(false);
    }

    private IEnumerator HandleQuestionSounds()
    {
        if (currentQuestion.questionSound != TypeSound.None)
        {
            Debug.Log("[QuizController] Playing question sound: " + currentQuestion.questionSound);
            soundManager.PlaySound(currentQuestion.questionSound);
            yield return new WaitUntil(() => !soundManager.IsPlaying());
        }

        yield return new WaitForSeconds(0.5f);

        Debug.Log("[QuizController] Playing animal sound: " + currentQuestion.animalSound);
        soundManager.PlaySound(currentQuestion.animalSound);
    }
}
