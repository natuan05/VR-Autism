using Unity.Collections;
using UnityEngine;

namespace VRAutism.Core
{
    public class BaseSO: ScriptableObject
    {
        [SerializeField, ReadOnly] private string id;
        public string Id => id;

        public void ResetId()
        {
            id = name;
        }
    }
}