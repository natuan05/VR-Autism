using System;

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
    }
}
