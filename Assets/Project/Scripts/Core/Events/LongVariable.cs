using UnityEngine;

namespace VRAutism.Core
{
    [CreateAssetMenu(fileName = "LongVariable", menuName = "Variables/LongVariable")]
    public class LongVariable : ScriptableObject
    {
        public long Value { get; set; }
    }
}

