# 🔭 Thiết kế Hệ thống Telemetry V2: Buffer-Aggregate

**Ngày tạo:** 2026-04-18  
**Cập nhật lần cuối:** 2026-04-19  
**Trạng thái:** Đã validate qua Brainstorming, sẵn sàng implement  
**Context:** Nâng cấp từ "Chụp 1 snapshot mỗi 2s" lên "Ghi liên tục 50Hz → Tổng hợp thông minh → Đẩy mỗi 2s"

---

## 1. Tóm tắt Đích đến (Understanding Summary)

- **Mục tiêu:** Nâng cấp SensorHarvester từ chế độ "chụp ảnh tĩnh mỗi 2 giây" sang chế độ "quay phim liên tục 50Hz rồi cắt trailer mỗi 2 giây". Điều này giúp bắt được các hành vi ngắn (stimming, liếc nhanh, chần chừ) mà bản V1 bỏ lỡ hoàn toàn.
- **Tại sao:** Dữ liệu Y khoa cần độ phân giải cao để máy chủ AlertEngine nhận diện chính xác triệu chứng Stimming, Freeze, Hesitation. Một snapshot duy nhất mỗi 2 giây dễ bỏ sót hành vi chỉ kéo dài 0.5 giây.
- **Cho ai:** Chuyên gia trị liệu trên Web Dashboard — nhận dữ liệu được tổng hợp sẵn, giàu thông tin nhưng gọn nhẹ.
- **Ràng buộc:** Không ảnh hưởng FPS kính VR (Quest 2/3). Không tăng băng thông Firebase Spark.
- **Non-goals:** VR **không** đưa ra kết luận y khoa (Alert/Stimming). VR chỉ gửi các con số đặc trưng (Feature), Web Dashboard là nơi phán quyết dựa trên ngưỡng (Threshold) của chuyên gia.

---

## 2. Giả định (Assumptions)

| # | Assumption |
|---|-----------|
| A1 | `FixedUpdate` (0.02s = 50Hz) là nơi ghi mẫu — đảm bảo tần suất ổn định, không phụ thuộc frame rate |
| A2 | Bộ đệm dùng mảng cố định (pre-allocated) kích thước 100 phần tử — tránh GC Allocation trên kính VR |
| A3 | Mỗi cửa sổ 2 giây tạo ra 100 mẫu thô (internal) → 1 bản tóm tắt (gửi đi) |
| A4 | Kính VR có đủ năng lực tính `Vector3.Angle`, `Vector3.Distance` 50 lần/giây mà không ảnh hưởng hiệu năng |
| A5 | Các vật thể mục tiêu (Quest) đều được gắn Collider để Gaze Cone tính toán được |

---

## 3. Nhật ký Quyết định (Decision Log)

| # | Vấn đề | Quyết định | Thay thế đã xem xét | Lý do chọn |
|---|--------|-----------|---------------------|-----------|
| D1 | **Tần suất gửi RTDB** | Giữ nguyên **2 giây/lần** | 0.5s (quá nhiều network call), 5s (quá chậm) | Cân bằng giữa real-time và bandwidth Firebase Spark |
| D2 | **Tần suất lấy mẫu** | **0.02s** (FixedUpdate, 50Hz) | Mỗi frame (GC nặng), 0.05s (kém chính xác), 0.1s (bỏ sót stimming nhanh) | 50Hz là chuẩn nghiên cứu y khoa, đồng bộ với FixedUpdate mặc định của Unity |
| D3 | **Dạng dữ liệu gửi** | **1 bản tóm tắt tổng hợp** (Aggregated) | Batch thô (quá nặng), Lệnh Alert (mất dữ liệu vẽ biểu đồ) | Giữ nguyên bandwidth, tăng chất lượng thông tin |
| D4 | **Tổng hợp Velocity** | **Peak + Average** | Chỉ Peak (mất bối cảnh), chỉ Average (mất đỉnh stimming), Peak+Spike Count (phức tạp) | 2 con số đủ kể câu chuyện: "Có cú xoay mạnh 20°/s nhưng trung bình chỉ 2.5°/s → xảy ra 1 lần" |
| D5 | **Tổng hợp Focus** | **focus_ratio + Dominant Object + Expected Target** | Chỉ ratio (không biết phân tâm bởi cái gì), chỉ object (mất tỷ lệ) | Một dòng log tự kể câu chuyện hoàn chỉnh cho bác sĩ |
| D6 | **Tổng hợp Hand Proximity** | **hand_near_ratio + min_hand_distance** | Boolean (mất thông tin chần chừ), chỉ ratio (không biết gần tới mức nào) | Phát hiện Hesitation: "Tay tiến gần 12cm mà 33% thời gian nhưng không chạm" |
| D7 | **Góc xoay đầu tuyệt đối** | **Bỏ hoàn toàn** (head_rotation_y, head_rotation_x) | Giữ cả hai (thừa, phụ thuộc vị trí đứng) | Góc tuyệt đối không mang ý nghĩa lâm sàng. Vận tốc xoay mới cho biết kích động hay bình tĩnh |
| D8 | **is_focusing_expected_target** | **Thay bằng focus_ratio** (0.0→1.0) | Giữ Boolean (thô, mất nuance) | Web tự phán quyết dựa trên ngưỡng của bác sĩ. VR không quyết True/False |
| D9 | **target_distance** | **Bỏ**, thay bằng min_hand_distance | Giữ (phụ thuộc chiều cao trẻ, không chuẩn) | Khoảng cách tay→vật quan trọng hơn mắt→vật vì tay là điểm tương tác thực tế |
| D10 | **Triết lý phân công** | **VR tính Toán Không Gian, Web tính Logic Nghiệp Vụ** | Tất cả trên VR (khó cập nhật ngưỡng), tất cả trên Web (không có dữ liệu 3D) | Như Apple Watch: đồng hồ đo nhịp tim (con số), iPhone phán "nhịp tim cao" (logic) |

