# 🌐 Ý tưởng phát triển Web Dashboard

> Xây dựng Web Dashboard để tạo hệ thống hoàn chỉnh với VR App

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

---

## 🎥 2. Real-time Monitoring & Co-located Setup

> **Hướng tiếp cận: Co-located (Cùng địa điểm)**. Hệ thống thiết kế để Trẻ và Chuyên gia ở chung một phòng. Điểm đặc biệt: Hình ảnh POV của trẻ sẽ bắn **trực tiếp lên Web Dashboard** thông qua mạng Wifi nội bộ (WebRTC P2P), không đi qua Internet để đảm bảo độ trễ = 0.

### Các tính năng

| Tính năng | Mô tả | Độ khó Code |
|-----------|-------|--------|
| 👁️ Live POV View | Tích hợp luồng Video trực tiếp vào Web Dashboard qua **WebRTC (Local LAN)**. Chuyên gia xem hình ảnh và bấm nút trên cùng một màn hình (giống hệt Floreo). | ⭐⭐⭐ |
| 🎮 Remote Control | Web Dashboard điều khiển NPC, vật thể qua Firebase RTDB | ⭐⭐ |
| 🔊 Environment Control | Tăng/giảm tiếng ồn, tốc độ vật thể, âm lượng (RTDB) | ⭐⭐ |
| 💬 NPC Hints | Trigger NPC nói gợi ý từ danh sách có sẵn (RTDB) | ⭐⭐ |
| 📊 Live Telemetry | Bảng tiến trình hiển thị dưới dạng Text/Progress Bar trên Web | ⭐⭐ |

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

> **Ghi chú kỹ thuật (Implementation Idea):** 
> Để video hiển thị thẳng trên Web Dashboard mà không vắt kiệt băng thông Internet, Unity VR App sẽ dùng thư viện WebRTC truyền hình ảnh Peer-to-Peer thẳng qua địa chỉ IP Local của cái iPad/Laptop đang mở Web. Trong khi đó, các Tín hiệu Điều khiển (Text/Command) nhẹ nhàng vẫn đi qua Firebase Realtime Database làm trung gian (Signaling Server) để đảm bảo tính ổn định.

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

## 🗄️ 5. Database Schema & Công nghệ

> 💡 **Chi tiết kiến trúc Database (Firestore & Realtime Database) đã được tách ra file riêng.**
> 
> 👉 **Xem chi tiết tại:** [DATABASE_SCHEMA_DESIGN.md](../design/DATABASE_SCHEMA_DESIGN.md)


## 🔌 6. Luồng Kết nối (Pairing) & Tài khoản

### 6.1 Cơ chế Role-Based (Dành cho Web)
- **Tài khoản Web:** Chỉ cấp cho Người lớn (Chuyên gia hoặc Phụ huynh). Đăng nhập bằng Email/Password/Google qua Firebase Auth.
- **Hồ sơ Trẻ (Child Profile):** Trẻ không có tài khoản. Hồ sơ của trẻ đóng vai trò là "Data Container" nằm gọn trong tài khoản của Người lớn (tương tự như chọn User Icon trên Netflix).

### 6.2 Cơ chế PIN-Pairing (Dành cho VR)
Áp dụng cách kết nối như Smart TV để nối Web Dashboard và Kính VR:

1. **Khởi động VR:** App hiển thị chữ to: `Mã kết nối của bạn là: 123456` và ở trạng thái **Đang đợi** (Waiting). Kính tự đẩy mã PIN lên nhánh `pairing_codes` ở Firebase.
2. **Khởi động Web Dashboard:** 
   - Chuyên gia chọn hồ sơ "Bé Nam", bấm "Bắt đầu bài học".
   - Popup web đòi: *"Nhập mã PIN hiển thị trong màn hình VR"*.
   - Chuyên gia/Phụ huynh gõ `123456`.
3. **Thành công:** Firebase ghép đôi `vr_device_id` với `session_id` đang mở của bé Nam. Kính tải ngay Setting của bé Nam và vào Scene tương ứng. Web bật màn hình Remote Control.

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

## ⚠️ 8. Rủi ro Kỹ thuật & Biện pháp Xử lý (Technical Risks & Mitigations)

Để đảm bảo hệ thống có thể scale lên hàng ngàn người dùng ở môi trường Clinic/Trường học mà không sập nguồn, dưới đây là các rủi ro hệ thống đã được nhận diện và xử lý triệt để trong cấu trúc hệ thống:

### 8.1. Rủi ro về Chi phí (Cloud Billing) 🚨
* **Vấn đề:** Trực tiếp ghi logs/telemetry 60s/lần từ VR lên Firestore.
* **Hậu quả:** Bùng nổ số lượt Write, chi phí tăng theo cấp số nhân khi có lượng hồ sơ lớn.
* **Cách fix:** Đẩy telemetry tốc độ cao vào Realtime DB (Không tính tiền theo Request). Kính VR hoặc Cloud Function gom lại thành 1 file JSON và ghi duy nhất 1 lần vào Firestore khi Buổi học (Session) kết thúc.

### 8.2. Rủi ro Mạng & Kết nối (Networking/P2P) 📡
* **Vấn đề:** 100% phụ thuộc mạng LAN cho WebRTC Stream. Không xử lý rớt mạng đột ngột.
* **Hậu quả:** Không stream được nếu cục Router ở trung tâm chặn P2P (AP Isolation). Nếu rớt mạng ngang, dữ liệu session bị đứt gãy.
* **Cách fix:** 
  1. Thêm **STUN/TURN Server** làm fallback nếu đàm phán LAN thất bại.
  2. Kính VR phải xử lý lưu cache offline (Local Save), chỉ gỡ màn hình "Paired" trên Web khi nhận sự kiện `onDisconnect()` đáng tin cậy từ Firebase.

### 8.3. Xung đột Trạng thái (Async/State Conflicts) ⚙️
* **Vấn đề:** Web Dashboard truyền lệnh "mù" quá nhanh; 2 thiết bị tranh quyền ghi nhận hoàn thành bài học khi bị lag mạng.
* **Hậu quả:** Phát sai âm thanh, lệnh thực thi trễ nhịp làm trẻ hoảng sợ (Meltdown). Các logs đè lên nhau.
* **Cách fix:**
  1. **Kính VR luôn là Nguồn Chân Lý (Source of Truth).** Kính có quyền tự động Ignore (Bỏ qua) các lệnh từ Web nếu State hiện tại trong Unity (VD: đã đi qua khu vực đó rồi) không còn khớp.
  2. Thêm **Debounce/Throttle limit** (0.5s/lần) trên giao diện nút bấm Web Dashboard để chặn tình trạng "Spam Click" từ người hướng dẫn.

### 8.4. Rủi ro Ghép nối (Pairing Security) 🔑
* **Vấn đề:** Dò trúng/Trùng mã PIN 6 số.
* **Hậu quả:** Vô tình ghép nối nhầm Session của một trẻ khác đang ngồi ở phòng khám bên cạnh (ở môi trường phòng khám/trung tâm lớn).
* **Cách fix:** Thiết kế mã PIN phức tạp hơn dạng Alphanumeric ngắn (Ví dụ: `A1X-B2Y`) thay vì chỉ là số thuần túy, và áp dụng cơ chế Rate Limit (chặn nhập sai API quá nhiều lần).

---
