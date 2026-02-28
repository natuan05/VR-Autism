using System;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public class ExitScene : BaseMono
{
    [SerializeField] private bool fixedPos;
    [SerializeField] private float distToCam;
    [SerializeField] private Camera cam;

    protected override void Initialize()
    {
        base.Initialize();
        gameObject.SetActive(false);
    }

    public void ShowUp()
    {
        if (fixedPos)
        {
            gameObject.SetActive(true);
            return;
        }
        
        
        if (cam == null)
        {
            Debug.LogWarning("No camera assigned to ExitScene");
            return;
        }

        var targetPosition = cam.transform.position + cam.transform.forward * distToCam;
        transform.position = targetPosition;

        transform.LookAt(cam.transform);
        transform.Rotate(0, 180, 0); 
        
        gameObject.SetActive(true);
    }

    public void OnClickExit()
    {
        this.SendEvent(EventID.ChangeScene, SceneEnum.GameMenu);
    }

    public void OnClickRetry()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
