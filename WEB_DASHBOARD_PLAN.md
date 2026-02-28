# 🌐 Kế hoạch phát triển Web Dashboard

> Xây dựng Web Dashboard để tạo hệ thống hoàn chỉnh với VR App

---

## 🎯 Tầm nhìn

```
┌─────────────────────────────────────────────────────────────────┐
│                    HỆ SINH THÁI HỖ TRỢ TRẺ TỰ KỶ               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│         👶 TRẺ                      👨‍👩‍👧 PHỤ HUYNH / 👨‍⚕️ CHUYÊN GIA  │
│           │                              │                       │
│           ▼                              ▼                       │
│      ┌────────┐                   ┌──────────┐                  │
│      │VR App  │                   │   Web    │                  │
│      │Oculus  │                   │Dashboard │                  │
│      └────┬───┘                   └────┬─────┘                  │
│           │                            │                        │
│           └────────────┬───────────────┘                        │
│                        ▼                                         │
│                  ┌──────────┐                                    │
│                  │  CLOUD   │                                    │
│                  │ Firebase │                                    │
│                  └──────────┘                                    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 👥 Đối tượng sử dụng

| Vai trò | Nhu cầu chính |
|---------|---------------|
| 👨‍👩‍👧 Phụ huynh | Xem tiến trình con, nhận gợi ý |
| 👨‍⚕️ Chuyên gia | Phân tích data, điều chỉnh bài học |
| 👩‍🏫 Giáo viên | Giám sát real-time, can thiệp |
| 🏥 Trung tâm | Quản lý nhiều trẻ, báo cáo tổng hợp |

---

## 📊 1. Dashboard Features

### Cho Phụ huynh

| Tính năng | Mô tả |
|-----------|-------|
| 📊 Xem tiến trình | Biểu đồ đơn giản, dễ hiểu |
| 📅 Lịch sử học | Xem lại từng buổi |
| 💬 Nhận gợi ý | AI đề xuất bài tập phù hợp |
| 📩 Liên hệ chuyên gia | Gửi tin nhắn/đặt lịch |
| 🏆 Thành tích | Xem huy chương, điểm |

### Cho Chuyên gia

| Tính năng | Mô tả |
|-----------|-------|
| 👥 Quản lý bệnh nhân | Danh sách, lọc, sắp xếp |
| 📈 Phân tích chi tiết | Biểu đồ chuyên sâu, so sánh |
| ✏️ Ghi chú | Lưu nhận xét mỗi buổi |
| ⚙️ Điều chỉnh | Thay đổi độ khó, thời gian |
| 📋 Báo cáo | Xuất PDF cho hồ sơ bệnh án |
| 🔔 Cảnh báo | Thông báo khi có bất thường |

---

## 🎥 2. Real-time Monitoring

> Cho phép giáo viên giám sát và can thiệp trực tiếp

### Các tính năng

| Tính năng | Mô tả | Độ khó |
|-----------|-------|--------|
| 👁️ Live View | Video 480p, 15-30fps | ⭐⭐⭐ |
| 🎮 Remote Control | Điều khiển NPC, vật thể | ⭐⭐ |
| 🔊 Environment | Tăng/giảm tiếng ồn, ánh sáng | ⭐⭐ |
| 💬 NPC Hints | Trigger NPC nói gợi ý | ⭐⭐⭐ |

### Kiến trúc

```
┌─────────────────────────────────────────────────────────────────┐
│  WEB DASHBOARD                                                   │
│  ┌─────────────────┐  ┌─────────────────┐                       │
│  │ 📺 Live View    │  │ 🎮 Controls     │                       │
│  │ (480p stream)   │  │ - NPC commands  │                       │
│  │                 │  │ - Volume        │                       │
│  └────────▲────────┘  └────────┬────────┘                       │
│           │ WebRTC             │ Firebase                       │
└───────────┼────────────────────┼────────────────────────────────┘
            │                    │
┌───────────┼────────────────────┼────────────────────────────────┐
│  VR APP   ▼                    ▼                                 │
│  ┌─────────────────┐  ┌─────────────────┐                       │
│  │ Camera Stream   │  │ Command Handler │                       │
│  │ (Unity Render   │  │ - Execute cmds  │                       │
│  │  Streaming)     │  │ - Update scene  │                       │
│  └─────────────────┘  └─────────────────┘                       │
└─────────────────────────────────────────────────────────────────┘
```

### Thứ tự triển khai

1. Remote Commands qua Firebase (1-2 tuần)
2. Environment Control (1 tuần)
3. NPC Hints - pre-recorded (1-2 tuần)
4. Live Video Stream 480p (2-3 tuần)

---

## 🛡️ 3. AI Safety Philosophy

> **Nguyên tắc: AI hỗ trợ Giáo viên, KHÔNG thay thế Giáo viên**

### Phân loại AI theo mức độ rủi ro

| Mức độ | AI làm gì? | Áp dụng? |
|--------|------------|----------|
| 🟢 Phân tích | Xử lý data, tạo báo cáo | ✅ Có |
| 🟢 Gợi ý | Đề xuất cho giáo viên | ✅ Có |
| 🟡 Cảnh báo | Alert khi bất thường | ✅ Có |
| 🔴 Can thiệp | Tự động tương tác với trẻ | ❌ Không |

### Mô hình Human-in-the-Loop

```
AI Backend                          Giáo viên (Quyết định)
──────────                          ─────────────────────
📊 Phân tích data           →       👁️ Xem dashboard
💡 Đề xuất hành động        →       👆 Chấp nhận/Từ chối
⚠️ Cảnh báo bất thường      →       🎮 Can thiệp thủ công
📝 Tạo báo cáo              →       ✏️ Review & chỉnh sửa

         AI KHÔNG BAO GIỜ trực tiếp tương tác với trẻ
