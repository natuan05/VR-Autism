using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RunMonitor : BaseMono
{
    private static RunMonitor _instance;
    [SerializeField] private SceneSO sceneSO;
    
    private ExitScene _exitScene;

    private Action<object> OnChangeScene;
    private Action<object> OnExitScene;

    protected override void Initialize()
    {
        if (_instance != null)
        {
            Destroy(this);
            return;
        }
        
        _instance = this;
        transform.SetParent(null);        // Đảm bảo là root GameObject
        DontDestroyOnLoad(gameObject);
        
        OnChangeScene = param => LoadScene((SceneEnum)param);
        OnExitScene = param => ExitScene();
    }
    
    protected override void ListenEvents()
    {
        this.SubscribeListener(EventID.ChangeScene, OnChangeScene);
        this.SubscribeListener(EventID.ExitScene, OnExitScene);
    }
    
    protected override void StopListeningEvents()
    {
        this.UnsubscribeListener(EventID.ChangeScene, OnChangeScene);
        this.UnsubscribeListener(EventID.ExitScene, OnExitScene);
    }
    
    private void LoadScene(SceneEnum sceneEnum)
    {
        SceneManager.LoadScene(sceneSO.GetSceneName(sceneEnum));
    }

    public void ExitScene()
    {
        var exitScene = FindFirstObjectByType<ExitScene>(FindObjectsInactive.Include);
        if (exitScene == null) return;
        exitScene.ShowUp();
    }
}
