using System;
using System.Linq;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;

namespace VRAutism.Gameplay.Actions
{
    public class QuestController : MonoBehaviour
    {
        // Báo hiệu mỗi khi chuyển sang quest mới
        public static event Action<string> OnQuestActivityChanged;

        // Báo hiệu Transform của vật thể mục tiêu mới cho Telemetry bắt đầu tracking
        public static event Action<Transform> OnTargetTransformChanged;

        [SerializeField] private Quest[] quests;
        [SerializeField] private QuestProgressUI questProgressUI;
        [SerializeField] private GameObject bubbleQuestion;
        [SerializeField] private GameObject congratulationUI;
        [SerializeField] private BooleanVariable isConditionMet;

        private int curQuestId;
        private string[] questNames;

        private float curQuestProgress;
        private float curReminderTimer;
        private float curEffectiveCycle;
        private bool isCharacterInsideTrigger;
        private int characterColliderCount;
        private LessonParameters activeParams;
        private int _currentQuestHintsVerbal;
        private int _currentQuestHintsVisual;

        private void Awake()
        {
            foreach (var quest in quests)
            {
                quest.Init();
                quest.OnCharacterEnter += HandleQuestCharacterEnter;
                quest.OnCharacterExit += HandleQuestCharacterExit;
            }

            questNames = quests.Where(q => q.IsSendData).Select(q => q.Name).ToArray();

            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
            if (congratulationUI != null) congratulationUI.SetActive(false);

            curQuestId = 0;
            activeParams = LessonParameters.Default;
        }

        private void Start()
        {
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest += HandleSkipCurrentQuest;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVerbalHint += HandleTriggerVerbalHintCurrentQuest;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVisualHint += HandleTriggerVisualHintCurrentQuest;

            activeParams = SessionContext.Instance != null 
                ? SessionContext.Instance.CurrentParams 
                : LessonParameters.Default;
        }

        private void Update()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest == null || (congratulationUI != null && congratulationUI.activeSelf)) return;

            if (!isCharacterInsideTrigger && curEffectiveCycle > 0f)
            {
                curReminderTimer -= Time.deltaTime;
                if (curReminderTimer < 0)
                {
                    curReminderTimer = curEffectiveCycle;
                    // Chu kỳ nhắc nhở tự động: sử dụng visual hint nhấp nháy viền (nếu được phép)
                    if (activeParams.Actions.EnableVisualGuidance)
                    {
                        activeQuest.BlinkHintOutline(true);
                        _currentQuestHintsVisual++;
                    }
                }
            }

