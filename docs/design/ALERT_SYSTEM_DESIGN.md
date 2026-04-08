# Alert Control System — Design Document

> **Trạng thái:** Đã validate, sẵn sàng implement  
> **Ngày:** 2026-03-18  
> **Phạm vi:** Web Dashboard + Unity VR App (Quest 2)

---

## 1. Tổng quan

Hệ thống **Alert Control Panel** cho phép chuyên gia quản lý cảnh báo hành vi tự động trong buổi học VR. Gồm 2 thành phần chính:

1. **Alert Control Panel** — UI trên Live Session view, bật/tắt nhóm cảnh báo trong thời gian thực
2. **Alert Profile** — Cấu hình riêng cho từng trẻ, lưu threshold và trạng thái mặc định của từng nhóm

---

## 2. Understanding Summary

- **Cái gì được xây dựng:** Hệ thống phát hiện hành vi tự động qua BehaviorSnapshot từ Quest 2, với Alert Control Panel cho chuyên gia quản lý cảnh báo theo nhóm
- **Tại sao tồn tại:** Giảm gánh nặng ghi chép tay cho chuyên gia; cung cấp data hành vi khách quan; hỗ trợ cá nhân hóa theo từng trẻ
- **Đối tượng dùng:** Chuyên gia trị liệu — xem và điều chỉnh; Phụ huynh — chỉ xem kết quả
- **Ràng buộc chính:** Quest 2 (không có Eye Tracking); session tối đa 5 phút; internet cần ổn định để Web nhận stream
- **Non-goals:** Không tự động can thiệp vào bài học; không thay thế quan sát của chuyên gia; không có alert cho phụ huynh trong real-time

---

## 3. Decision Log

| # | Quyết định | Lý do | Thay thế đã xem xét |
|---|-----------|-------|---------------------|
| D1 | Tắt alert ≠ tắt detect — VR vẫn ghi data ngầm | Không bao giờ mất data; chuyên gia chỉ kiểm soát noise trên UI | Tắt hoàn toàn (bị loại: mất data) |
| D2 | Nhóm theo hành vi lâm sàng + màu severity | Chuyên gia thấy ngay "vấn đề gì" và "nghiêm trọng không" | Nhóm theo sensor nguồn (bị loại: không intuitive về lâm sàng) |
| D3 | Alert Profile lưu cả threshold + on/off state | Cá nhân hóa đầy đủ; tự động áp dụng mỗi session | Chỉ lưu threshold hoặc chỉ lưu on/off (bị loại: không đủ) |
| D4 | Chỉ chuyên gia được chỉnh Alert Profile | Phù hợp vai trò lâm sàng; phụ huynh không có đủ context kỹ thuật | Cả phụ huynh được chỉnh (bị loại: rủi ro sai setting) |
| D5 | Chỉnh được ở cả Profile page lẫn Live Session | Linh hoạt; chuyên gia điều chỉnh ngay khi quan sát thực tế | Chỉ chỉnh trước session (bị loại: không thực tế) |
| D6 | Cuối session hỏi lưu nếu có thay đổi | Không làm phiền flow đang quan sát; tránh lưu nhầm | Lưu ngay (bị loại: dễ lưu nhầm khi thử điều chỉnh) |
| D7 | Alert detection logic nằm trên Web | Unity không cần biết business logic; thay đổi không cần update VR build | Logic trong Unity (bị loại: phân tán, khó maintain) |
| D8 | Alert Profile lưu trong Firestore, child_profiles | Dữ liệu cấu hình dài hạn phù hợp Firestore; tái dùng collection hiện có | Collection riêng (bị loại: phức tạp không cần thiết) |
| D9 | BehaviorSnapshot gửi mỗi 2 giây | Đủ để detect event ngắn nhất (5 giây threshold / 2); ~30KB/session (5 phút) | Mỗi frame (quá nhiều); mỗi 5 giây (quá chậm) |

---

## 4. Kiến trúc Tổng thể

