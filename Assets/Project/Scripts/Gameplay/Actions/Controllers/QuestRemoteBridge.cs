using UnityEngine;
using VRAutism.Cloud.RTDB;

namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Cầu nối một chiều giữa Firebase RTDB Remote Commands và QuestController.
    /// Tuân thủ SRP: QuestController không còn biết Firebase tồn tại.
    /// Tuân thủ DIP: QuestController phụ thuộc vào IQuestFlowController (abstraction),
    ///               không phụ thuộc vào RemoteCommandListener (concretion).
    ///
    /// Cách dùng trong Unity Inspector:
    ///   Kéo QuestRemoteBridge vào cùng GameObject với QuestController
    ///   và gán tham chiếu QuestController vào trường questController.
    /// </summary>
    [RequireComponent(typeof(QuestController))]
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
