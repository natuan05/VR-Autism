/// <summary>
/// Pure C# data model for a single quiz question.
/// Text and answer fields are JSON/Firestore-serializable.
/// Sound fields use TypeSound enum for type-safety — migrate to string keys only when Firestore integration is needed.
/// </summary>
[System.Serializable]
public class QuizQuestionData
{
    public string question;
    public string[] answers;
    public int correctAnswer;

    // String key resolved at runtime — decoupled from Unity prefab asset
    public string associatedObjectKey;

    // Keep as enum for now: type-safe, Inspector dropdown, no parse errors
    public TypeSound questionSound;
    public TypeSound animalSound;
}
