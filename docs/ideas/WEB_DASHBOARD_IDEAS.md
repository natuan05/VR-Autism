# 🌐 Ý tưởng phát triển Web Dashboard

> Xây dựng Web Dashboard để tạo hệ thống hoàn chỉnh với VR App
> **Lần cập nhật cuối:** 2026-04-03

---

## 🎯 Tầm nhìn

```
┌─────────────────────────────────────────────────────────────────┐
│                    HỆ SINH THÁI HỖ TRỢ TRẺ TỰ KỶ               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│         👶 TRẺ                     👨⚕️ CHUYÊN GIA │ PHỤ HUYNH │
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
| 👨‍👩‍👧 Phụ huynh | Xem tiến trình con |
| 👩‍🏫 Giáo viên/ Chuyên gia | Giám sát real-time, can thiệp, phân tích data, điều chỉnh bài học, quản lý nhiều trẻ, báo cáo |

---

## 📊 1. Dashboard Features

### Cho Phụ huynh

| Tính năng | Mô tả |
|-----------|-------|
| 📊 Xem tiến trình | Biểu đồ đơn giản, dễ hiểu |
| 📅 Lịch sử học | Xem lại từng buổi |
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
| 📚 Quản lý bài học | Tạo/sửa đề Quiz, thêm câu hỏi mới mà không cần Build lại App |

---

## 📚 2. Lesson & Quiz Content Management (Mới)

> **Bối cảnh từ thiết kế VR App:** `LessonInfo` và `QuizConfig` hiện đang lưu trực tiếp trong máy dưới dạng ScriptableObject. Khi quy mô mở rộng, chúng cần được đưa lên Firestore để đạt **Single Source of Truth** — cùng một nguồn dữ liệu phục vụ cả Web Dashboard và VR App.

### 2.1 Vấn đề hiện tại (Local SO Path)

```
Hiện tại:
  Nội dung Quiz → lưu trong FarmQuizConfig.asset (trong máy)
  Thông tin bài học → lưu trong LessonInfo.asset (trong máy)
  
  → Giáo viên muốn đổi câu hỏi phải nhờ Developer → Build lại APK → cài lại kính
  → Mỗi tháng mất 1-2 ngày công chỉ để cập nhật nội dung
```

### 2.2 Kiến trúc mục tiêu (Cloud Content Path)

```json
// Firestore: lessons/{lesson_id}
{
  "lesson_id": "farm_quiz_01",
  "lesson_name": "Nhận biết Động vật Trang trại",
  "lesson_type": "theoretical",
  "scene_name": "FarmQuiz",
  "quiz_data": [
    {
      "question": "Đây là con vật gì?",
      "answers": ["Lợn", "Bò", "Chó", "Dê"],
      "correctAnswer": 0,
      "questionSoundKey": "Farm3",
      "animalSoundKey": "PigSound",
      "associatedObjectKey": "Pig"
    }
  ]
}
```

### 2.3 Lợi ích của Content Management trên Dashboard

| Hành động | Hiện tại | Sau khi có Dashboard |
|-----------|----------|---------------------|
| Đổi nội dung câu hỏi | Sửa code → Build lại → cài kính (2h) | Sửa trên Web → Lưu → Kính tự tải (30s) |
| Thêm bài học mới | Cần Developer | Giáo viên tự làm |
| Cập nhật 100 trường | 100 lần mở file | 1 lần nhấn "Import CSV" |

### 2.4 Luồng Fetch on Demand (VR App nhận nội dung từ Cloud)

```
Web Dashboard chọn bài → ghi lesson_id vào RTDB
        ↓
VR App nhận lesson_id qua Firebase listener
        ↓
VR App gọi Firestore.GetDocument("lessons/{lesson_id}")
        ↓
Parse JSON → đổ vào QuizQuestionData[] trong RAM
        ↓
