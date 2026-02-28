using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class CompleteMission : MonoBehaviour
{
    public static CompleteMission Inst;
    [SerializeField] private Transform missionTxt;

    private void Awake()
    {
        Inst = this;
        gameObject.SetActive(false);
    }
    
    public void ShowUp()
    {
        transform.localScale = Vector3.zero;
        missionTxt.localScale = new Vector3(0, 1, 1);
        gameObject.SetActive(true);
        transform.DOScale(Vector3.one, 0.5f).SetEase(Ease.InOutQuad).OnComplete(() =>
        {
            missionTxt.DOScaleX(1, 0.5f).SetEase(Ease.InOutQuad);
        });
    }
}
