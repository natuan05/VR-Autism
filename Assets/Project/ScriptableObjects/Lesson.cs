using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Topic
{
    public int id;
    public string name;
    public List<Lesson> lessons;
}

[Serializable]
public class Lesson
{
    public int lesson_index;
    public string lesson_id;
    public int topicId;
    public string sceneName;
    public string lesson_name;
    public LessonType type;
    public string description;     
    public Sprite cover;
    public List<float> sessionTimes;
    public int level_index;
    public int level_id;
    public string level_name;
}

public enum LessonType
{
    practical,
    theoretical
}