QuestionCollection.LoadFromData(data) → Quiz bắt đầu
```

> **Lưu ý kỹ thuật:** `QuizQuestionData` trong VR App đã được thiết kế dạng Pure C# (không phụ thuộc Unity), sẵn sàng nhận JSON từ Firestore. Trường `questionSound` và `animalSound` sẽ cần chuyển sang `string` key khi implement path này.

---

## 🎥 3. Real-time Monitoring & Co-located Setup

> **Hướng tiếp cận: Co-located (Cùng địa điểm)**. Hệ thống thiết kế để Trẻ và Chuyên gia ở chung một phòng. Điểm đặc biệt: Hình ảnh POV của trẻ sẽ bắn **trực tiếp lên Web Dashboard** thông qua mạng Wifi nội bộ (WebRTC P2P), không đi qua Internet để đảm bảo độ trễ = 0.

### Các tính năng

| Tính năng | Mô tả | Độ khó Code |
|-----------|-------|--------|
| 👁️ Live POV View | Tích hợp luồng Video trực tiếp vào Web Dashboard qua **WebRTC (Local LAN)**. Chuyên gia xem hình ảnh và bấm nút trên cùng một màn hình (giống hệt Floreo). | ⭐⭐⭐ |
| 🎮 Remote Control | Web Dashboard điều khiển NPC, vật thể qua Firebase RTDB | ⭐⭐ |
| 🔊 Environment Control | Tăng/giảm tiếng ồn, tốc độ vật thể, âm lượng (RTDB) | ⭐⭐ |
| 💬 NPC Hints | Trigger NPC nói gợi ý từ danh sách có sẵn (RTDB) | ⭐⭐ |
| 📊 Live Telemetry | Bảng tiến trình hiển thị dưới dạng Text/Progress Bar trên Web | ⭐⭐ |
| 📊 Quest Log Stream | Hiển thị từng quest hoàn thành theo thời gian thực (response time, đúng/sai) | ⭐⭐ |

### Kiến trúc Luồng Dữ liệu (Realtime)

```text
┌─────────────────────────────────────────────────────────────────┐
│                 MẠNG WIFI NỘI BỘ (LOCAL NETWORK)                │
│                                                                 │
│   🥽 KÍNH VR (Trẻ)  ────────(WebRTC Video P2P)──► 💻 WEB DASHBOARD │
│   (Render Unity)                                  (Trình duyệt)   │
└────────┬─────────────────────────────────────────────────▲──────┘
         │ Signal & Text Data (RTDB)                       │
         ▼                                                 │
┌──────────────────┐                                       │
│ ⚡ CLOUD Firebase │ ──────────────────────────────────────┘
└──────────────────┘
```

> **Ghi chú kỹ thuật:** Unity VR App dùng thư viện WebRTC truyền hình ảnh Peer-to-Peer thẳng qua địa chỉ IP Local. Các Tín hiệu Điều khiển nhẹ vẫn đi qua Firebase Realtime Database làm Signaling Server để đảm bảo tính ổn định.

---

## 🛡️ 4. AI Safety Philosophy

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

## 🤖 5. AI Features (Hỗ trợ, không can thiệp)

| Feature | Mô tả | Công nghệ |
|---------|-------|-----------|
| 📊 Progress Analytics | Phân tích pattern học tập | LLM API |
| 💡 Recommendation | Đề xuất bài học phù hợp | Rule-based / ML |
| ⚠️ Anomaly Detection | Phát hiện regression | Statistics / API |
| 📝 NL Reports | Tạo báo cáo tự động | GPT/Gemini |
| 🔔 Smart Alerts | Cảnh báo khi cần chú ý | Rule-based |
| 🐢 Response Time Analysis | Biểu đồ tốc độ phản xạ của trẻ theo từng câu hỏi (Quest Log) | Rule-based |

> **Insight mới từ thiết kế Quest Log:** Dashboard có thể vẽ biểu đồ "Response Time" cho từng câu Quiz. Giáo viên có thể nhìn vào biểu đồ và nhận xét: *"Tại sao câu hỏi về Con Cừu, bé mất 12 giây mới chọn sai đáp án?"* → phát hiện điểm kẹt nhận thức cụ thể.

> Không cần tự build model - dùng API có sẵn!

---

## 🗄️ 6. Database Schema & Công nghệ

> 💡 **Chi tiết kiến trúc Database (Firestore & Realtime Database) đã được tách ra file riêng.**
>
> 👉 **Xem chi tiết tại:** [DATABASE_SCHEMA_DESIGN.md](../design/DATABASE_SCHEMA_DESIGN.md)

---

## 🔌 7. Luồng Kết nối (Pairing) & Tài khoản

### 7.1 Cơ chế Role-Based (Dành cho Web)
- **Tài khoản Web:** Chỉ cấp cho Người lớn (Chuyên gia hoặc Phụ huynh). Đăng nhập bằng Email/Password/Google qua Firebase Auth.
- **Hồ sơ Trẻ (Child Profile):** Trẻ không có tài khoản. Hồ sơ của trẻ đóng vai trò là "Data Container" nằm gọn trong tài khoản của Người lớn (tương tự như chọn User Icon trên Netflix).

### 7.2 Cơ chế PIN-Pairing (Dành cho VR)
Áp dụng cách kết nối như Smart TV để nối Web Dashboard và Kính VR:

1. **Khởi động VR:** App hiển thị chữ to: `Mã kết nối của bạn là: 123456` và ở trạng thái **Đang đợi** (Waiting). Kính tự đẩy mã PIN lên nhánh `pairing_codes` ở Firebase.
2. **Khởi động Web Dashboard:**
   - Chuyên gia chọn hồ sơ "Bé Nam", bấm "Bắt đầu bài học".
   - Popup web đòi: *"Nhập mã PIN hiển thị trong màn hình VR"*.
   - Chuyên gia/Phụ huynh gõ `123456`.
3. **Thành công:** Firebase ghép đôi `vr_device_id` với `session_id` đang mở của bé Nam. Kính tải ngay Setting của bé Nam và vào Scene tương ứng. Web bật màn hình Remote Control.

### 7.3 Lesson Selection Flow (Mới)
Sau khi Pairing thành công, giáo viên chọn bài học trên Web:

```
Web Dashboard: Chọn bài "Farm Quiz" cho Bé Nam
        ↓ Ghi lesson_id vào RTDB: sessions/{session_id}/pending_lesson
