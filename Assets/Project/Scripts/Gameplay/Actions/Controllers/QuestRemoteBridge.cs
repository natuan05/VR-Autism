using UnityEngine;
using VRAutism.Cloud.RTDB;

namespace VRAutism.Gameplay.Actions
{
    public class QuestRemoteBridge : MonoBehaviour
    {
        [SerializeField] private QuestController questController;

        private void Awake()
        {
            if (questController == null)
                questController = GetComponent<QuestController>();
        }

        private void Start()
        {
            RemoteCommandListener.OnSkipQuest          += HandleSkip;
            RemoteCommandListener.OnTriggerVerbalHint  += HandleVerbalHint;
            RemoteCommandListener.OnTriggerVisualHint  += HandleVisualHint;
        }

        private void OnDestroy()
        {
            RemoteCommandListener.OnSkipQuest          -= HandleSkip;
            RemoteCommandListener.OnTriggerVerbalHint  -= HandleVerbalHint;
            RemoteCommandListener.OnTriggerVisualHint  -= HandleVisualHint;
        }

        private void HandleSkip()        => questController.TriggerSkip();
        private void HandleVerbalHint()  => questController.TriggerVerbalHint();
        private void HandleVisualHint()  => questController.TriggerVisualHint();
    }
}
