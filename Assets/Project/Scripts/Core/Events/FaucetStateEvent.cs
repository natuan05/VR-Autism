using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Core
{
    public class FaucetStateEvent : MonoBehaviour
    {
        public void Enable()
        {
            this.SendEvent(EventID.ToggleFaucet, true);
        }

        public void Disable()
        {
            this.SendEvent(EventID.ToggleFaucet, false);

        }
    }
}

