using UnityEngine;

namespace VRAutism.Core
{
    [CreateAssetMenu(fileName = "FloatVariable", menuName = "Variables/FloatVariable")]
    public class FloatVariable : ScriptableObject
    {
        public float Value { get; set; }
    }
}

