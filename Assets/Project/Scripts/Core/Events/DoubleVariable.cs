using UnityEngine;

namespace VRAutism.Core
{
    [CreateAssetMenu(fileName = "DoubleVariable", menuName = "Variables/DoubleVariable")]
    public class DoubleVariable : ScriptableObject
    {
        public double Value { get; set; }
    }
}

