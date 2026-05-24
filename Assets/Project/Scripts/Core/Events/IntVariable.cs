using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Core
{
    [CreateAssetMenu(fileName = "IntVariable", menuName = "Variables/IntVariable")]
    public class IntVariable : ScriptableObject
    {
        public int Value { get; set; }
    }
}