---

## 4. Kiến trúc Tổng thể

```
Unity (Quest 2/3)
  │
  ├─ FixedUpdate (0.02s = 50Hz)
  │     └─ SensorHarvester.SampleToBuffer()
  │           - Đo: Head velocity, Angular velocity X/Y, Hand velocity L/R
  │           - Đo: Gaze Cone → is_in_cone (bool), focus_object_name (string)
  │           - Đo: Hand proximity → distance to target
  │           - Lưu vào: RawSample[100] (mảng cố định, circular buffer)
  │
  ├─ Mỗi 2 giây (Coroutine)
  │     └─ TelemetryStreamer gọi SensorHarvester.AggregateAndFlush()
  │           - Tính: Peak + Avg cho tất cả velocity
  │           - Tính: focus_ratio, dominant focus_object
  │           - Tính: hand_near_ratio, min_hand_distance
  │           - Đóng gói → AggregatedSnapshot
  │           - Xóa sạch buffer (reset con trỏ về 0)
  │
  └─ TelemetryUploader.PushAggregatedSnapshot()
        - Serialize thành JSON (~300 bytes)
        - Gửi lên RTDB: behavior_snapshots/{session_id}/{timestamp}
              ↓
        Firebase Realtime DB
              ↓
        Web Dashboard — AlertEngine
          ├─ Nhận stream real-time
          ├─ So sánh focus_ratio với ngưỡng chuyên gia
          ├─ So sánh velocity peak với ngưỡng stimming
          └─ Emit Alert lên UI
```

---

## 5. Đặc tả Dữ liệu

### 5.1. RawSample (Internal)

Struct nhẹ, lưu trong mảng cố định. Không cần Serializable.

| Trường | Kiểu | Mô tả |
|--------|------|-------|
| `headVelocity` | float | Vận tốc tịnh tiến đầu tại thời điểm đó (m/s) |
| `angularVelX` | float | Vận tốc góc Pitch (°/s) |
| `angularVelY` | float | Vận tốc góc Yaw (°/s) |
| `leftHandVel` | float | Vận tốc tay trái (m/s) |
| `rightHandVel` | float | Vận tốc tay phải (m/s) |
| `isInGazeCone` | bool | True nếu target nằm trong nón 20° tại thời điểm đó |
| `focusObjectName` | string | Tên vật đang nhìn trúng (Raycast hit hoặc target nếu in cone) |
| `handDistance` | float | Khoảng cách tay gần nhất → target (m). -1 nếu không có target |

**Kích thước ước tính:** ~50 bytes/mẫu × 100 mẫu = **~5 KB/buffer** (trong RAM, không gửi đi)

### 5.2. AggregatedSnapshot (Gửi lên RTDB)

Đường dẫn RTDB: `behavior_snapshots/{session_id}/{timestamp}`

