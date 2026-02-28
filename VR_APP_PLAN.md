# 📱 Kế hoạch phát triển VR App

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

### Tình trạng hiện tại

| Data | Có chưa? | Ghi chú |
|------|----------|---------|
| response_time | ✅ | Thời gian phản hồi cơ bản |
| hint_count | ✅ | Đếm số gợi ý |
| duration | ✅ | Thời gian bài học |
| SkillsData | ⚠️ | Placeholder (luôn = 0) |

### Data cần thêm mới

#### 1.1 Dữ liệu cảm biến VR

| Loại | Metrics | Yêu cầu |
|------|---------|---------|
| **Eye-Tracking** | Fixation Duration, Gaze Targets, Joint Attention | Quest Pro hoặc SDK |
| **Head Tracking** | Rotation Speed, Scanning Pattern, Nod/Shake | Có sẵn, cần log |
| **Proxemics** | Virtual Distance, Personal Space Invasion | Tính trong Unity |

#### 1.2 Dữ liệu Hiệu suất

| Metric | Nâng cấp |
|--------|----------|
| Response Time | + Benchmark thời gian tối ưu |
| Accuracy | + Phân loại lỗi (hiểu vs xung động) |
| Completion | + Ghi chính xác bước bị gãy |

#### 1.3 Prompt Hierarchy (Hệ thống hỗ trợ mới)

| Mức | Loại | Điểm |
|-----|------|------|
| 0 | Độc lập (tự làm) | 10 |
| 1 | Nhắc bằng lời (Verbal) | 7 |
| 2 | Chỉ tay/Cử chỉ (Gestural) | 5 |
| 3 | Cầm tay chỉ việc (Physical) | 2 |

#### 1.4 Behavior Logs

| Behavior | Cách ghi nhận |
|----------|---------------|
| Stimming | Nút bấm cho giáo viên |
| Meltdown | Nút bấm cho giáo viên |
| Sao nhãng | Nút bấm hoặc auto-detect |

---

## ⚙️ 2. Hệ thống Settings & Cá nhân hóa

### Settings cần implement

| Setting | Mô tả | Default |
|---------|-------|---------|
| difficulty | Easy / Normal / Hard | Normal |
| reminder_interval | Thời gian giữa các nhắc nhở | 10s |
| max_hints | Số gợi ý tối đa | 3 |
| time_limit | Giới hạn thời gian | 0 (không giới hạn) |
| npc_voice_speed | Tốc độ nói của NPC | 1.0 |
| visual_cues | Hiện hướng dẫn trực quan | true |

### Hệ thống Difficulty

| Aspect | Easy | Normal | Hard |
|--------|------|--------|------|
| Thời gian chờ | 15s | 10s | 5s |
| Số bước | Ít | Bình thường | Nhiều |
| Hỗ trợ trực quan | Nhiều | Vừa | Ít |
| NPC nói | Chậm, rõ | Bình thường | Nhanh |

### Code changes cần làm

- [ ] Thêm `SettingsManager.cs` - load/save settings từ Firebase
- [ ] Sửa `Quest.cs` - đọc reminder_interval từ settings
- [ ] Sửa `NPCController.cs` - điều chỉnh tốc độ nói
- [ ] Thêm `DifficultyController.cs` - điều chỉnh nội dung theo mức độ
- [ ] Sync settings real-time từ Firebase

---

## 📚 3. Bổ sung bài học mới

### Các Scene hiện có

| Scene | Chủ đề | Trạng thái |
|-------|--------|------------|
| Bathroom | Rửa tay | ✅ Hoàn thành |
| Farm | Trang trại | ✅ Hoàn thành |
| Zoo | Sở thú | ✅ Hoàn thành |
| Supermarket | Siêu thị | ✅ Hoàn thành |
| Ocean | Biển | ✅ Hoàn thành |
| Classroom | Lớp học | ✅ Hoàn thành |

### Đề xuất bài học mới

| Chủ đề | Kỹ năng dạy | Độ ưu tiên |
|--------|-------------|------------|
| Đi khám bác sĩ | Kiểm soát lo lắng, tuân thủ | 🔴 Cao |
| Tương tác với bạn | Kỹ năng xã hội | 🔴 Cao |
| Tự mặc quần áo | Tự phục vụ | 🟡 TB |
| Đi xe buýt | Di chuyển công cộng | 🟡 TB |
| Đặt hàng ở quán | Giao tiếp thực tế | 🟢 Thấp |

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

## 🔧 4. Refactor cấu trúc code

### Vấn đề hiện tại

- Scripts phân tán: `Dajunctic/Scripts/`, `Dang/Scripts/`, `Scripts/`
- Tên folder theo người, không theo chức năng
- File rác chưa xóa

### Cấu trúc đề xuất

```
Assets/_Project/
├── Scripts/
│   ├── Core/
│   │   ├── Managers/       ← GameManager, TimeManager, ActionManager
│   │   ├── Events/         ← BooleanVariable, IntVariable
│   │   └── Utils/          ← TimeUtils, DataUtils
│   ├── Lesson/
│   │   └── Quest/          ← Quest, QuestController
│   ├── Player/             ← Player, PlayerCam
│   ├── NPC/                ← NPC, NPCController
│   └── UI/                 ← QuestProgressUI, ExitScene
├── Prefabs/
├── ScriptableObjects/
└── Scenes/
```

> ⚠️ Xem chi tiết tại `REFACTOR_PLAN.md`

---

## 📱 5. Tính năng hỗ trợ Remote Control

> Cho phép giáo viên điều khiển từ Web Dashboard

### VR App cần implement

- [ ] `CommandHandler.cs` - Nhận và xử lý lệnh từ Firebase
- [ ] `StreamManager.cs` - Gửi video stream (Unity Render Streaming)
- [ ] Real-time listener cho Firebase commands

### Commands cần hỗ trợ

| Command | Mô tả |
|---------|-------|
| `trigger_hint` | Hiện gợi ý cho trẻ |
| `play_npc_audio` | NPC phát audio |
| `set_volume` | Điều chỉnh âm lượng |
| `pause_lesson` | Tạm dừng bài học |
| `skip_quest` | Bỏ qua quest hiện tại |

---

## 📅 Roadmap

| Phase | Công việc | Thời gian |
|-------|-----------|-----------|
| 1 | Implement SkillsData thực sự | 1-2 tuần |
| 2 | Thêm Prompt Hierarchy | 1-2 tuần |
| 3 | Hệ thống Settings | 2 tuần |
| 4 | Behavior logging UI | 1 tuần |
| 5 | Remote Command handler | 2 tuần |
| 6 | Eye/Head tracking (nếu có hardware) | 3-4 tuần |
| 7 | Refactor code structure | 1-2 tuần |
| 8 | Bài học mới | Ongoing |

---

## 🔗 Files liên quan

- `REFACTOR_PLAN.md` - Chi tiết kế hoạch tổ chức lại folder
- `FUTURE_IDEAS.md` - Các ý tưởng chưa triển khai
- `WEB_DASHBOARD_PLAN.md` - Kế hoạch phát triển Web (hướng 2)
