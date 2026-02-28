using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class NavUI : MonoBehaviour
{
    [SerializeField] private List<NavTab> tabs;

    private void Awake()
    {
        foreach (var tab in tabs)
        {
            tab.content.SetActive(tab.btnType == BtnType.NavLearn);
        }
    }

    public void OnClickLearnTab()
    {
        Toggle(BtnType.NavLearn);
    }
    
    public void OnClickReviewTab()
    {
        Toggle(BtnType.NavReview);
    }
    
    public void OnClickSettingTab()
    {
        Toggle(BtnType.NavSetting);
    }

    private void Toggle(BtnType btnType)
    {
        foreach (var tab in tabs)
        {
            tab.content.SetActive(tab.btnType == btnType);
            tab.button.Toggle(btnType);
        }
    }

    public void OnClickQuit()
    {
        Toggle(BtnType.NavQuit);
        Application.Quit();
    }
}

[Serializable]
public class NavTab
{
    public BtnType btnType;
    public BtnUI button;
    public GameObject content;
}

public enum BtnType
{
    NavLearn,
    NavReview,
    NavSetting,
    NavQuit
}
