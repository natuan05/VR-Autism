using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRAutism.Quests
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

