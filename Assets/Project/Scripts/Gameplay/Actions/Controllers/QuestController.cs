using System;
using System.Linq;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Pure Sequencer — chỉ lo điều phối thứ tự Quest và phát telemetry events.
    /// Không biết VisualQuest, TouchQuest, hay bất kỳ loại Quest cụ thể nào.
    /// </summary>
    public class QuestController : MonoBehaviour, IQuestFlowController
    {
        public static QuestController Instance { get; private set; }

        // ── Events ─────────────────────────────────────────────────────────
        /// <summary>Báo hiệu mỗi khi chuyển sang quest mới.</summary>
        public static event Action<string> OnQuestActivityChanged;

        /// <summary>Báo hiệu khi một quest hoàn thành (kèm dữ liệu telemetry).</summary>
        public static event EventHandler<ActiveQuestFinishedEventArgs> OnActiveQuestCompleted;

        /// <summary>Báo hiệu Transform mục tiêu mới cho SensorHarvester tracking.</summary>
        public static event Action<Transform> OnTargetTransformChanged;

        /// <summary>Báo hiệu khi tất cả quest đã hoàn thành.</summary>
        public event Action OnAllQuestsCompleted;

        // ── Serialized Fields ──────────────────────────────────────────────
        [SerializeField] private Quest[] quests;
        [SerializeField] private BooleanVariable isConditionMet;
        [SerializeField] private IntVariable verbalHintCount;

        // ── Public Accessors ───────────────────────────────────────────────
        public Quest[] Quests => quests;

        // ── Private State ──────────────────────────────────────────────────
        private int curQuestId;
        private string[] questNames;
        private LessonParameters activeParams;
        private float curReminderTimer;
        private float curEffectiveCycle;
        private int _currentQuestHintsVisual;
        private float _lastVisualHintTime;
        private float _questStartTime;

        // ── Setup ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            foreach (var quest in quests)
                quest?.Init();

            questNames = quests.Where(q => q != null).Select(q => q.Name).ToArray();
            curQuestId = 0;
            activeParams = LessonParameters.Default;
        }

        private void Start()
        {
            activeParams = SessionContext.Instance != null
                ? SessionContext.Instance.CurrentParams
                : LessonParameters.Default;

            enabled = false; // Tắt Update loop ban đầu
        }

        // ── Main Loop ──────────────────────────────────────────────────────
        private void Update()
        {
            Quest quest = GetCurQuest();
            if (quest == null) return;

            // Auto-hint timer — hỏi Quest xem có nên hint không
            if (activeParams.Actions.EnableAutoHint && quest.ShouldAutoHint && curEffectiveCycle > 0f)
            {
                curReminderTimer -= Time.deltaTime;
                if (curReminderTimer < 0)
                {
                    curReminderTimer = curEffectiveCycle;
                    TriggerVisualHint();
                }
            }

            // Delegate frame update cho Quest tự xử lý
            quest.Tick();
        }

        // ── Quest Flow ─────────────────────────────────────────────────────
        public void StartRunningQuest()
        {
            enabled = true;
            isConditionMet.Value = false;
            TimeManager.Instance?.StartLessonTime();
            ActivateQuest();
        }

        private void ActivateQuest()
        {
            TimeManager.Instance?.StartQuestTime();
            _questStartTime = TimeManager.Instance != null
                ? (float)TimeManager.Instance.GetTotalElapsedSeconds()
                : 0f;

            Quest quest = GetCurQuest();
            if (quest == null)
            {
                Debug.LogError($"Quest {curQuestId} not found in total {quests.Length} quests");
                return;
            }

            // Reset hint counters
            if (verbalHintCount != null) verbalHintCount.Value = 0;
            _currentQuestHintsVisual = 0;
            _lastVisualHintTime = -1f;

            // Reset reminder timer
            float overrideCycle = activeParams.Actions.ActionReminderCycle;
            curEffectiveCycle = overrideCycle >= 0f ? overrideCycle : quest.ReminderCycle;
            curReminderTimer = curEffectiveCycle;

            // Quest setup — outline, trigger, voice prompt...
            quest.Begin(this);

            // Telemetry events
            OnQuestActivityChanged?.Invoke("Action_" + quest.Name);
            OnTargetTransformChanged?.Invoke(quest.transform);
        }

        public void CompleteActiveQuest(string status = "success")
        {
            Quest quest = GetCurQuest();
            if (quest == null) return;

            // Quest tự lo cleanup
            quest.End();

            // Telemetry
            int hintsVerbal = verbalHintCount != null ? verbalHintCount.Value : 0;
            double responseTimeFromHint = -1.0;
            if (_lastVisualHintTime >= 0f)
            {
                double currentElapsed = TimeManager.Instance != null
                    ? TimeManager.Instance.GetTotalElapsedSeconds()
                    : 0.0;
                responseTimeFromHint = currentElapsed - _lastVisualHintTime;
            }
            OnActiveQuestCompleted?.Invoke(this, new ActiveQuestFinishedEventArgs(
                curQuestId, quest.Name, status,
                hintsVerbal, _currentQuestHintsVisual, 0,
                responseTimeFromHint));

            if (verbalHintCount != null) verbalHintCount.Value = 0;

            // Advance hoặc kết thúc
            if (curQuestId >= quests.Length - 1)
            {
                isConditionMet.Value = true;
                enabled = false;
                OnAllQuestsCompleted?.Invoke();
                return;
            }

            curQuestId++;
            ActivateQuest();
        }

        // ── Remote Commands ────────────────────────────────────────────────
        public void TriggerSkip()
        {
            Quest quest = GetCurQuest();
            if (quest != null)
            {
                Debug.Log($"[QuestController] Skip -> Quest: {quest.Name}");
                CompleteActiveQuest("skipped");
            }
        }

        public void TriggerVerbalHint()
        {
            Quest quest = GetCurQuest();
            if (quest != null)
            {
                Debug.Log($"[QuestController] Gợi ý Lời nói -> Quest: {quest.Name}");
                quest.OnVerbalHint();
            }
        }

        public void TriggerVisualHint()
        {
            Quest quest = GetCurQuest();
            if (quest == null) return;

            Debug.Log($"[QuestController] Gợi ý Thị giác -> Quest: {quest.Name}");
            quest.OnVisualHint(activeParams.Actions.EnableVisualGuidance);
            _currentQuestHintsVisual++;
            _lastVisualHintTime = TimeManager.Instance != null
                ? (float)TimeManager.Instance.GetTotalElapsedSeconds()
                : 0f;
        }

        // ── Getters ────────────────────────────────────────────────────────
        public Quest GetCurQuest()
        {
            if (curQuestId >= 0 && curQuestId < quests.Length)
                return quests[curQuestId];
            return null;
        }

        public string[] GetAllQuestNames() => questNames;

        public float GetLastVisualHintOrQuestStartTime()
            => _lastVisualHintTime >= 0f ? _lastVisualHintTime : _questStartTime;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
