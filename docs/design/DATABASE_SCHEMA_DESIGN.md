# 🗄️ Kiến trúc Database & Hệ thống dữ liệu

> **Công nghệ đề xuất: Hybrid Model (Kết hợp 2 loại Database)**
> 1. **Cloud Firestore:** Lưu trữ bền vững, query/filter phức tạp. Dùng cho Users, Profiles, Sessions, Lessons.
> 2. **Firebase Realtime Database:** Kênh truyền siêu tốc, độ trễ thấp cho dữ liệu tạm thời: PIN Pairing, Remote Commands.

> **Nguyên tắc thiết kế: Flat Structure with References** – Các collection đứng ngang hàng nhau ở cấp top-level, nối với nhau qua ID. Không lồng `sessions` vào `child_profiles` vì Chuyên gia cần query chéo nhiều trẻ cùng lúc mà không bị giới hạn bởi đường dẫn của một user cụ thể.

### 5.1 Quan hệ giữa các Collection (Firestore)

```mermaid
erDiagram
    %% ── TẦNG 0: SYSTEM ADMIN ──────────────────────────────────
    SYSTEM_ADMINS ||--o{ CENTERS : "tạo & cấp phép"
    SYSTEM_ADMINS {
        string uid PK
        string name
        string email
    }

    %% ── TẦNG 1: TRUNG TÂM ─────────────────────────────────────
    CENTERS ||--|| CENTER_MANAGERS : "có đại diện"
    CENTERS ||--o{ EXPERTS : "bao gồm"
    CENTERS {
        string center_id PK
        string name "Tên trung tâm"
        string address
        string manager_uid FK "Trỏ về center_managers"
        string created_by FK "Trỏ về system_admins"
    }

    %% ── TẦNG 2: QUẢN LÝ TRUNG TÂM ────────────────────────────
    CENTER_MANAGERS ||--o{ EXPERTS : "tạo tài khoản"
    CENTER_MANAGERS ||--o{ PARENTS : "tạo & liên kết"
    CENTER_MANAGERS {
        string uid PK
        string name
        string email
        string center_id FK "Thuộc trung tâm"
    }

    %% ── TẦNG 3: CHUYÊN GIA ────────────────────────────────────
    EXPERTS ||--o{ CHILD_PROFILES : "quản lý hồ sơ"
    EXPERTS {
        string uid PK
        string name
        string email
        string center_id FK "Thuộc trung tâm"
    }

    %% ── TẦNG 4: PHỤ HUYNH ────────────────────────────────────
    PARENTS {
        string uid PK
        string name
        string email
        string linked_expert_uid FK "Chuyên gia đang phối hợp"
    }
    PARENTS }o--|| EXPERTS : "liên kết với"

    %% ── DỮ LIỆU LÂM SÀNG ────────────────────────────────────
    CHILD_PROFILES {
        string profile_id PK
        string expert_uid FK "Trỏ về experts (chuyên gia quản lý)"
        string parent_uid FK "Trỏ về parents (optional)"
        string center_id FK "Dùng cho Firestore isolation rules"
        string display_name "Bé Nam"
        int age
        float height_cm "Căn chỉnh Camera Rig VR"
        int sound_sensitivity "1-5"
        int attention_span_min
        array anxiety_triggers
        string diagnosis_notes
        map default_lesson_params "Thông số mặc định cho bài học"
        map alert_profile "Cấu hình Alert Control cho từng trẻ"
    }

    LESSONS {
        string lesson_id PK "vd: washing_hand"
        string name "Rửa tay"
        string type "practical | theory"
        int min_age
        int duration_min
        array skills_trained
        array quests_definition "Mảng định nghĩa các quest tĩnh"
        map levels "level_0, level_1, level_2"
    }

    PARENTS ||--o{ CHILD_PROFILES : "theo dõi tiến trình"
    CHILD_PROFILES ||--o{ SESSIONS : "tham gia"
    EXPERTS ||--o{ SESSIONS : "host buổi học"
    LESSONS ||--o{ SESSIONS : "thuộc bài học"
    SESSIONS {
        string session_id PK
        string child_profile_id FK "Trỏ về child_profiles"
        string hosted_by FK "Trỏ về experts (chuyên gia)"
        string lesson_id FK "Trỏ về lessons"
        string level_name "Cơ bản (denormalized để query nhanh)"
        int level_index
        timestamp start_time
        timestamp finish_time
        float duration "Giây"
        string completion_status "success | aborted | timeout"
        int score
        string video_url "Link clip buổi học (nếu có)"
        map params_used "Settings thực tế hôm đó: voice_speed, visual_cues..."
    }

    SESSIONS ||--o{ QUEST_LOGS : "chi tiết từng bước"
    QUEST_LOGS {
        string log_id PK
        int index
        float response_time "Giây"
        string completion_status "success | skipped | assisted"
        int hints_verbal "Nhắc bằng lời (audio qua tai nghe)"
        int hints_visual "Visual cue: mũi tên / highlight trong VR"
        int hints_physical "Cầm tay chỉ việc (ghi tay bởi chuyên gia)"
    }

    SESSIONS ||--o{ INTERVENTION_LOGS : "can thiệp của chuyên gia"
    INTERVENTION_LOGS {
        string log_id PK
        string child_id "Tham chiếu trẻ (tối ưu Group Query)"
        float time_offset "Giây kể từ đầu buổi"
        int quest_index "Đang ở Quest số mấy"
        string command_type "verbal_hint | visual_cue | audio_cue | pause | skip_quest | adjust_level | play_encouragement | emergency_stop"
        string command_detail "vd: play_audio:step2_hint, set_level:easy"
        string note "Ghi chú ngắn của chuyên gia (optional)"
        string triggered_by "uid_expert1"
    }

    SESSIONS ||--o{ AUTO_ALERTS : "phát sinh cảnh báo tự động"
    AUTO_ALERTS {
        string alert_id PK
        string child_id "Tham chiếu trẻ (tối ưu Group Query)"
        float time_offset "Giây thứ mấy kể từ đầu buổi"
        int quest_index "Số thứ tự Quest tương ứng"
        string type "freeze | distraction | hesitation | idle"
        string group "stress_overwhelm | distraction | execution_difficulty"
        string severity "high | medium | low"
        float duration_sec "Kéo dài bao lâu"
        boolean auto_detected "true mặc định"
        boolean suppressed "true nếu đang tắt trên UI"
    }

    SESSIONS ||--o{ BEHAVIOR_LOGS : "ghi nhận hành vi"
    BEHAVIOR_LOGS {
        string log_id PK
        string child_id "Tham chiếu trẻ"
        float time_offset "Giây kể từ đầu buổi"
        string event "meltdown | distraction | stimming"
        string triggered_by "teacher | auto"
        string note "Mô tả ngắn của giáo viên"
    }
```

---

### 5.2 Firebase Realtime Database (Volatile – Tạm thời)

> Chỉ lưu dữ liệu "sống" trong buổi học. Tự động xóa sau khi kết thúc.

```mermaid
erDiagram
    PAIRING_CODES {
        string pin_6_digit PK "vd: 123456 (Auto-delete sau 5 phút)"
        string device_id "QUEST_PRO_001"
        string status "waiting | paired"
        timestamp created_at
    }

    LIVE_SESSIONS {
        string session_id PK "Id của buổi đang diễn ra"
        map commands "Web → VR: set_object_speed, force_hint, play_npc_script..."
        map vr_state "VR → Web: current_quest_idx, current_quest_name"
    }

    BEHAVIOR_SNAPSHOTS {
        string session_id PK "Id của buổi đang diễn ra"
        map snapshots_by_timestamp "Key là timestamp (mỗi 2s). Value là snapshot telemetry data"
    }
```

---