            // 2. Logic tính toán Thanh tiến trình (HoldTouch Progress) tập trung
            if (activeQuest.Type == QuestType.HoldTouch && isCharacterInsideTrigger)
            {
                curQuestProgress += Time.deltaTime / activeQuest.Duration;
                if (questProgressUI != null) questProgressUI.SetProgress(curQuestProgress);

                if (curQuestProgress >= 1f)
                {
                    curQuestProgress = 1f;
                    CompleteActiveQuest();
                }
            }
        }

        // ── XỬ LÝ VẬT LÝ TỪ QUEST ──────────────────────────────────────────
        private void HandleQuestCharacterEnter(Quest quest)
        {
            if (quest != GetCurQuest()) return; // Chỉ xử lý nếu chạm đúng Quest hiện tại

            characterColliderCount++;
            if (characterColliderCount > 1) return; // Đã ở trong trigger từ trước

            isCharacterInsideTrigger = true;
            quest.TriggerStartedEvent();

            if (quest.Type == QuestType.Touch)
            {
                CompleteActiveQuest();
            }
            else if (quest.Type == QuestType.HoldTouch)
            {
                // Bật UI Progress Bar tại vị trí Quest
                if (questProgressUI != null)
                {
                    questProgressUI.gameObject.SetActive(true);
                    questProgressUI.transform.position = quest.ProgressBarPosition;
                    questProgressUI.SetProgress(0);
                }
                curQuestProgress = 0;
            }
        }

        private void HandleQuestCharacterExit(Quest quest)
        {
            if (quest != GetCurQuest()) return;

            characterColliderCount--;
            if (characterColliderCount > 0) return; // Vẫn còn collider bên trong
            if (characterColliderCount < 0) characterColliderCount = 0;

            isCharacterInsideTrigger = false;
            
            if (quest.Type == QuestType.HoldTouch)
            {
                if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);
                curQuestProgress = 0;
            }
        }

        // ── ĐIỀU PHỐI TRẠNG THÁI QUEST ──────────────────────────────────────
        public void StartRunningQuest()
        {
            isConditionMet.Value = false;
            TimeManager.Instance?.StartLessonTime(); // Bấm giờ từ lúc trẻ bắt đầu làm bài
            StartNewQuest();
        }

        private void StartNewQuest()
        {
            TimeManager.Instance?.StartQuestTime();   // stamp _questStartSecond before quest begins

            Quest activeQuest = GetCurQuest();
            if (activeQuest == null)
            {
                Debug.LogError($"Quest {curQuestId} not found in total {quests.Length} quests");
                return;
            }

            isCharacterInsideTrigger = false;
            characterColliderCount = 0;
            curQuestProgress = 0;
            _currentQuestHintsVerbal = 0;
            _currentQuestHintsVisual = 0;

            // Setup hiển thị Outline và Bubble tập trung
            activeQuest.SetOutline(activeParams.Actions.EnableVisualGuidance);
            if (bubbleQuestion != null)
            {
                bubbleQuestion.SetActive(activeParams.Actions.EnableBubbleHints);
                bubbleQuestion.transform.position = activeQuest.BubblePosition;
            }

            // Reset bộ đếm nhắc nhở
            float overrideCycle = activeParams.Actions.ActionReminderCycle;
            curEffectiveCycle = overrideCycle >= 0f ? overrideCycle : activeQuest.ReminderCycle;
            curReminderTimer = curEffectiveCycle;

            OnQuestActivityChanged?.Invoke("Action_" + activeQuest.Name);

            // Báo cho SensorHarvester biết mục tiêu mới để bắt đầu tracking Gaze & Proximity
            OnTargetTransformChanged?.Invoke(activeQuest.transform);
        }

        private void CompleteActiveQuest()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest == null) return;

            // Tắt hiển thị visuals của Quest vừa xong
            activeQuest.SetOutline(false);
            if (bubbleQuestion != null) bubbleQuestion.SetActive(false);
            if (questProgressUI != null) questProgressUI.gameObject.SetActive(false);

            activeQuest.TriggerFinishedEvent();

            // Lưu kết quả
            if (TimeManager.Instance)
            {
                TimeManager.Instance.LogQuestComplete(
                    questIndex:       curQuestId,
                    questName:        activeQuest.Name,
                    completionStatus: "success",
                    hintsVerbal:      _currentQuestHintsVerbal,
                    hintsVisual:      _currentQuestHintsVisual
                );
            }

            if (curQuestId >= quests.Length - 1)
            {
                if (congratulationUI != null) congratulationUI.SetActive(true);
                // Quest cuối hoàn thành → ActionManager sẽ tự xử lý tiếp
                isConditionMet.Value = true;
                return;
            }

            curQuestId++;
            StartNewQuest();
        }

        // ── REMOTE COMMANDS ──────────────────────────────────────────────────
        private void HandleSkipCurrentQuest()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Nhận lệnh Skip từ xa -> Chuyển tiếp tới Quest hiện tại: {activeQuest.Name}");
                CompleteActiveQuest();
            }
        }

        private void HandleTriggerVerbalHintCurrentQuest()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Nhận lệnh Gợi ý Lời nói từ xa -> Kích hoạt nhắc nhở NPC.");
                activeQuest.TriggerReminderEvent();
                _currentQuestHintsVerbal++;
            }
        }

        private void HandleTriggerVisualHintCurrentQuest()
        {
            Quest activeQuest = GetCurQuest();
            if (activeQuest != null)
            {
                Debug.Log($"[QuestController] Nhận lệnh Gợi ý Thị giác từ xa -> Kích hoạt Blink nhấp nháy viền.");
                activeQuest.BlinkHintOutline(true);
                _currentQuestHintsVisual++;
            }
        }

        private Quest GetCurQuest()
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
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest -= HandleSkipCurrentQuest;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVerbalHint -= HandleTriggerVerbalHintCurrentQuest;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVisualHint -= HandleTriggerVisualHintCurrentQuest;

            foreach (var quest in quests)
            {
                if (quest == null) continue;
                quest.OnCharacterEnter -= HandleQuestCharacterEnter;
                quest.OnCharacterExit -= HandleQuestCharacterExit;
            }
        }
    }
}
