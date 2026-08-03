using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Abstract base class for all Quest types.
    /// Data + minimal lifecycle hooks — subclass tự quản lý vòng đời.
    /// </summary>
    public abstract class Quest : MonoBehaviour
    {
        [Header("Setup Quest Data")]
        [SerializeField] private int id;
        [SerializeField] private string questName;
        [SerializeField] private float duration;
        [SerializeField] private float reminderCycle;

        public int Id => id;
        public string Name => questName;
        public float Duration => duration;
        public float ReminderCycle => reminderCycle;

        /// <summary>Controller hiện tại đang điều phối Quest này. Null nếu chưa active.</summary>
        protected IQuestFlowController Controller { get; private set; }

        // ── Lifecycle ──────────────────────────────────────────────────────

        /// <summary>Khởi tạo ban đầu (gọi 1 lần trong Awake của QuestController).</summary>
        public virtual void Init() {}

        /// <summary>Quest được kích hoạt — lưu controller rồi gọi OnBegin cho subclass.</summary>
        public void Begin(IQuestFlowController controller)
        {
            Controller = controller;
            OnBegin();
        }

        /// <summary>Quest kết thúc — gọi OnEnd rồi xóa controller.</summary>
        public void End()
        {
            OnEnd();
            Controller = null;
        }

        /// <summary>Subclass override để xử lý khi quest bắt đầu.</summary>
        protected virtual void OnBegin() {}

        /// <summary>Subclass override để cleanup khi quest kết thúc.</summary>
        protected virtual void OnEnd() {}

        /// <summary>Gọi mỗi frame khi quest đang active.</summary>
        public virtual void Tick() {}

        // ── Hint Hooks ─────────────────────────────────────────────────────

        /// <summary>QuestController kiểm tra trước khi auto-hint. Mặc định: luôn cho phép.</summary>
        public virtual bool ShouldAutoHint => true;

        /// <summary>Xử lý gợi ý thị giác — mỗi loại Quest tự quyết cách hiển thị.</summary>
        public virtual void OnVisualHint(bool enableVisualGuidance) {}

        /// <summary>Xử lý gợi ý lời nói — mỗi loại Quest tự quyết cách kích hoạt.</summary>
        public virtual void OnVerbalHint() {}
    }
}
