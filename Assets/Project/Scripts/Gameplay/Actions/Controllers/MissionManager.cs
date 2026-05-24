using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Inst;
    [SerializeField] private AudioClip demoMissionClip;
    [SerializeField] private RectTransform bgMission;
    [SerializeField] private RectTransform rawImage;
    
    [SerializeField] private TextMeshProUGUI timeText;

    public DateTime StartTime;
    public bool MissionCompleted { get; set; } = false;
    
    private void Awake()
    {
        Inst = this;
    }
    
    public IEnumerator StartMission()
    {
        yield return new WaitForSeconds(2f);
        AudioManager.Inst.Play(demoMissionClip);
        ShowUp();
    }

    private void Update()
    {
        if (!MissionCompleted)
        {
            var timeSpan = DateTime.Now - StartTime;
            var m = timeSpan.Hours * 60 + timeSpan.Minutes;
            timeText.text = "Tổng thời gian: " + m.ToString("00") + "m " + timeSpan.Seconds.ToString("00") + "s";
        }
    }

    private void ShowUp()
    {
        this.bgMission.localScale = Vector3.zero;
        this.bgMission.gameObject.SetActive(true);
        bgMission.DOScale(1, 0.5f).SetEase(Ease.InOutQuad);
        StartTime = DateTime.Now;
    }
}
