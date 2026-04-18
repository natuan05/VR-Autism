using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages quiz question sequencing.
/// Accepts QuizConfig (local SO) or a raw list of QuizQuestionData (from Firestore).
/// </summary>
public class QuestionCollection : MonoBehaviour
{
    private List<QuizQuestionData> _questions = new();
    private Dictionary<string, GameObject> _prefabLookup = new();
    private int _currentIndex = 0;

    /// <summary>Load from local ScriptableObject via Resources folder.</summary>
    public void LoadFromConfig(QuizConfig config)
    {
        if (config == null || config.Questions.Count == 0)
        {
            Debug.LogError("[QuestionCollection] QuizConfig is null or empty.");
            return;
        }

        _questions.Clear();
        _prefabLookup.Clear();

        foreach (var entry in config.Questions)
        {
            if (entry.associatedObject != null)
            {
                entry.data.associatedObjectKey = entry.associatedObject.name;
                _prefabLookup[entry.associatedObject.name] = entry.associatedObject;
            }
            _questions.Add(entry.data);
        }

        _currentIndex = 0;
        Debug.Log($"[QuestionCollection] Loaded {_questions.Count} questions from local config.");
    }

    /// <summary>
    /// Load from a pre-built data list (e.g. deserialized from Firestore JSON).
    /// Prefab lookup must be supplied separately via Addressables in this path.
    /// </summary>
    public void LoadFromData(List<QuizQuestionData> data)
    {
        _questions = data;
        _prefabLookup.Clear();
        _currentIndex = 0;
        Debug.Log($"[QuestionCollection] Loaded {_questions.Count} questions from remote data.");
    }

    public QuizQuestionData GetNextQuestion()
    {
        if (_currentIndex >= _questions.Count)
        {
            Debug.LogWarning("[QuestionCollection] No more questions available.");
            return null;
        }
        return _questions[_currentIndex++];
    }

    /// <summary>Resolve a prefab by the question's associatedObjectKey.</summary>
    public GameObject GetPrefab(string key)
    {
        _prefabLookup.TryGetValue(key, out var prefab);
        return prefab;
    }

    public void ResetQuestions() => _currentIndex = 0;

    public int TotalQuestions => _questions.Count;
}