| Trường | Kiểu | Mô tả | Cách tổng hợp từ buffer |
|--------|------|-------|------------------------|
| `time_offset` | float | Giây thứ mấy của buổi học | Thời điểm kết thúc cửa sổ 2s |
| `head_vel_avg` | float | Vận tốc tịnh tiến đầu TB (m/s) | Trung bình 100 mẫu `headVelocity` |
| `head_vel_peak` | float | Vận tốc tịnh tiến đầu đỉnh (m/s) | Max của 100 mẫu `headVelocity` |
| `ang_vel_x_avg` | float | Vận tốc góc Pitch TB (°/s) | Trung bình 100 mẫu `angularVelX` |
| `ang_vel_x_peak` | float | Vận tốc góc Pitch đỉnh (°/s) | Max của 100 mẫu `angularVelX` |
| `ang_vel_y_avg` | float | Vận tốc góc Yaw TB (°/s) | Trung bình 100 mẫu `angularVelY` |
| `ang_vel_y_peak` | float | Vận tốc góc Yaw đỉnh (°/s) | Max của 100 mẫu `angularVelY` |
| `left_hand_vel_avg` | float | Vận tốc tay trái TB (m/s) | Trung bình 100 mẫu `leftHandVel` |
| `left_hand_vel_peak` | float | Vận tốc tay trái đỉnh (m/s) | Max của 100 mẫu `leftHandVel` |
| `right_hand_vel_avg` | float | Vận tốc tay phải TB (m/s) | Trung bình 100 mẫu `rightHandVel` |
| `right_hand_vel_peak` | float | Vận tốc tay phải đỉnh (m/s) | Max của 100 mẫu `rightHandVel` |
| `focus_object` | string | Vật trẻ nhìn nhiều nhất trong 2s | Đếm tần suất `focusObjectName`, lấy vật có count cao nhất |
| `expected_target` | string | Quest mục tiêu đang yêu cầu | Lấy từ `_currentQuestTarget` tại thời điểm aggregate |
| `focus_ratio` | float | Tỷ lệ tập trung (0.0–1.0) | Số mẫu `isInGazeCone == true` ÷ tổng số mẫu |
| `hand_near_ratio` | float | Tỷ lệ tay gần mục tiêu (0.0–1.0) | Số mẫu có `handDistance <= 0.3m` ÷ tổng số mẫu |
| `min_hand_dist` | float | Khoảng cách gần nhất tay→target (m) | Min của 100 mẫu `handDistance` (bỏ qua -1) |

**Kích thước JSON ước tính:** ~350 bytes/snapshot × 150 snapshots/session (5 phút) = **~52 KB/session**

---

## 6. Thiết kế Chi tiết (Component Design)

### 6.1. SensorHarvester (Nâng cấp)

**Trách nhiệm:** Thu thập dữ liệu thô mỗi 0.02s và tổng hợp khi được yêu cầu.

**Thay đổi so với V1:**
- ~~`TakeSnapshot()`~~ → `SampleToBuffer()` (gọi mỗi FixedUpdate) + `AggregateAndFlush()` (gọi mỗi 2s)
- Thêm trạng thái nội bộ: `_lastHeadPos` để tính vận tốc tịnh tiến đầu
- Thêm `_lastHeadRotX` để tính vận tốc góc Pitch
- Bộ đệm: `RawSample[] _buffer` kích thước 100, biến `_bufferCount` đếm số mẫu hiện tại

**Luồng xử lý:**

```
FixedUpdate() ─── Mỗi 0.02s ───────────────────────────────
│
├─ Tính deltaTime = fixedDeltaTime
├─ Tính headVelocity = Distance(headPos, lastHeadPos) / dt
├─ Tính angVelX = Abs(DeltaAngle(lastPitchX, pitchX)) / dt
├─ Tính angVelY = Abs(DeltaAngle(lastYawY, yawY)) / dt
├─ Tính leftVel, rightVel (tương tự V1)
├─ Raycast + Gaze Cone → focusObjectName, isInGazeCone
├─ Tính handDistance = Min(leftDist, rightDist) to target
├─ Ghi vào _buffer[_bufferCount++]
└─ Cập nhật _lastXxx

AggregateAndFlush(timeOffset) ─── Mỗi 2s ──────────────────
│
├─ Duyệt _buffer[0.._bufferCount-1]:
│   ├─ Tính sum, max cho mỗi trường velocity
│   ├─ Đếm isInGazeCone == true → focusCount
│   ├─ Đếm handDistance <= threshold → nearCount
│   ├─ Min handDistance (bỏ qua -1)
│   └─ Đếm tần suất focusObjectName → tìm dominant
│
├─ Tính avg = sum / count, peak = max
├─ focus_ratio = focusCount / count
├─ hand_near_ratio = nearCount / count
├─ Đóng gói → AggregatedSnapshot
├─ Reset: _bufferCount = 0
└─ Return AggregatedSnapshot
```

