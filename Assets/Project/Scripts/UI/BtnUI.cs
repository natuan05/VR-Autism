using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BtnUI : MonoBehaviour
{
    [SerializeField] private BtnType btnType;
    [SerializeField] private GameObject active;

    
    public void Toggle(BtnType toggleType)
    {
        active.SetActive(toggleType == btnType);
    }
}
