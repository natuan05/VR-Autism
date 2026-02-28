using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Daark/SceneSO")]
public class SceneSO : ScriptableObject
{
    [SerializeField] public List<SceneObject> sceneLists;

    public string GetSceneName(SceneEnum sceneEnum)
    {
        var scene = sceneLists.Find(x => x.sceneEnum == sceneEnum);
        if (scene != null) return scene.name;
        return "GameMenu";
    }
}

[Serializable]
public class SceneObject {
    public SceneEnum sceneEnum;
    public string name;
}

public enum SceneEnum
{
    GameMenu,
    Supermarket,
    GrassLand,
    Farm,
    Ocean,
    TidyRoom,
    Bathroom,
    Classroom
}
