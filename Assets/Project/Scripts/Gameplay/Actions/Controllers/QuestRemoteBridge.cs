using UnityEngine;
using VRAutism.Cloud.RTDB;

namespace VRAutism.Gameplay.Actions
{
    public class QuestRemoteBridge : MonoBehaviour //triển khai của Adapter Pattern, để tách biệt QuestController khỏi RemoteCommandListener
    {
        [SerializeField] private QuestController questController;

        private void Awake()
        {
            if (questController == null)
                questController = GetComponent<QuestController>() ?? QuestController.Instance ?? FindObjectOfType<QuestController>();
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

        private void HandleSkip()
        {
            if (questController == null)
                questController = QuestController.Instance ?? FindObjectOfType<QuestController>();

            if (questController != null)
                questController.TriggerSkip();
            else
                Debug.LogWarning("[QuestRemoteBridge] Không tìm thấy QuestController để Skip Quest.");
        }

        private void HandleVerbalHint()
        {
            if (questController == null)
                questController = QuestController.Instance ?? FindObjectOfType<QuestController>();

            if (questController != null)
                questController.TriggerVerbalHint();
        }

        private void HandleVisualHint()
        {
            if (questController == null)
                questController = QuestController.Instance ?? FindObjectOfType<QuestController>();

            if (questController != null)
                questController.TriggerVisualHint();
        }
    }
}
