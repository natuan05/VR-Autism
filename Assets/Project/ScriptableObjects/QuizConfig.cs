using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "QuizConfig", menuName = "QuizConfig")]
public class QuizConfig : ScriptableObject
{
    [System.Serializable]
    public class QuestionData
    {
        public string question;               
        public string[] answers;            
        public int correctAnswer;            
        public GameObject associatedObject;
        public TypeSound questionSound;
        public TypeSound animalSound;
        [HideInInspector] public bool asked = false; 
    }

    [SerializeField]
    private List<QuestionData> questions = new List<QuestionData>();

    public List<QuestionData> Questions => questions;
}
