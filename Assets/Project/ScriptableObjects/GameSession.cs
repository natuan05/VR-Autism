using System;
using UnityEngine;

[Serializable]
public class GameSession
{
    public static GameSession Instance { get; private set; } = new GameSession();

    public int LessonID { get; set; }
    public string EnvironmentType { get; set; }
    public int Level { get; set; }

    private GameSession() { }  

    public void ResetSession()
    {
        EnvironmentType = "NoDistractor";
        Level = 1;
    }
}
