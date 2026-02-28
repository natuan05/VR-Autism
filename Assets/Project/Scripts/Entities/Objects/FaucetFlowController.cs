using System;
using VRAutism.Core;
using UnityEngine;
using UnityEngine.UI;

namespace VRAutism.Entities
{
    public class FaucetFlowController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        private bool turnOn;

        private void Awake()
        {
            this.SubscribeListener(EventID.ToggleFaucet, param => Toggle((bool)param));
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (!turnOn)
                {
                    animator.Play("FaucetWaterStart");
                }
                else
                {
                    animator.Play("FaucetWaterEnd");
                }
            }
        }

        public void TurnOn()
        {
            if (!turnOn)
            {
                animator.Play("FaucetWaterStart");
            }
        }

        public void TurnOff()
        {
            animator.Play("FaucetWaterEnd");
        }

        private void Toggle(bool state)
        {
            turnOn = state;
        }
    }
}
