# 📱 Ý tưởng phát triển VR App

> Đào sâu và cải thiện ứng dụng VR hiện có

---

## 🎯 Mục tiêu

Nâng cấp VR App hiện tại để:
- Thu thập dữ liệu phong phú hơn
- Hỗ trợ cá nhân hóa cho từng trẻ
- Bổ sung thêm bài học mới
- Tối ưu hóa trải nghiệm

---

## 📊 1. Nâng cấp hệ thống Data Collection

Dựa trên thiết kế Schema mới nhất (`DATABASE_SCHEMA_DESIGN.md`), hệ thống thu thập dữ liệu tự động của VR được chia làm 2 cấp độ:

### 1.1 Khối lượng nhẹ: Quest Logs (Ghi nhận sau mỗi Quest)
*Dữ liệu tĩnh (Static Data), ghi nhận khi trẻ hoàn thành một bước nhỏ (Quest).*

| Metric | Mô tả (Thu thập trong Unity) |
|--------|------------------------------|
| `response_time` | Thời gian từ lúc nhận Quest đến lúc kích hoạt đúng vật thể |
| `completion_status` | Trạng thái: `success` (tự làm), `assisted` (có gợi ý), `skipped` |
| `hints_verbal` | Số lần hệ thống (hoặc chuyên gia) nhấn nút phát âm thanh gợi ý |
| `hints_visual` | Số lần hệ thống làm sáng vật thể đích (Highlight) |
| `hints_physical` | (Ghi nhận bằng tay từ Web) Số lần chuyên gia cầm tay trẻ chỉ việc |

### 1.2 Khối lượng nặng: Behavior Snapshots (Chụp mỗi 2 giây)
*Luồng Telemetry siêu tốc, bắn qua Realtime Database để Web AI phân tích.*

| Cảm biến | Dữ liệu gốc (Raw Data) | Mục đích phân tích (Web Engine) |
|----------|------------------------|---------------------------------|
| **Head Kinematics** | `head_pitch_yaw`, Vận tốc xoay cổ | Phát hiện né tránh ánh nhìn (Distraction) |
| **Hand Kinematics** | `left_hand_velocity`, `right_hand_velocity` | Dò tìm dao động lặp lại (Stimming) |
| **Physical Status** | gia tốc (tay/đầu) = ~0 trong 10s | Cảnh báo quá tải/căng thẳng (Freeze) |
| **Gaze Target** | `focus_object` (Vật đang trực tiếp nhìn) | Phân tích Joint Attention |
| **Hand Proximity** | `near_object = true` lặp lại | Đánh giá chần chừ (Hesitation) |

### 1.3 Behavior Tracking (Giám sát Hành vi tóm tắt)

Sự phân tách giữa hành vi máy đo & người đo:

| Phân loại | Hành vi | Nguồn thu thập | Cơ sở Y khoa / Lâm sàng |
|-----------|----------|---------------|--------------|
| **Auto_Alert** | Freeze (Đứng hình) | Gia tốc tay/đầu = 0 | Sensory Overload / Stress response |
| **Auto_Alert** | Distraction (Xao nhãng) | Angle > 30 độ | Né tránh ánh nhìn (Gaze aversion) |
| **Auto_Alert** | Stimming (Tự kích thích) | Dao động gia tốc tay | Điều hòa thần kinh (Rhythmic movements) |
| **Manual Log** | Meltdown / Cáu gắt | Chuyên gia bấm nút (Web) | Cảm quan trực tiếp không thể đo bằng máy |
| **Manual Log** | Phản ứng tích cực | Chuyên gia bấm nút (Web) | Khuyến khích hành vi tốt |

---

## ⚙️ 2. Hệ thống Settings & Cá nhân hóa

### 2.1 Hồ sơ Cá nhân Trẻ (Child Profile)

> Lưu tại `users/{user_id}/profile` trên Firebase. Được thiết lập bởi chuyên gia/phụ huynh trên Web Dashboard trước buổi học.

| Trường | Mô tả | Ví dụ |
|--------|-------|-------|
| `display_name` | Tên hiển thị | "Bé Nam" |
| `age` | Tuổi | 7 |
| `height_cm` | Chiều cao (cm) – dùng để căn camera VR | 110 |
| `sound_sensitivity` | Khả năng chịu đựng âm thanh (1-5) | 3 |
| `attention_span_min` | Thời gian tập trung ước tính (phút) | 10 |
| `anxiety_triggers` | Các yếu tố gây lo lắng (mảng text) | ["đám đông", "tiếng ồn lớn"] |
| `preferred_lessons` | Bài học yêu thích | ["Farm", "Zoo"] |
| `diagnosis_notes` | Ghi chú chẩn đoán (chuyên gia nhập) | "ASD Level 1" |

### 2.2 Phân loại Bài học

> Mỗi bài học có metadata để hệ thống gợi ý và lọc phù hợp.

| Thuộc tính | Mô tả | Ví dụ |
|------------|-------|-------|
| `age_range` | Độ tuổi phù hợp | [5, 10] |
| `duration_min` | Thời gian ước tính (phút) | 15 |
| `skills` | Kỹ năng rèn luyện | ["tự phục vụ", "giao tiếp"] |
| `level` | Độ khó 1–3 | 2 |