```
Unity (Quest 2)
  └─ BehaviorSnapshot mỗi 2 giây
       (head rotation, velocity, deviation_deg, hand velocity, quest_id)
         ↓
    Firebase Realtime DB
    [behavior_snapshots/{session_id}/{timestamp}]
         ↓
    Web Dashboard — AlertEngine
      ├─ Tải Alert Profile từ Firestore (1 lần khi mở session)
      ├─ Subscribe snapshot stream real-time
      ├─ Chạy detection logic (distraction, freeze, hesitation)
      ├─ Filter theo nhóm nào đang ON
      └─ Emit: Alert lên UI + Event vào Firestore (luôn ghi dù ON hay OFF)
         ↓
    Alert Control Panel (UI sidebar)
      ├─ Hiển thị active alerts theo nhóm
      ├─ Toggle bật/tắt từng nhóm
      └─ Slider threshold nhanh qua ⚙️ popover
         ↓
    [Cuối session] End-of-Session Save Dialog
      └─ Nếu có thay đổi → hỏi lưu vào Alert Profile
```

---

## 5. BehaviorSnapshot Schema

Ghi vào: `behavior_snapshots/{session_id}/{timestamp}`

| Field | Mô tả | Nguồn trong Unity |
|-------|-------|-------------------|
| `ts` | Unix timestamp (ms) | Time.time |
| `quest_id` | Quest đang active | QuestController |
| `quest_state` | in_progress / completed / idle | QuestController |
| `head.yaw/pitch/roll` | Góc đầu hiện tại | OVRPlugin |
| `head.angular_velocity` | Tốc độ xoay đầu (rad/s) | Delta rotation / deltaTime |
| `task_zone.target_yaw` | Hướng của quest object hiện tại | Quest object position → yaw |
| `task_zone.deviation_deg` | Chênh lệch góc đầu vs target | abs(head.yaw - target_yaw) |
| `left_hand.velocity` | Tốc độ tay trái (m/s) | OVRInput |
| `right_hand.velocity` | Tốc độ tay phải (m/s) | OVRInput |
| `*.near_object` | Tay có đang gần quest object không | Trigger collider |

**Ước tính tài nguyên:** ~150 snapshots/session × ~200 bytes = ~30KB/session

---

## 6. Alert Groups

### 😰 Stress / Overwhelm `[🔴 High]`
| Signal | Detect khi | Threshold mặc định |
|--------|-----------|-------------------|
| **Freeze** | Không có chuyển động đầu VÀ tay liên tục | 10 giây |
| **Meltdown Proxy** | Freeze + không phản hồi sau NPC nói xong | 15 giây |

### 😵 Mất tập trung `[🟡 Medium]`
| Signal | Detect khi | Threshold mặc định |
|--------|-----------|-------------------|
| **Distraction** | `deviation_deg > 30°` liên tục | 8 giây |
| **Stimming Proxy** | `angular_velocity` dao động lặp lại theo pattern | V2 — cần thêm nghiên cứu |

### 🤔 Khó thực hiện `[🟢 Low]`
| Signal | Detect khi | Threshold mặc định |
|--------|-----------|-------------------|
| **Hesitation** | Tay near_object=true nhiều lần mà không complete | 3 lần |
| **Idle Time** | Không có tương tác nào trong quest | 12 giây |

---

## 7. Alert Event Object

Lưu vào: `sessions/{session_id}/auto_alerts/{alert_id}`  
*(Luôn ghi dù nhóm đang ON hay OFF — field `suppressed` ghi lại trạng thái)*

| Field | Mô tả |
|-------|-------|
| `alert_id` | UUID |
| `session_id` | ID buổi học |
| `child_id` | ID trẻ |
| `quest_index` | Số thứ tự Quest đang active (Int: 0, 1) |
| `type` | freeze / distraction / hesitation / stimming / idle |
| `group` | stress_overwhelm / distraction / execution_difficulty |
| `severity` | high / medium / low |
| `time_offset` | Giây thứ mấy trong buổi học (Float) |
| `duration_sec` | Kéo dài bao lâu |
| `auto_detected` | Luôn = true |
| `suppressed` | true nếu nhóm đang OFF khi event xảy ra |

---

## 8. Alert Profile Schema

Lưu vào: `child_profiles/{child_id}/alert_profile`

