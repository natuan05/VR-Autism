using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "New Lesson Config", menuName = "Lesson Config")]
public class LessonConfig : ScriptableObject
{
    public List<Topic> topics;
}


