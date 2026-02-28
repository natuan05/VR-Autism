using UnityEngine;

[CreateAssetMenu(fileName = "LessonInfo", menuName = "LessonInfo")]
public class LessonInfo : ScriptableObject
{
    public string lesson_name;
    public string level_name;
    public int lesson_index;
    public int level_index;
    public string lesson_id;
    public string level_id;
    public LessonType type;
}