```
alert_profile:
  thresholds:
    distraction_threshold_sec: 8
    freeze_threshold_sec: 10
    deviation_angle_deg: 30
    idle_threshold_sec: 12
    hesitation_count: 3
  
  groups:
    stress_overwhelm:
      enabled: true
      severity: high
    distraction:
      enabled: true  
      severity: medium
    execution_difficulty:
      enabled: true
      severity: low
  
  last_updated_by: {expert_uid}
  last_updated_at: {timestamp}
  version: 1
```

---

## 9. Alert Control Panel UI

**Vị trí:** Sidebar phải của Live Session view

**Layout:**
```
┌──────────────────────┐
│  ALERT CONTROL        │
│  ─────────────────   │
│  😰 Stress    🔴 [ON] │
│  😵 Distraction🟡 [ON]│
│  🤔 Khó thực hiện     │
│            🟢 [OFF]  │
│  ─────────────────   │
│  🔔 ALERTS HIỆN TẠI  │
│  > FREEZE 12s ●●●    │
│  > IDLE 15s ●        │
└──────────────────────┘
```

**Trạng thái visual:**
| Trạng thái | Hiển thị |
|----------|---------|
| ON + không có alert | Tên nhóm + màu nhạt |
| ON + đang có alert | Badge số đỏ nhấp nháy |
| OFF | Mờ, gạch ngang |
| OFF nhưng có event ngầm | Badge xám nhỏ — "đang ghi ngầm" |

**Chỉnh threshold nhanh:** Nút ⚙️ bên cạnh tên nhóm → popover slider, không cần vào Profile page

---

## 10. End-of-Session Save Flow

```
Chuyên gia bấm "Kết thúc buổi học"
  │
  ├─ [Không có thay đổi] → Kết thúc bình thường
  │
  └─ [Có thay đổi] → Hiện dialog:
       "Trong buổi này bạn đã thay đổi:
        • 🤔 Khó thực hiện: ON → OFF
        • Freeze threshold: 10s → 15s
        Áp dụng cho các buổi sau của [Tên trẻ]?"
       
       [Lưu vào Profile] → Ghi vào Firestore → Áp dụng từ buổi sau
       [Chỉ buổi này]   → Không lưu → Buổi sau dùng lại setting cũ
```

---

## 11. Implementation Priority

### Đợt 1 — Nền tảng (cần làm trước)
1. **BehaviorLogger.cs (Unity)** — Ghi BehaviorSnapshot mỗi 2 giây lên Realtime DB
2. **Alert Profile schema (Firestore)** — Thêm `alert_profile` vào `child_profiles`
3. **AlertEngine (Web)** — Core detection logic: freeze + distraction + idle

### Đợt 2 — UI
4. **Alert Control Panel** — Sidebar với toggle và active alerts
5. **End-of-session dialog** — Hỏi lưu Alert Profile khi có thay đổi
6. **Profile page** — UI chỉnh Alert Profile trước session

### Đợt 3 — Nâng cao
7. **Hesitation detection** — near_object tracking trong Unity
8. **Stimming proxy** — Angular velocity pattern analysis (cần thêm nghiên cứu về threshold)
9. **Session replay timeline** — Xem lại BehaviorSnapshot dưới dạng timeline sau buổi học

---

## 12. Assumptions

| # | Assumption |
|---|-----------|
| A1 | Internet ổn định trong buổi học để Web nhận snapshot real-time |
| A2 | Delay 2-3 giây cho alert là chấp nhận được (chuyên gia vẫn có mặt trong phòng) |
| A3 | Mỗi trẻ có một Alert Profile duy nhất (không phân theo loại bài học) |
| A4 | NPC voice line duration được gửi kèm để tính grace period sau khi NPC nói xong |
| A5 | Stimming detection V1 là manual (chuyên gia bấm tay) — auto detect để V2 |

---

## 13. Open Questions (để sau)

- Threshold 30° cho distraction có phù hợp với layout thực tế của các scene không? (cần test thực tế)
- Grace period sau NPC nói xong là bao nhiêu giây? (đề xuất: 2 giây)
- Có cần alert history log trong UI không, hay chỉ hiện alert đang active?
