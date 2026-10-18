using VRAutism.Core;
using VRAutism.Core.Models;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace VRAutism.Gameplay.Quizzes{
    /// <summary>
    /// Orchestrates quiz lesson flow.
    /// Subscribes to UIController events — has zero direct references to UI elements.
    /// </summary>
    public class QuizController : MonoBehaviour
    {
        // Báo hiệu chuyển cảnh (gửi thẳng string Activity Name)
        public static event Action<string> OnQuizActivityChanged;

        [Header("Dependencies")]
        [SerializeField] private QuestionCollection questionCollection;
        [SerializeField] private QuizUIController uiController;
        [SerializeField] private SoundManager soundManager;

        [Header("Quiz Settings")]
        [SerializeField] private QuizConfig quizConfig;    // direct asset ref — no magic string
        [SerializeField] private AudioClip introAudioClip;
        [SerializeField] private IntVariable quiz_score;

        private QuizQuestionData _currentQuestion;
        private GameObject _currentAssociatedObject;
        private int _currentQuestionIndex;
        private bool _isQuestionAnswered;
        private Coroutine _questionSoundCoroutine;
        private int _currentQuestHintsVerbal;
        private int _currentQuestHintsVisual;
        private string _quizSessionId = string.Empty;
        private bool _isEnding;
        private bool _completionSaved;

        /// <summary>
        /// Tham số bài Quiz động: fallback về giá trị legacy nếu SessionContext chưa khởi tạo hoặc sentinel.
        /// </summary>
        private LessonParameters QuizParams =>
            SessionContext.Instance != null
                ? SessionContext.Instance.CurrentParams
                : LessonParameters.Default;  // Dùng singleton để tránh GC allocation

        // Sentinel -1f = không ghi đè; fallback về giá trị legacy hardcode
        // Patch 6: Clamp Mathf.Max(0f, ...) để bảo vệ WaitForSeconds khỏi nhận giá trị âm.
        private float SafeQuizIntroDelay => Mathf.Max(0f,
            QuizParams.Quiz.QuizIntroDelay >= 0f ? QuizParams.Quiz.QuizIntroDelay : 2f);

        private float SafeQuizSoundGap => Mathf.Max(0f,
            QuizParams.Quiz.QuizSoundGap >= 0f ? QuizParams.Quiz.QuizSoundGap : 0.5f);

        private float SafeQuizEndDelay => Mathf.Max(0f,
            QuizParams.Quiz.QuizEndDelay >= 0f ? QuizParams.Quiz.QuizEndDelay : 3f);

        private void Awake()
        {
            // Subscribe to UI events — controller reacts, UI just raises signals
            uiController.OnAnswerSelected += SubmitAnswer;
            uiController.OnNextClicked    += OnNextQuestionClicked;
        }

        private void OnDestroy()
        {
            uiController.OnAnswerSelected -= SubmitAnswer;
            uiController.OnNextClicked    -= OnNextQuestionClicked;
        }

        private void OnEnable()
        {
            Cloud.RTDB.RemoteCommandListener.OnTriggerVerbalHint += HandleRemoteTriggerVerbalHint;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVisualHint += HandleRemoteTriggerVisualHint;
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest += HandleRemoteSkipQuest;
        }
 
        private void OnDisable()
        {
            Cloud.RTDB.RemoteCommandListener.OnTriggerVerbalHint -= HandleRemoteTriggerVerbalHint;
            Cloud.RTDB.RemoteCommandListener.OnTriggerVisualHint -= HandleRemoteTriggerVisualHint;
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest -= HandleRemoteSkipQuest;
        }
 
        private void HandleRemoteTriggerVerbalHint()
        {
            // Chỉ phát gợi ý (phát lại câu hỏi/tiếng kêu) khi câu hỏi đang active và chưa trả lời xong
            if (_currentQuestion != null && !_isQuestionAnswered)
            {
                Debug.Log("[QuizController] Nhận lệnh OnTriggerVerbalHint -> Phát lại câu hỏi âm thanh");
                
                _currentQuestHintsVerbal++;
 
                // Chỉ stop coroutine âm thanh, không dùng StopAllCoroutines() để tránh cắt ngang intro
                if (_questionSoundCoroutine != null)
                {
                    StopCoroutine(_questionSoundCoroutine);
                    _questionSoundCoroutine = null;
                }
                _questionSoundCoroutine = StartCoroutine(HandleQuestionSounds());
            }
        }

        private void HandleRemoteTriggerVisualHint()
        {
            if (_currentQuestion != null && !_isQuestionAnswered)
            {
                Debug.Log("[QuizController] Nhận lệnh OnTriggerVisualHint -> Tăng bộ đếm gợi ý thị giác");
                _currentQuestHintsVisual++;
            }
        }

        private void HandleRemoteSkipQuest()
        {
            if (_currentQuestion != null)
            {
                if (!_isQuestionAnswered)
                {
                    Debug.Log("[QuizController] Nhận lệnh OnSkipQuest -> Bỏ qua câu hiện tại, tự động submit và chuyển câu");
                    SubmitSkippedAnswer();
                    StartCoroutine(AutoNextQuestionRoutine());
                }
                else
                {
                    // Nếu đã trả lời rồi nhưng bị kẹt (ví dụ quên bấm Next), Skip lần 2 sẽ ép qua câu luôn
                    Debug.Log("[QuizController] Nhận lệnh OnSkipQuest -> Ép chuyển sang câu tiếp theo");
                    OnNextQuestionClicked();
                }
            }
        }

        private IEnumerator AutoNextQuestionRoutine()
        {
            // Chờ một chút để trẻ nghe tiếng đúng (Win) và thấy UI đáp án sáng lên
            yield return new WaitForSeconds(1.5f);
            
            // Kiểm tra xem chưa bị ai (hoặc lệnh Skip thứ 2) bấm Next
            if (_isQuestionAnswered)
            {
                OnNextQuestionClicked();
            }
        }

        private void Start()
        {
            _quizSessionId = SessionContext.Instance != null ? SessionContext.Instance.SessionId : string.Empty;
            quiz_score.Value    = 0;
            _currentQuestionIndex = 0;

            uiController.Initialize();
            uiController.UpdateScoreText(quiz_score.Value);

            questionCollection.LoadFromConfig(quizConfig);
            TimeManager.Instance.StartLessonTime();

            StartCoroutine(PlayIntroAndStartQuiz());
        }

        // ─── Lesson flow ───────────────────────────────────────────────────────

        private IEnumerator PlayIntroAndStartQuiz()
        {
            yield return new WaitForSeconds(SafeQuizIntroDelay);

            soundManager.PlayAudioClip(introAudioClip);
            yield return new WaitForSeconds(introAudioClip.length);

            PresentQuestion();
        }

        private void PresentQuestion()
        {
            _isQuestionAnswered = false;
            _currentQuestHintsVerbal = 0;
            _currentQuestHintsVisual = 0;
            
            if (_questionSoundCoroutine != null)
            {
                StopCoroutine(_questionSoundCoroutine);
                _questionSoundCoroutine = null;
            }
            
            uiController.StopAllEffects();
            uiController.HideNextButton();

            DestroyCurrentObject();

            _currentQuestion = questionCollection.GetNextQuestion();

            if (_currentQuestion == null)
            {
                Debug.Log("[QuizController] All questions answered — ending quiz.");
                EndQuiz();
                return;
            }

            // associatedObjectKey must match the prefab's name in the scene
            if (!string.IsNullOrEmpty(_currentQuestion.associatedObjectKey))
            {
                var prefab = questionCollection.GetPrefab(_currentQuestion.associatedObjectKey);
                if (prefab != null)
                    _currentAssociatedObject = Instantiate(prefab);
                else
                    Debug.LogWarning($"[QuizController] No prefab found for key: '{_currentQuestion.associatedObjectKey}'");
            }

            uiController.SetupUIForQuestion(_currentQuestion);
            TimeManager.Instance.MarkQuestStart();
            
            // Báo cáo Cloud: Đang ở câu hỏi số mấy để Web vẽ nút
            OnQuizActivityChanged?.Invoke("Quiz_Q" + (_currentQuestionIndex + 1));
            _questionSoundCoroutine = StartCoroutine(HandleQuestionSounds());
        }

        private void EndQuiz()
        {
            if (_isEnding) return;
            _isEnding = true;

            Debug.Log("[QuizController] Quiz ended.");
            DestroyCurrentObject();
            soundManager.StopLoopingSound();
            uiController.ShowGameOver(TimeManager.Instance.GetTotalElapsedSeconds());
            StartCoroutine(ReturnToMenuAfterDelay(SafeQuizEndDelay));
        }

        /// <summary>
        /// Chờ vài giây để người dùng xem bảng kết quả, sau đó quay về GameMenu.
        /// Tương tự ActionManager — tất cả loại bài sau khi xong đều phải về Lobby.
        /// </summary>
        private IEnumerator ReturnToMenuAfterDelay(float delaySeconds)
        {
            Debug.Log($"[QuizController] Chuyển về GameMenu sau {delaySeconds}s...");
            yield return new WaitForSeconds(delaySeconds);

            if (!IsCurrentQuizSession()) yield break;
            if (!_completionSaved)
            {
                _completionSaved = true;
                if (TimeManager.Instance != null)
                    TimeManager.Instance.SaveLessonTimeData("success", quiz_score.Value);
            }
            SceneManager.LoadScene("GameMenu");
        }

        private bool IsCurrentQuizSession()
        {
            if (this == null) return false;
            string activeSessionId = SessionContext.Instance != null ? SessionContext.Instance.SessionId : string.Empty;
            return string.Equals(_quizSessionId, activeSessionId, StringComparison.Ordinal);
        }

        // ─── Event handlers (subscribed from Awake) ────────────────────────────

        private void SubmitAnswer(int answerIndex)
        {
            SubmitAnswer(answerIndex, false);
        }

        private void SubmitSkippedAnswer()
        {
            if (_currentQuestion == null || _isQuestionAnswered) return;
            SubmitAnswer(_currentQuestion.correctAnswer, true);
        }

        private void SubmitAnswer(int answerIndex, bool skipped)
        {
            if (_currentQuestion == null) return;
            if (_isQuestionAnswered) return;
            _isQuestionAnswered = true;

            bool isCorrect = !skipped && answerIndex == _currentQuestion.correctAnswer;
            if (isCorrect) quiz_score.Value++;

            TimeManager.Instance.LogQuestComplete(
                _currentQuestionIndex,
                _currentQuestion.question,
                skipped ? "skipped" : isCorrect ? "success" : "failed",
                hintsVerbal:      _currentQuestHintsVerbal,
                hintsVisual:      _currentQuestHintsVisual
            );
            _currentQuestionIndex++;

            uiController.HandleSubmittedAnswer(
                answerIndex,
                _currentQuestion.correctAnswer,
                isCorrect || skipped,
                quiz_score.Value
            );

            if (!skipped)
                soundManager.PlaySound(isCorrect ? TypeSound.Win : TypeSound.Lose);
            uiController.ShowNextButton();
        }

        private void OnNextQuestionClicked()
        {
            soundManager.StopLoopingSound();
            PresentQuestion();
        }

        // ─── Sound sequence ────────────────────────────────────────────────────

        private IEnumerator HandleQuestionSounds()
        {
            if (_currentQuestion.questionSound != TypeSound.None)
            {
                Debug.Log("[QuizController] Playing question sound: " + _currentQuestion.questionSound);
                soundManager.PlaySound(_currentQuestion.questionSound);
                yield return new WaitUntil(() => !soundManager.IsPlaying());
            }

            yield return new WaitForSeconds(SafeQuizSoundGap);

            if (_currentQuestion.animalSound != TypeSound.None)
            {
                Debug.Log("[QuizController] Playing animal sound: " + _currentQuestion.animalSound);
                soundManager.PlaySound(_currentQuestion.animalSound);
            }
            _questionSoundCoroutine = null;
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private void DestroyCurrentObject()
        {
            if (_currentAssociatedObject != null)
            {
                Destroy(_currentAssociatedObject);
                _currentAssociatedObject = null;
            }
        }
    }

}