```

---

## 🤖 4. AI Features (Hỗ trợ, không can thiệp)

| Feature | Mô tả | Công nghệ |
|---------|-------|-----------|
| 📊 Progress Analytics | Phân tích pattern học tập | LLM API |
| 💡 Recommendation | Đề xuất bài học phù hợp | Rule-based / ML |
| ⚠️ Anomaly Detection | Phát hiện regression | Statistics / API |
| 📝 NL Reports | Tạo báo cáo tự động | GPT/Gemini |
| 🔔 Smart Alerts | Cảnh báo khi cần chú ý | Rule-based |

> Không cần tự build model - dùng API có sẵn!

---

## 🗄️ 5. Database Schema

### Cấu trúc đề xuất

```
users/
  ├── {user_id}/
  │   ├── profile (tên, tuổi, chẩn đoán...)
  │   ├── settings (cấu hình cá nhân)
  │   └── linked_experts (danh sách chuyên gia)

sessions/
  ├── {session_id}/
  │   ├── user_id
  │   ├── lesson_data
  │   ├── quest_list
  │   ├── sensor_data (eye, head, proxemics)
  │   └── behavior_logs

experts/
  ├── {expert_id}/
  │   └── patient_list

commands/
  ├── {session_id}/
  │   └── pending_commands (cho remote control)
```

---

## 🔐 6. Authentication & Authorization

| Tính năng | Mô tả |
|-----------|-------|
| 🔑 Đăng nhập | Parent / Expert / Admin roles |
| 🔗 Liên kết | Trẻ ↔ Phụ huynh ↔ Chuyên gia |
| 🔒 Quyền xem | Ai được xem data của ai |
| 📧 Xác thực | Email verification |

**Công nghệ**: Firebase Auth (đã có sẵn)

---

## 📋 7. Các tính năng khác

### Onboarding
- [ ] Đánh giá ban đầu (baseline)
- [ ] Thu thập thông tin trẻ
- [ ] Đề xuất bài học đầu tiên

### Thông báo
- [ ] Push notification
- [ ] Email báo cáo hàng tuần
- [ ] Cảnh báo regression

### Reporting
- [ ] Xuất PDF
- [ ] Tích hợp hệ thống y tế (tùy chọn)

### Privacy & Compliance
- [ ] COPPA compliance (dữ liệu trẻ em)
- [ ] Mã hóa dữ liệu nhạy cảm
- [ ] Chính sách lưu trữ/xóa

---

## 🛠️ 8. Công nghệ đề xuất

| Thành phần | Công nghệ | Lý do |
|------------|-----------|-------|
| Frontend | React / Next.js | Phổ biến, dễ tuyển dev |
| Charts | Chart.js / Recharts | Biểu đồ đẹp |
| Backend | Firebase Functions | Đã có Firebase |
| Auth | Firebase Auth | Tích hợp sẵn |
| Database | Firebase Realtime DB | Đã có sẵn |
| Video Stream | Unity Render Streaming | Free, WebRTC |
| Hosting | Firebase Hosting | Miễn phí |
| AI | OpenAI / Gemini API | Không cần tự build |

---

## 📅 Roadmap

| Phase | Công việc | Thời gian |
|-------|-----------|-----------|
| **0** | User Research - Phỏng vấn chuyên gia | 1-2 tuần |
| **1** | Thiết kế UI/UX mockups | 1 tuần |
| **2** | Setup project + Firebase Auth | 1 tuần |
| **3** | Dashboard MVP (xem data) | 2-3 tuần |
| **4** | Remote Commands | 1-2 tuần |
| **5** | Live Video Stream | 2-3 tuần |
| **6** | AI Features (reports, alerts) | 2-3 tuần |
| **7** | Advanced features | Ongoing |

---

## 🔗 Files liên quan

- `VR_APP_PLAN.md` - Kế hoạch phát triển VR App (hướng 1)
- `REFACTOR_PLAN.md` - Kế hoạch tổ chức lại code VR
- `FUTURE_IDEAS.md` - Các ý tưởng chưa triển khai
