using System;
using System.Linq;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class QuestController : MonoBehaviour, IQuestFlowController
    {
        public static QuestController Instance { get; private set; }

        // Báo hiệu mỗi khi chuyển sang quest mới
        public static event Action<string> OnQuestActivityChanged;

        // Báo hiệu Transform của vật thể mục tiêu mới cho Telemetry bắt đầu tracking
        public static event Action<Transform> OnTargetTransformChanged;

        public static event Action<int, string, string, int, int, int, double> ActiveQuestFinished;

        // Sự kiện kết thúc toàn bộ bài học
        public event Action OnAllQuestsCompleted;

        [SerializeField] private Quest[] quests;
        [SerializeField] private BooleanVariable isConditionMet;

        private int curQuestId;
        private string[] questNames;

        private float curReminderTimer;
        private float curEffectiveCycle;
        private bool isCharacterInsideTrigger;
        private int characterColliderCount;
        private LessonParameters activeParams;
        private int _currentQuestHintsVisual;
        [SerializeField] private IntVariable verbalHintCount;
        private float _lastVisualHintTime;
        private float _questStartTime;

        // Getter công khai để lấy danh sách Quest cho QuestUIController đăng ký sự kiện
        public Quest[] Quests => quests;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            foreach (var quest in quests)
            {
                if (quest == null) continue;
                quest.Init();
                quest.CharacterCanEnter += HandleQuestCharacterEnter;
                quest.CharacterExit += HandleQuestCharacterExit;
            }

            questNames = quests.Where(q => q != null && q.IsSendData).Select(q => q.Name).ToArray();

            curQuestId = 0;
            activeParams = LessonParameters.Default;
        }

        private void Start()
        {
            activeParams = SessionContext.Instance != null 
                ? SessionContext.Instance.CurrentParams 
                : LessonParameters.Default;

            enabled = false; // Tắt Update loop ban đầu để tối ưu hóa hiệu năng
        }

        private void Update()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest == null) return;

            if (activeParams.Actions.EnableAutoHint && !isCharacterInsideTrigger && curEffectiveCycle > 0f)
            {
                curReminderTimer -= Time.deltaTime;
                if (curReminderTimer < 0)
                {
                    curReminderTimer = curEffectiveCycle;
                    TriggerVisualHint();
                }
            }

            // Chỉ tính toán tiến độ khi nhân vật đang ở trong vùng tương tác
            if (isCharacterInsideTrigger)
            {
                activeQuest.OnUpdateInteraction(this);
            }
        }

        // ── XỬ LÝ VẬT LÝ TỪ QUEST ──────────────────────────────────────────
        private void HandleQuestCharacterEnter(Quest quest)
        {
            if (quest != GetCurQuest()) return; // Chỉ xử lý nếu chạm đúng Quest hiện tại

            characterColliderCount++;
            if (characterColliderCount > 1) return; // Đã ở trong trigger từ trước

            isCharacterInsideTrigger = true;
            quest.QuestIsActivated();

            // Ủy quyền xử lý cho Quest con
            quest.OnStartInteraction(this);
        }

        private void HandleQuestCharacterExit(Quest quest)
        {
            if (quest != GetCurQuest()) return;

            characterColliderCount--;
            if (characterColliderCount > 0) return; // Vẫn còn collider bên trong
            if (characterColliderCount < 0) characterColliderCount = 0;

            isCharacterInsideTrigger = false;

            // Ủy quyền xử lý cho Quest con
            quest.OnCancelInteraction(this);
        }

        // ── ĐIỀU PHỐI TRẠNG THÁI QUEST ──────────────────────────────────────
        public void StartRunningQuest()
        {
            enabled = true; // Kích hoạt lại Update loop khi bài học bắt đầu
            isConditionMet.Value = false;
            TimeManager.Instance?.StartLessonTime(); // Bấm giờ từ lúc trẻ bắt đầu làm bài
            StartNewQuest();
        }

        private void StartNewQuest()
        {
            TimeManager.Instance?.StartQuestTime();
            
            _questStartTime = TimeManager.Instance != null ? (float)TimeManager.Instance.GetTotalElapsedSeconds() : 0f;

            Quest activeQuest = GetCurQuest();
            if (activeQuest == null)
            {
                Debug.LogError($"Quest {curQuestId} not found in total {quests.Length} quests");
                return;
            }

            isCharacterInsideTrigger = false;
            characterColliderCount = 0;
            if (verbalHintCount != null)
            {
                verbalHintCount.Value = 0;
            }
            _currentQuestHintsVisual = 0;
            _lastVisualHintTime = -1f;

            // Setup hiển thị Outline tập trung
            activeQuest.SetOutline(activeParams.Actions.EnableVisualGuidance);

            // Reset bộ đếm nhắc nhở
            float overrideCycle = activeParams.Actions.ActionReminderCycle;
            curEffectiveCycle = overrideCycle >= 0f ? overrideCycle : activeQuest.ReminderCycle;
            curReminderTimer = curEffectiveCycle;

            OnQuestActivityChanged?.Invoke("Action_" + activeQuest.Name);

            // Báo cho SensorHarvester biết mục tiêu mới để bắt đầu tracking Gaze & Proximity
            OnTargetTransformChanged?.Invoke(activeQuest.transform);

            // Báo cho các đối tượng quan tâm (ví dụ UI) là Quest đã được kích hoạt chạy
            activeQuest.OnQuestActive(this);
        }

        public void CompleteActiveQuest(string status = "success")
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest == null) return;

            // Tắt hiển thị viền của Quest vừa xong
            activeQuest.SetOutline(false);
            activeQuest.ActiveQuestFinished();
            
            int hintsVerbal = verbalHintCount != null ? verbalHintCount.Value : 0;
            double responseTimeFromHint = -1.0;
            if (_lastVisualHintTime >= 0f)
            {
                double currentElapsed = TimeManager.Instance != null ? TimeManager.Instance.GetTotalElapsedSeconds() : 0.0;
                responseTimeFromHint = currentElapsed - _lastVisualHintTime;
            }
            ActiveQuestFinished?.Invoke(curQuestId, activeQuest.Name, status, hintsVerbal, _currentQuestHintsVisual, 0, responseTimeFromHint);

            if (verbalHintCount != null)
            {
                verbalHintCount.Value = 0;
            }

            if (curQuestId >= quests.Length - 1)
            {
                isConditionMet.Value = true;
                enabled = false; // Tắt Update loop khi tất cả các Quest đã hoàn thành
                
                // Kích hoạt sự kiện kết thúc toàn bộ nhiệm vụ
                OnAllQuestsCompleted?.Invoke();
                return;
            }

            curQuestId++;
            StartNewQuest();
        }

        // ── REMOTE COMMANDS (public — được gọi từ QuestRemoteBridge) ──────────
        public void TriggerSkip()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Skip -\u003e Quest: {activeQuest.Name}");
                CompleteActiveQuest("skipped");
            }
        }

        public void TriggerVerbalHint()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Gợi ý Lời nói -\u003e Kích hoạt nhắc nhở NPC.");
                activeQuest.AllowReminderEvent();
            }
        }

        public void TriggerVisualHint()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Gợi ý Thị giác -\u003e Kích hoạt Blink nhấp nháy viền.");
                activeQuest.BlinkHintOutline(activeParams.Actions.EnableVisualGuidance);
                _currentQuestHintsVisual++;
                _lastVisualHintTime = TimeManager.Instance != null ? (float)TimeManager.Instance.GetTotalElapsedSeconds() : 0f;
            }
        }

        public float GetLastVisualHintOrQuestStartTime()
        {
            return _lastVisualHintTime >= 0f ? _lastVisualHintTime : _questStartTime;
        }


        public Quest GetCurQuest()
        {
            if (curQuestId >= 0 && curQuestId < quests.Length)
                return quests[curQuestId];
            return null;
        }

        public string[] GetAllQuestNames()
        {
            return questNames;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            foreach (var quest in quests)
            {
                if (quest == null) continue;
                quest.CharacterCanEnter -= HandleQuestCharacterEnter;
                quest.CharacterExit -= HandleQuestCharacterExit;
            }
        }
    }
}
