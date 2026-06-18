using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using VRAutism.Cloud.Models;
using VRAutism.Cloud;
using UnityEngine;
using UnityEngine.Events;

namespace VRAutism.Gameplay.Actions
{
    public class ActionManager : MonoBehaviour
    {
        public static ActionManager Instance { get; private set; }

        [SerializeField] private List<ActionEvent> actionEvents;

        void Awake()
        {
            Instance = this;
        }
        
        private void Start()
        {
            StartCoroutine(ActionLoop());
        }

        private IEnumerator ActionLoop()
        {
            foreach (var actionEvent in actionEvents)
            {
                if (!actionEvent.on) continue;

                Debug.Log("[Debug] <color=#00ff48>Event </color> <color=#ffea00>" + actionEvent.name + "</color> is starting...");
                
                actionEvent.onStart?.Invoke();

                yield return new WaitForSeconds(actionEvent.duration);

                if (actionEvent.isConditionMet is not null)
                    yield return new WaitUntil(() => actionEvent.isConditionMet.Value);

                actionEvent.onFinished?.Invoke();
            }
            
            Debug.Log("[Debug] <color=#00ff48>All actions have been finished. Quay về sảnh chính...</color>");
            yield return new WaitForSeconds(3f);
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameMenu");
        }
    }

}

