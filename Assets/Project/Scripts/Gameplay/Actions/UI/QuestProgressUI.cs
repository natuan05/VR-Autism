using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRAutism.Gameplay.Actions
{
    public class QuestProgressUI : MonoBehaviour
    {
        [SerializeField] private Slider bar;

        public void SetProgress(float progress)
        {
            bar.value = progress;
        }
    }
}

