using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseMono : MonoBehaviour
{
    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        ListenEvents();
    }

    private void OnDisable()
    {
        StopListeningEvents();
    }

    private void Update()
    {
        Tick();
    }


    protected virtual void Initialize() { }
    protected virtual void ListenEvents() { }

    protected virtual void StopListeningEvents() { }

    protected virtual void Tick() { }
}
