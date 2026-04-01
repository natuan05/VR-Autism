using UnityEngine;

public class QuestionCollection : MonoBehaviour
{
    private QuizConfig quizConfig;
    private int currentIndex = 0; 

    private void Awake()
    {
    }

    public void LoadQuizConfig(string configName)
    {
        quizConfig = Resources.Load<QuizConfig>(configName);
        if (quizConfig == null || quizConfig.Questions.Count == 0)
        {
            Debug.LogError($"No QuizConfig asset found with name {configName} in Resources");
        }
        else
        {
            Debug.Log($"Loaded QuizConfig: {configName}");
        }
    }

    public QuizConfig.QuestionData GetNextQuestion()
    {
        if (currentIndex >= quizConfig.Questions.Count)
        {
            Debug.LogWarning("No more questions available.");
            return null;
        }

        var question = quizConfig.Questions[currentIndex];
        currentIndex++; 
        return question;
    }

    public void ResetQuestions()
    {
        currentIndex = 0; 
    }
}
