using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Inst;

    
    private void Awake()
    {
        Inst = this;
        DontDestroyOnLoad(this);
        
        
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }
    }
    
}

[Serializable]
class MissionConfiguration
{
    public string MissionName;
    public int Level;
    public string Type;
}
