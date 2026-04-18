using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject container for a quiz lesson.
/// Each QuestionEntry bridges the pure QuizQuestionData model
/// with Unity-specific assets (prefab, audio clip lookup).
/// </summary>
[CreateAssetMenu(fileName = "QuizConfig", menuName = "VR-Autism/Quiz Config")]
public class QuizConfig : ScriptableObject
{
    [System.Serializable]
    public class QuestionEntry
    {
        [Tooltip("Pure data — serializable to JSON/Firestore")]
        public QuizQuestionData data;

        [Tooltip("3D prefab to instantiate for this question")]
        public GameObject associatedObject;
    }

    [SerializeField]
    private List<QuestionEntry> questions = new List<QuestionEntry>();

    public List<QuestionEntry> Questions => questions;

    /// <summary>
    /// Converts this ScriptableObject into a list of pure data models.
    /// Used when loading from local asset; Firestore path bypasses this entirely.
    /// </summary>
    public List<QuizQuestionData> ToDataList()
    {
        var list = new List<QuizQuestionData>(questions.Count);
        foreach (var entry in questions)
        {
            list.Add(entry.data);
        }
        return list;
    }
}
