using System;
using System.Collections.Generic;

namespace VRAutism.Core.Models
{
    /// <summary>
    /// Tham số cấu hình bài học, áp dụng cho một phiên trị liệu cụ thể.
    /// Được lưu trong SessionContext.CurrentParams và có thể ghi đè từ Firestore (Story 2.3).
    /// Sử dụng thiết kế Composition (lồng ghép các lớp con chuyên biệt) thay vì kế thừa
    /// để tối ưu hóa khả năng Serialise/Deserialise tự động từ JSON/Firestore trong Unity.
    ///
    /// SENTINEL VALUES: Các trường float dùng -1f làm "không ghi đè" (no-override).
    /// Khi giá trị là -1f, consumer sẽ fallback về giá trị Inspector của component đó.
    /// Chỉ khi giá trị >= 0f mới được coi là cấu hình động hợp lệ từ Firestore.
    /// </summary>
    [Serializable]
    public class LessonParameters
    {
        // --- Danh mục tham số của từng loại bài học ---
        public ActionParams Actions = new ActionParams();
        public QuizParams Quiz = new QuizParams();
        public ExplorationParams Exploration = new ExplorationParams();

        // ── Lớp tham số con chuyên biệt ──────────────────────────────────────────

        [Serializable]
        public class ActionParams
        {
            /// <summary>
            /// Bật/tắt hiệu ứng viền phát sáng (Outline) của vật thể mục tiêu
            /// khi Quest ở trạng thái Enable hoặc Start.
            /// </summary>
            public bool EnableVisualGuidance = true;

            /// <summary>
            /// Bật/tắt bong bóng câu hỏi (Bubble) nổi bên cạnh vật thể mục tiêu
            /// khi Quest ở trạng thái Enable.
            /// </summary>
            public bool EnableBubbleHints = true;

            /// <summary>
            /// Khoảng lặng (giây) trước khi SpeechResponser kích hoạt gợi ý giáo viên.
            /// Sentinel -1f = không ghi đè (dùng giá trị Inspector).
            /// Legacy hardcode: 5f.
            /// </summary>
            public float SpeechSilenceTimeout = -1f;

            /// <summary>
            /// Chu kỳ (giây) giữa các lần nhắc nhở tự động của Quest (reminderCycle).
            /// Sentinel -1f = không ghi đè (dùng giá trị Inspector per-Quest).
            /// Legacy: 0f = không bao giờ nhắc nhở (giữ nguyên hành vi Inspector).
            /// </summary>
            public float ActionReminderCycle = -1f;
        }

        [Serializable]
        public class QuizParams
        {
            /// <summary>
            /// Thời gian trễ (giây) trước khi phát âm thanh intro của bài Quiz.
            /// Sentinel -1f = không ghi đè.
            /// Legacy hardcode: 2f.
            /// </summary>
            public float QuizIntroDelay = -1f;

            /// <summary>
            /// Khoảng dừng (giây) giữa âm thanh câu hỏi và âm thanh con vật.
            /// Sentinel -1f = không ghi đè.
            /// Legacy hardcode: 0.5f.
            /// </summary>
            public float QuizSoundGap = -1f;

            /// <summary>
            /// Thời gian trễ (giây) sau khi Quiz kết thúc trước khi load lại GameMenu.
            /// Sentinel -1f = không ghi đè.
            /// Legacy hardcode: 3f.
            /// </summary>
            public float QuizEndDelay = -1f;
        }

        [Serializable]
        public class ExplorationParams
        {
            /// <summary>
            /// Tốc độ lerp di chuyển camera giữa các chuồng thú.
            /// Sentinel -1f = không ghi đè (dùng giá trị Inspector).
            /// Legacy hardcode: 2f.
            /// </summary>
            public float CameraMoveSpeed = -1f;

            /// <summary>
            /// Khoảng dừng (giây) giữa âm thanh tiếng kêu con vật và thông tin mô tả tiếp theo.
            /// Sentinel -1f = không ghi đè (dùng giá trị Inspector).
            /// Legacy hardcode: 4f.
            /// </summary>
            public float SoundToDescriptionGap = -1f;
        }

        // ── Factory ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Instance mặc định tái sử dụng (không tạo object mới mỗi lần gọi).
        /// Dùng làm fallback an toàn để tránh GC spike trong Update loops.
        /// Tất cả giá trị đều là sentinel -1f → mọi consumer sẽ dùng giá trị Inspector của riêng mình.
        /// </summary>
        public static readonly LessonParameters Default = new LessonParameters();

        /// <summary>
        /// Tương thích ngược với code cũ đang gọi GetDefault().
        /// Trả về singleton Default thay vị tạo object mới.
        /// </summary>
        public static LessonParameters GetDefault() => Default;

        /// <summary>
        /// Tạo LessonParameters từ Firestore Dictionary (default_lesson_params).
        /// Hỗ trợ 2 định dạng:
        ///   1. Nested map: dict["actions"]["enable_visual_guidance"]
        ///   2. Flat map:   dict["enable_visual_guidance"] (fallback)
        /// Sử dụng System.Convert để ép kiểu an toàn (Firestore trả về số dạng long/double).
        /// Trả về LessonParameters.Default nếu dict rỗng hoặc null.
        /// </summary>
        public static LessonParameters FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0)
                return Default;

