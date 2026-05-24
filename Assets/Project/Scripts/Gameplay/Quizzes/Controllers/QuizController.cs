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
            Cloud.RTDB.RemoteCommandListener.OnTriggerHint += HandleRemoteTriggerHint;
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest += HandleRemoteSkipQuest;
        }

        private void OnDisable()
        {
            Cloud.RTDB.RemoteCommandListener.OnTriggerHint -= HandleRemoteTriggerHint;
            Cloud.RTDB.RemoteCommandListener.OnSkipQuest -= HandleRemoteSkipQuest;
        }

        private void HandleRemoteTriggerHint()
        {
            // Chỉ phát gợi ý (phát lại câu hỏi/tiếng kêu) khi câu hỏi đang active và chưa trả lời xong
            if (_currentQuestion != null && !_isQuestionAnswered)
            {
                Debug.Log("[QuizController] Nhận lệnh OnTriggerHint -> Phát lại câu hỏi âm thanh");
                // Chỉ stop coroutine âm thanh, không dùng StopAllCoroutines() để tránh cắt ngang intro
                if (_questionSoundCoroutine != null)
                {
                    StopCoroutine(_questionSoundCoroutine);
                    _questionSoundCoroutine = null;
                }
                _questionSoundCoroutine = StartCoroutine(HandleQuestionSounds());
            }
        }

        private void HandleRemoteSkipQuest()
        {
            // Bỏ qua câu hỏi hiện tại: Tự động submit câu trả lời đúng để cho trẻ đi tiếp
            if (_currentQuestion != null && !_isQuestionAnswered)
            {
                Debug.Log("[QuizController] Nhận lệnh OnSkipQuest -> Bỏ qua câu hỏi hiện tại");
                SubmitAnswer(_currentQuestion.correctAnswer);
            }
        }

        private void Start()
        {
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
            _questionSoundCoroutine = null;
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
            
            StartCoroutine(HandleQuestionSounds());
        }

        private void EndQuiz()
        {
            Debug.Log("[QuizController] Quiz ended.");
            DestroyCurrentObject();
            soundManager.StopLoopingSound();
            TimeManager.Instance.SaveLessonTimeData("success", quiz_score.Value);
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
            SceneManager.LoadScene("GameMenu");
        }

        // ─── Event handlers (subscribed from Awake) ────────────────────────────

        private void SubmitAnswer(int answerIndex)
        {
            if (_isQuestionAnswered) return;
            _isQuestionAnswered = true;

            bool isCorrect = (answerIndex == _currentQuestion.correctAnswer);
            if (isCorrect) quiz_score.Value++;

            TimeManager.Instance.LogQuestComplete(
                _currentQuestionIndex,
                _currentQuestion.question,
                isCorrect ? "success" : "failed"
            );
            _currentQuestionIndex++;

            uiController.HandleSubmittedAnswer(
                answerIndex,
                _currentQuestion.correctAnswer,
                isCorrect,
                quiz_score.Value
            );

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