VR App: Listener nhận lesson_id
        ↓ Fetch QuizConfig từ Firestore
        ↓ Load Scene "FarmQuiz"
        ↓ Quiz bắt đầu với nội dung từ Cloud
```

---

## 📋 8. Các tính năng khác

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

## ⚠️ 9. Rủi ro Kỹ thuật & Biện pháp Xử lý

Để đảm bảo hệ thống có thể scale lên hàng ngàn người dùng ở môi trường Clinic/Trường học mà không sập nguồn, dưới đây là các rủi ro hệ thống đã được nhận diện và xử lý triệt để trong cấu trúc hệ thống:

### 9.1. Rủi ro về Chi phí (Cloud Billing) 🚨
* **Vấn đề:** Trực tiếp ghi logs/telemetry 60s/lần từ VR lên Firestore.
* **Hậu quả:** Bùng nổ số lượt Write, chi phí tăng theo cấp số nhân khi có lượng hồ sơ lớn.
* **Cách fix:** Đẩy telemetry tốc độ cao vào Realtime DB (Không tính tiền theo Request). Kính VR hoặc Cloud Function gom lại thành 1 file JSON và ghi duy nhất 1 lần vào Firestore khi Buổi học (Session) kết thúc.

### 9.2. Rủi ro Mạng & Kết nối (Networking/P2P) 📡
* **Vấn đề:** 100% phụ thuộc mạng LAN cho WebRTC Stream. Không xử lý rớt mạng đột ngột.
* **Hậu quả:** Không stream được nếu cục Router ở trung tâm chặn P2P (AP Isolation). Nếu rớt mạng ngang, dữ liệu session bị đứt gãy.
* **Cách fix:**
  1. Thêm **STUN/TURN Server** làm fallback nếu đàm phán LAN thất bại.
  2. Kính VR phải xử lý lưu cache offline (Local Save), chỉ gỡ màn hình "Paired" trên Web khi nhận sự kiện `onDisconnect()` đáng tin cậy từ Firebase.

### 9.3. Xung đột Trạng thái (Async/State Conflicts) ⚙️
* **Vấn đề:** Web Dashboard truyền lệnh "mù" quá nhanh; 2 thiết bị tranh quyền ghi nhận hoàn thành bài học khi bị lag mạng.
* **Hậu quả:** Phát sai âm thanh, lệnh thực thi trễ nhịp làm trẻ hoảng sợ (Meltdown). Các logs đè lên nhau.
* **Cách fix:**
  1. **Kính VR luôn là Nguồn Chân Lý (Source of Truth).** Kính có quyền tự động Ignore (Bỏ qua) các lệnh từ Web nếu State hiện tại trong Unity không còn khớp.
  2. Thêm **Debounce/Throttle limit** (0.5s/lần) trên giao diện nút bấm Web Dashboard để chặn tình trạng "Spam Click".

### 9.4. Rủi ro Ghép nối (Pairing Security) 🔑
* **Vấn đề:** Dò trúng/Trùng mã PIN 6 số.
* **Hậu quả:** Vô tình ghép nối nhầm Session của một trẻ khác đang ngồi ở phòng khám bên cạnh.
* **Cách fix:** Thiết kế mã PIN phức tạp hơn dạng Alphanumeric ngắn (Ví dụ: `A1X-B2Y`) thay vì chỉ là số thuần túy, và áp dụng cơ chế Rate Limit.

### 9.5. Rủi ro Đồng bộ Nội dung (Content Sync) 📋 (Mới)
* **Vấn đề:** VR App đang build với `QuizConfig` cứng trong máy; nếu Firestore trả về schema JSON khác (vd: thêm field mới), App cũ sẽ parse sai hoặc crash im lặng.
* **Hậu quả:** Câu hỏi quiz bị mất nội dung, hoặc âm thanh không phát.
* **Cách fix:**
  1. Thêm field `schema_version` vào mỗi document Firestore.
  2. VR App kiểm tra version trước khi parse: nếu version quá cũ → hiển thị thông báo "Vui lòng cập nhật App".
  3. Dùng `[JsonIgnore]` / default value cho các field tùy chọn để tránh crash khi field mới được thêm vào Firestore mà App chưa kịp update.

---
