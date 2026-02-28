using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
     [SerializeField] private MissionManager missionManager;

     public void StartMission()
     {
          StartCoroutine(missionManager.StartMission());
     }
}