            var result = new LessonParameters();

            // ── Thử parse nested map trước ────────────────────────────────────────

            // --- Actions ---
            if (TryGetSubDict(dict, "actions", out var actionsDict))
            {
                result.Actions.EnableVisualGuidance = GetBool(actionsDict, "enable_visual_guidance", "enableVisualGuidance", result.Actions.EnableVisualGuidance);
                result.Actions.EnableBubbleHints    = GetBool(actionsDict, "enable_bubble_hints",    "enableBubbleHints",    result.Actions.EnableBubbleHints);
                result.Actions.SpeechSilenceTimeout = GetFloat(actionsDict, "speech_silence_timeout", "speechSilenceTimeout", result.Actions.SpeechSilenceTimeout);
                result.Actions.ActionReminderCycle  = GetFloat(actionsDict, "action_reminder_cycle",  "actionReminderCycle",  result.Actions.ActionReminderCycle);
            }
            else
            {
                // Fallback: flat root-level keys
                result.Actions.EnableVisualGuidance = GetBool(dict, "enable_visual_guidance", "enableVisualGuidance", result.Actions.EnableVisualGuidance);
                result.Actions.EnableBubbleHints    = GetBool(dict, "enable_bubble_hints",    "enableBubbleHints",    result.Actions.EnableBubbleHints);
                result.Actions.SpeechSilenceTimeout = GetFloat(dict, "speech_silence_timeout", "speechSilenceTimeout", result.Actions.SpeechSilenceTimeout);
                result.Actions.ActionReminderCycle  = GetFloat(dict, "action_reminder_cycle",  "actionReminderCycle",  result.Actions.ActionReminderCycle);
            }

            // --- Quiz ---
            if (TryGetSubDict(dict, "quiz", out var quizDict))
            {
                result.Quiz.QuizIntroDelay = GetFloat(quizDict, "quiz_intro_delay", "quizIntroDelay", result.Quiz.QuizIntroDelay);
                result.Quiz.QuizSoundGap   = GetFloat(quizDict, "quiz_sound_gap",   "quizSoundGap",   result.Quiz.QuizSoundGap);
                result.Quiz.QuizEndDelay   = GetFloat(quizDict, "quiz_end_delay",   "quizEndDelay",   result.Quiz.QuizEndDelay);
            }
            else
            {
                result.Quiz.QuizIntroDelay = GetFloat(dict, "quiz_intro_delay", "quizIntroDelay", result.Quiz.QuizIntroDelay);
                result.Quiz.QuizSoundGap   = GetFloat(dict, "quiz_sound_gap",   "quizSoundGap",   result.Quiz.QuizSoundGap);
                result.Quiz.QuizEndDelay   = GetFloat(dict, "quiz_end_delay",   "quizEndDelay",   result.Quiz.QuizEndDelay);
            }

            // --- Exploration ---
            if (TryGetSubDict(dict, "exploration", out var exploDict))
            {
                result.Exploration.CameraMoveSpeed       = GetFloat(exploDict, "camera_move_speed",        "cameraMoveSpeed",       result.Exploration.CameraMoveSpeed);
                result.Exploration.SoundToDescriptionGap = GetFloat(exploDict, "sound_to_description_gap", "soundToDescriptionGap", result.Exploration.SoundToDescriptionGap);
            }
            else
            {
                result.Exploration.CameraMoveSpeed       = GetFloat(dict, "camera_move_speed",        "cameraMoveSpeed",       result.Exploration.CameraMoveSpeed);
                result.Exploration.SoundToDescriptionGap = GetFloat(dict, "sound_to_description_gap", "soundToDescriptionGap", result.Exploration.SoundToDescriptionGap);
            }

            return result;
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private static bool TryGetSubDict(Dictionary<string, object> dict, string key, out Dictionary<string, object> subDict)
        {
            subDict = null;
            if (dict.TryGetValue(key, out var raw))
            {
                if (raw is Dictionary<string, object> d)
                {
                    subDict = d;
                    return true;
                }
                if (raw is System.Collections.IDictionary idict)
                {
                    subDict = new Dictionary<string, object>();
                    foreach (System.Collections.DictionaryEntry entry in idict)
                    {
                        subDict[entry.Key.ToString()] = entry.Value;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Đọc bool từ dict với fallback snake_case / camelCase. Trả về defaultVal nếu không tìm thấy.</summary>
        private static bool GetBool(Dictionary<string, object> dict, string snakeKey, string camelKey, bool defaultVal)
        {
            object raw = null;
            if (!dict.TryGetValue(snakeKey, out raw))
                dict.TryGetValue(camelKey, out raw);

            if (raw == null) return defaultVal;
            try   { return Convert.ToBoolean(raw); }
            catch { return defaultVal; }
        }

        /// <summary>Đọc float từ dict với fallback snake_case / camelCase. Trả về defaultVal (-1f) nếu không tìm thấy.</summary>
        private static float GetFloat(Dictionary<string, object> dict, string snakeKey, string camelKey, float defaultVal)
        {
            object raw = null;
            if (!dict.TryGetValue(snakeKey, out raw))
                dict.TryGetValue(camelKey, out raw);

            if (raw == null) return defaultVal;
            try   { return Convert.ToSingle(raw); }
            catch { return defaultVal; }
        }
    }
}
