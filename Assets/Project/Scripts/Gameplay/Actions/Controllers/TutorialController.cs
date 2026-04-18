using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class TutorialController : MonoBehaviour
{
    public static TutorialController Inst;

    [SerializeField] private PlayableDirector shortDemo;

    public DemoTutorial state;

    private void Start()
    {
        Inst = this;
        state = DemoTutorial.IntroduceMission;
    }

    public void PlayShortDemo()
    {
        shortDemo.Play();
    }
}

public enum DemoTutorial
{
    IntroduceMission,
    OpenDoor,
}
