using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class InteractController : MonoBehaviour
{
    public static InteractController Inst;
    
    [SerializeField] private GameObject interactButton;
    [SerializeField] private TextMeshProUGUI txt;

    private void Awake()
    {
        Inst = this;
        HideDown();
    }

    public void ShowUp(string text="E")
    {
        interactButton.SetActive(true);
        txt.text = text;
    }

    public void HideDown()
    {
        interactButton.SetActive(false);
    }
}