### 2.3 Tuỳ Chỉnh Thông Số Từng Bài Học (Per-lesson Parameters)

> Khác với việc set cứng "Độ khó" (Easy/Normal/Hard) cho cả trò chơi, mỗi bài học sẽ có các thông số **tuỳ chỉnh chi tiết riêng biệt**, giúp chuyên gia/phụ huynh điều chỉnh chính xác theo nhu cầu của trẻ ở bài học đó.

| Thông số (Parameter) | Ý nghĩa | Default |
|----------------------|-------|---------|
| `reminder_interval` | Thời gian chờ (giây) trước khi phát ra gợi ý nhắc nhở trẻ | `10s` |
| `max_hints` | Quá số lần gợi ý này, hệ thống coi như trẻ chưa tự làm được | `3` |
| `npc_voice_speed` | Tốc độ nói của nhân vật (NPC) trong bài | `1.0` (Bình thường) |
| `visual_cues` | Bật/tắt các mũi tên, viền sáng hướng dẫn trực quan | `true` |
| `quest_complexity` | Số lượng bước/quest con phải làm (Ví dụ: ít bước vs đầy đủ bước) | `Normal` |
| `object_move_speed` | Tốc độ di chuyển của xe cộ, người qua đường, vật thể | `1.0` |
| `env_volume` | Âm lượng tiếng ồn môi trường | `0.5` |
| `time_limit` | Giới hạn thời gian tối đa để hoàn thành bài tập | `0` (Không có) |



---

## 📚 3. Bổ sung bài học mới

### Các Scene hiện có

| Scene | Chủ đề | Trạng thái |
|-------|--------|------------|
| Bathroom | Rửa tay | ✅ Hoàn thành |
| Farm | Trang trại | ✅ Hoàn thành |
| Zoo | Sở thú | ✅ Hoàn thành |
| Supermarket | Siêu thị | ⚠️ Cần nâng cấp (xem bên dưới) |
| Ocean | Biển | ✅ Hoàn thành |
| Classroom | Lớp học | ✅ Hoàn thành |

### Nâng cấp Scene hiện có

#### 🛒 Supermarket – Nâng cấp "Mua đồ siêu thị"

> Scene đã tồn tại nhưng cần bổ sung luồng tương tác phức tạp hơn.

- Nhìn tổng quan các kệ hàng theo hàng/cột
- Tìm đúng vật phẩm theo danh sách mua hàng
- **Đẩy xe hàng** đến đúng gian hàng
- Thanh toán, nhận lại tiền thừa

### Đề xuất bài học mới (Scene mới)

| Chủ đề | Kỹ năng dạy |
|--------|-------------|
| **Băng qua đường** | An toàn giao thông, nhìn trái-phải, chờ đèn xanh, đi vạch |
| Tương tác với bạn | Kỹ năng xã hội |
| Tự mặc quần áo | Tự phục vụ |
| Chờ/Đi xe buýt | Di chuyển công cộng |

### Kỹ năng cần bổ sung

| Kỹ năng | Mô tả | Gợi ý bài học |
|---------|-------|---------------|
| **Joint Attention** | Chú ý vào cùng vật thể với người khác | NPC chỉ vào đồ vật, trẻ cần nhìn theo |
| **School Environment** | Tương tác trong lớp học, hành lang | Giơ tay phát biểu, xếp hàng, chào thầy cô |
| **Băng qua đường** | An toàn giao thông | Nhìn trái-phải, chờ đèn xanh, đi trên vạch |
| **Mua sắm nâng cao** | Tìm đồ, so sánh giá, thanh toán | Theo list mua hàng, trả tiền, nhận lại tiền thừa |
| **Điều hòa cảm xúc** | Kiểm soát cảm xúc trong tình huống stress | Môi trường ồn ào, đông người, thay đổi bất ngờ |

### Câu hỏi cần làm rõ

- [ ] Có cần thêm các bài tập mang tính **tương tác hơn** không?
  - Hiện tại: Chủ yếu làm theo hướng dẫn
  - Đề xuất: Tình huống mở, nhiều lựa chọn, roleplay
- [ ] Cần nghiên cứu thêm với chuyên gia về thứ tự ưu tiên kỹ năng

---

## 📱 4. Tính năng hỗ trợ Remote Control

> Cho phép giáo viên điều khiển từ Web Dashboard



### Commands cần hỗ trợ

| Command | Mô tả |
|---------|-------|
| `trigger_hint` | Hiện gợi ý trực quan cho trẻ trong kính |
| `play_npc_audio` | NPC phát audio theo clip đã chọn |
| `play_npc_script` | NPC đọc câu thoại do giáo viên gõ real-time |
| `set_volume` | Điều chỉnh âm lượng tiếng ồn môi trường |
| `set_npc_voice_speed` | Điều chỉnh tốc độ giọng nói NPC |
| `set_object_speed` | Điều chỉnh tốc độ di chuyển vật thể trong scene |
| `pause_lesson` | Tạm dừng/tiếp tục bài học |
| `skip_quest` | Bỏ qua quest hiện tại |
| `set_camera_height` | Điều chỉnh chiều cao camera (override profile) |

---

