using VRAutism.Core;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class QuizController : MonoBehaviour
{
    private QuestionCollection questionCollection;
    private QuizConfig.QuestionData currentQuestion;
    private QuizUIController uiController;
    private SoundManager soundManager;
    public GameObject gameover;
    private TypeSound win = TypeSound.Win;
    private TypeSound lose = TypeSound.Lose;

    [SerializeField]
    private TimeManager timeManager;
    [SerializeField]
    private AudioClip introAudioClip;




    [SerializeField]
    private string quizConfigName = "QuizConfig";

    /*[SerializeField]
    private float delayBetweenQuestions = 3f;*/

    [SerializeField]
    private Button nextQuestionButton;

    private void Awake()
    {
        questionCollection = FindFirstObjectByType<QuestionCollection>();
        uiController = FindFirstObjectByType<QuizUIController>();
        soundManager = FindFirstObjectByType<SoundManager>();
    }

    private void Start()
    {
        gameover.SetActive(false);
        questionCollection.LoadQuizConfig(quizConfigName);
        nextQuestionButton.gameObject.SetActive(false);
        nextQuestionButton.onClick.AddListener(OnNextQuestionClicked);
        
        timeManager.StartLessonTime();


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
            Debug.Log("No more questions available.");
            EndQuiz(); 
        }
    }

    public void SubmitAnswer(int answerNumber)
    {
        bool isCorrect = answerNumber == currentQuestion.correctAnswer;
        uiController.HandleSubmittedAnswer(answerNumber, currentQuestion.correctAnswer);
        if (isCorrect)
        {
            soundManager.PlaySound(win);
        }
        else
        {
            soundManager.PlaySound(lose);
        }
        //StartCoroutine(ShowNextQuestionAfterDelay());
        nextQuestionButton.gameObject.SetActive(true);
    }

    private void OnNextQuestionClicked()
    {
        soundManager.StopLoopingSound();
        PresentQuestion(); 
    }

    /*private IEnumerator ShowNextQuestionAfterDelay()
    {
        yield return new WaitForSeconds(delayBetweenQuestions);
        PresentQuestion();
    }*/

    private void EndQuiz()
    {
        Debug.Log("Quiz ended.");
        uiController.ShowFinalTime();
        gameover.SetActive(true);
        nextQuestionButton.gameObject.SetActive(false);
        soundManager.StopLoopingSound();
        timeManager.SaveLessonTimeData();
    }

    private IEnumerator HandleQuestionSounds()
    {
        if (currentQuestion.questionSound != TypeSound.None)
        {
            Debug.Log("Playing question sound: " + currentQuestion.questionSound);
            soundManager.PlaySound(currentQuestion.questionSound);
            //float soundDuration = soundManager.GetSoundDuration(currentQuestion.questionSound);
            
            //Debug.Log("Waiting for: " + soundDuration + "s");
            yield return new WaitUntil(() => !soundManager.IsPlaying());
        }

        yield return new WaitForSeconds(0.5f);

        //if (currentQuestion.animalSound != TypeSound.None)
        //{
        //soundManager.PlaySoundLoop(currentQuestion.animalSound);
        Debug.Log("Playing animal sound: " + currentQuestion.animalSound);
        soundManager.PlaySound(currentQuestion.animalSound);
       
        // }
    }
}
