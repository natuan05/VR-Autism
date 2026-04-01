using VRAutism.Core;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class QuizController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private QuestionCollection questionCollection;
    [SerializeField] private QuizUIController uiController;
    [SerializeField] private SoundManager soundManager;
    [SerializeField] private Button nextQuestionButton;
    [SerializeField] private GameObject gameover;
    
    [Header("Quiz Settings")]
    [SerializeField] private string quizConfigName = "QuizConfig";
    [SerializeField] private AudioClip introAudioClip;
    [SerializeField] private IntVariable quiz_score;

    private QuizConfig.QuestionData currentQuestion;
    private GameObject currentAssociatedObject;
    private int currentQuestionIndex = 0;
    
    private readonly TypeSound winSound = TypeSound.Win;
    private readonly TypeSound loseSound = TypeSound.Lose;

    private void Start()
    {
        gameover.SetActive(false);
        nextQuestionButton.gameObject.SetActive(false);
        nextQuestionButton.onClick.AddListener(OnNextQuestionClicked);
        
        quiz_score.Value = 0;
        uiController.UpdateScoreText(quiz_score.Value);
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

        if (currentAssociatedObject != null)
        {
            Destroy(currentAssociatedObject);
        }

        currentQuestion = questionCollection.GetNextQuestion();

        if (currentQuestion != null)
        {
            if (currentQuestion.associatedObject != null)
            {
                currentAssociatedObject = Instantiate(currentQuestion.associatedObject);
                currentAssociatedObject.SetActive(true);
            }
            
            uiController.SetupUIForQuestion(currentQuestion);
            nextQuestionButton.gameObject.SetActive(false);
            
            TimeManager.Instance.MarkQuestStart(); // Bấm giờ báo danh Quest
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
        if (isCorrect) quiz_score.Value++;
        
        TimeManager.Instance.LogQuestComplete(currentQuestionIndex, currentQuestion.question, isCorrect ? "success" : "failed");
        currentQuestionIndex++;
        
        uiController.HandleSubmittedAnswer(answerNumber, currentQuestion.correctAnswer, isCorrect, quiz_score.Value);
        
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
        
        if (currentAssociatedObject != null)
        {
            Destroy(currentAssociatedObject);
        }
        
        uiController.ShowFinalTime(TimeManager.Instance.GetTotalElapsedSeconds());
        soundManager.StopLoopingSound();
        TimeManager.Instance.SaveLessonTimeData("success", quiz_score.Value);
        
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
