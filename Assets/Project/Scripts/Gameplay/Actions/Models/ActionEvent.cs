using System;
using UnityEngine.Events;
using VRAutism.Core; // Nếu BooleanVariable nằm ở đây

namespace VRAutism.Gameplay.Actions
{
    [Serializable]
    public class ActionEvent
    {
        public string name; 
        public bool on; 
        public float duration; 
        public UnityEvent onStart; 
        public UnityEvent onFinished; 
        public BooleanVariable isConditionMet;
    }
}