### 6.2. TelemetryStreamer (Nâng cấp nhẹ)

**Thay đổi:**
- Không gọi `TakeSnapshot()` nữa.
- Gọi `AggregateAndFlush(elapsed)` mỗi 2 giây.
- Mọi thứ khác giữ nguyên.

### 6.3. TelemetryUploader (Thay đổi nhẹ)

**Thay đổi:**
- Đổi tham số từ `BehaviorSnapshot` → `AggregatedSnapshot`.
- Logic serialize/push giữ nguyên.

### 6.4. AggregatedSnapshot Model (Thay thế BehaviorSnapshot)

**Thay đổi:**
- File mới hoặc rename `BehaviorSnapshot.cs` → `AggregatedSnapshot.cs`
- Cập nhật toàn bộ trường theo bảng ở mục 5.2.

---

## 7. Tác động lên Alert System (ALERT_SYSTEM_DESIGN.md)

Bảng ánh xạ từ trường cũ sang trường mới cho AlertEngine trên Web:

| Alert | Trường cũ | Trường mới | Logic detect mới |
|-------|----------|-----------|-----------------|
| **Distraction** | `is_focusing_expected_target == false` kéo dài | `focus_ratio < threshold` liên tiếp N cửa sổ | Chính xác hơn: 0.1 vs 0.4 cho biết mức độ mất tập trung |
| **Freeze** | `angular_velocity == 0` + `hand_vel == 0` | `ang_vel_y_peak < ε` + `head_vel_peak < ε` + `hand_vel_peak < ε` | Dùng peak thay avg: nếu peak cũng gần 0 → thực sự đông cứng |
| **Stimming** | `angular_velocity` dao động (V2) | `ang_vel_y_peak` cao + `ang_vel_y_avg` trung bình | Peak cao + avg trung bình = lắc nhiều lần (pattern lặp) |
| **Hesitation** | `hand_near_target == true` nhiều lần | `hand_near_ratio > 0` + `min_hand_dist < 0.15` liên tiếp | Tay tiến gần nhưng không chạm, kéo dài qua nhiều cửa sổ |

---

## 8. Ước tính Hiệu năng & Tài nguyên

### Trên Kính VR (Quest 2/3)
| Hạng mục | Giá trị | Đánh giá |
|----------|---------|---------|
| Phép tính/FixedUpdate | ~10 phép Vector3 + 1 Raycast | Cực nhẹ (< 0.01ms) |
| RAM cho buffer | 100 × ~50 bytes = 5 KB | Không đáng kể |
| GC Allocation | Không có (mảng pre-allocated, struct) | An toàn cho VR |
| Network call/2s | 1 lần push JSON ~350 bytes | Giữ nguyên như V1 |

### Trên Firebase Spark (Miễn phí)
| Hạng mục | Giá trị |
|----------|---------|
| Dung lượng 1 session (5 phút) | ~52 KB |
| Số session lưu được (1 GB) | ~19.600 sessions |
| Bandwidth/session (Web xem) | ~52 KB download |
| Số lần xem/tháng (10 GB) | ~196.000 lần |

---

## 9. Kế hoạch Triển khai (Implementation Priority)

### Bước 1 — Model & Buffer
1. Tạo `RawSample` struct (internal)
2. Tạo `AggregatedSnapshot` class (thay thế `BehaviorSnapshot`)
3. Cập nhật `TelemetryUploader` để nhận `AggregatedSnapshot`

### Bước 2 — SensorHarvester V2
4. Thêm bộ đệm `RawSample[100]` + biến `_bufferCount`
5. Triển khai `SampleToBuffer()` trong `FixedUpdate`
6. Triển khai `AggregateAndFlush()` với logic Peak/Avg/Ratio
7. Cập nhật Gizmos để hiển thị trạng thái mới

### Bước 3 — TelemetryStreamer V2
8. Đổi từ gọi `TakeSnapshot()` sang `AggregateAndFlush()`

### Bước 4 — Cập nhật Web Dashboard
9. Cập nhật AlertEngine để dùng trường mới (`focus_ratio`, `peak`, `avg`)
10. Cập nhật biểu đồ real-time để hiển thị dữ liệu mới

---

## 10. Câu hỏi Mở (Open Questions)

- Ngưỡng `focus_ratio` mặc định chuyên gia nên đặt là bao nhiêu? (Đề xuất: 0.5 = 50% thời gian)
- Có cần thêm `body_rocking_score` (kết hợp `head_vel` + `ang_vel_x`) vào V3 không?
- Khi không có Quest nào đang active, có nên vẫn ghi buffer không hay tạm dừng?
