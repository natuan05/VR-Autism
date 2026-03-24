# 🚀 Lộ trình Phát triển Hệ sinh thái VR Autism

Bản kế hoạch này mô tả lộ trình từng bước (Phase-by-Phase) để đưa dự án VR Autism từ trạng thái hiện tại (một ứng dụng VR độc lập) trở thành một **Hệ sinh thái Giáo dục Đặc biệt** hoàn chỉnh, bao gồm nâng cấp ứng dụng VR và xây dựng Web Dashboard cho chuyên gia/phụ huynh.

---

## 🗺️ TỔNG QUAN LỘ TRÌNH (ROADMAP)

Dự án sẽ được chia làm 4 giai đoạn chính để đảm bảo tính ổn định, dễ test và tích hợp liên tục:

1. **Phase 1: Vững chắc nền tảng (Foundation & Refactoring)** - Chuẩn hóa code hiện tại để chuẩn bị kết nối hệ thống mới.
2. **Phase 2: Xây dựng Xương sống Dữ liệu (Cloud Architecture)** - Thiết lập Firebase Architecture mới (Firestore + Realtime DB).
3. **Phase 3: Ứng dụng Quản lý (Web Dashboard MVP)** - Xây dựng bản MVP đầu tiên của Web Dashboard để quản lý User, Profile và Session.
4. **Phase 4: Kết nối Real-time & Can thiệp (Live Interaction)** - Triển khai cơ chế Pairing (PIN) và Remote Control giữa Web và VR.

---

## 🛠️ CHI TIẾT TỪNG GIAI ĐOẠN

### 🟢 PHASE 1: VỮNG CHẮC NỀN TẢNG (App VR)
*Mục tiêu: Dọn dẹp nợ kỹ thuật (technical debt), chuẩn hóa kiến trúc để dễ mở rộng.*

#### 1.1 Hoàn tất Code Refactoring
*Thực hiện triệt để các bước trong `CODE_REFACTOR_PLAN.md` hiện tại.*
- [x] **Dọn dẹp:** Xóa toàn bộ dead code (`TimeManager`, `FirebaseManager`, v.v.).
- [x] **Chuẩn hóa kiến trúc:** Sửa lại 3 loại Singleton về một chuẩn duy nhất, an toàn (Duplicate guard).
- [x] **Quy hoạch Namespace:** Áp dụng `VRAutism.Core`, `VRAutism.Cloud`, `VRAutism.Player` cho toàn project.
- [ ] **Bảo mật & Clean Code:** Đưa các đường dẫn Firebase ra file Const (`FirebasePaths.cs`) *(Tạm hoãn chờ test build kính VR)*, đóng gói property (Encapsulation) trong `QuestController`.

#### 1.2 Tái cấu trúc Model Dữ liệu (Local)
- [x] Tách biệt hoàn toàn các lớp Data Models (`LessonTimeData`, `QuestTimeData`, `SkillsData`) sang thư mục riêng.
- [ ] Chuẩn bị các model trống (Dumb models) cho các dữ liệu mới mở rộng ở Phase 2 (Profile, Session Params).

> 🎯 **VỊ TRÍ HIỆN TẠI:** Đang tạm ngưng Refactoring để Build và Test ứng dụng trực tiếp trên kính VR (hoàn tất kiểm thử Phase 1 trước khi sang Phase 2).

---

### 🟡 PHASE 2: XÂY DỰNG XƯƠNG SỐNG DỮ LIỆU (Firebase Cloud)
*Mục tiêu: Chuyển đổi từ cấu trúc lưu trữ phẳng, tạm bợ hiện tại sang mô hình dữ liệu chuẩn y tế/giáo dục.*

#### 2.1 Thiết lập Firebase Hybrid Database
- [ ] **Firestore:** Cấu hình Collection chính thức: `users`, `child_profiles`, `lessons`, `sessions`.
- [ ] **Realtime Database:** Thiết lập các Node ngắn hạn: `pairing_codes`, `live_sessions` (tự động xóa sau 1-2h).

#### 2.2 Cập nhật FirebaseManager (Unity)
*Nâng cấp khả năng đồng bộ của VR App với cấu trúc mới.*
- [ ] Chuyển đổi `FirebaseManager` từ việc dùng Realtime DB (hiện tại) sang **Firestore** để lưu trữ lịch sử (`sessions` & `quest_logs`).
- [ ] Giữ lại Realtime DB connection cho tính năng Control ở Phase 4.

#### 2.3 Cập nhật Logic Thu thập Dữ liệu (VR App)
- [ ] Thêm logic thu thập data nâng cao: *Gaze target* (nhìn vào đâu), *Head rotation* (nếu thiết bị vạch ra), thay vì chỉ đếm Hint và Time.
- [ ] Tạo module `BehaviorLogger.cs` để ghi nhận các hành vi đặc thù (nếu phát hiện hoặc do giáo viên trigger sau này).

---

### 🟠 PHASE 3: ỨNG DỤNG QUẢN LÝ (Web Dashboard MVP)
*Mục tiêu: Đưa ứng dụng web lên hình hài, kết nối vào Firestore.*

#### 3.1 Khởi tạo Project Web
- [ ] Setup dự án React/Next.js hoặc Vue (tùy stack ưu tiên).
- [ ] Cài đặt TailwindCSS / Material-UI, xác định Theme Color (thân thiện y tế/giáo dục: Xanh nhạt, trắng).

#### 3.2 Hệ thống Xác thực (Auth) & Phân quyền
- [ ] Tích hợp **Firebase Auth** (Login/Register/Google Login).
- [ ] Xây dựng luồng phân quyền: `Role` (Expert / Parent). Expert có thể liên kết Child Profiles.

#### 3.3 Giao diện Quản lý (CRUD)
- [ ] **Trang Quản lý Hồ sơ trẻ (Child Profiles):** Tạo, sửa, thiết lập parameters (`sound_sensitivity`, `visual_cues` mặc định).
- [ ] **Trang Lịch sử (Sessions):** Lấy dữ liệu từ Firestore, hiển thị danh sách các buổi học đã qua.
- [ ] **Trang Báo cáo Chi tiết:** Show biểu đồ tiến độ (Duration, Response Time, Hint Count).

---

### 🔴 PHASE 4: KẾT NỐI REAL-TIME & CAN THIỆP (Live Interaction)
*Mục tiêu: Biến bộ đôi Web - VR thành một công cụ điều khiển và giám sát từ xa theo thời gian thực.*

#### 4.1 Cơ chế PIN Pairing (Web Browser ↔ VR)
- [ ] **Trên VR App:** Khi khởi động, sinh random mã PIN 6 số, push lên nhánh `pairing_codes` ở Realtime DB.
- [ ] **Trên Web Dashboard:** Tạo Popup yêu cầu nhập mã PIN. Khi khớp, map `device_id` với `live_sessions` đang khởi tạo.

#### 4.2 Tải Cấu hình (Per-Lesson Parameters)
- [ ] Khi ghép đôi thành công, VR App tự động tải `child_profiles` -> fetch `default_lesson_params`.
- [ ] Áp dụng các parameters này vào Game (Ví dụ: Giảm nhẹ Volume, đổi tốc độ di chuyển của Ô tô) **trước khi** Scene bắt đầu.

#### 4.3 Remote Control (Web -> VR)
- [ ] **Web Dashboard:** Xây dựng màn hình "Live Session" với các buttons điều khiển: Bơm gợi ý (Trigger Hint), Đọc text (TTS Audio), Pause Lesson.
- [ ] **VR App:** Xây dựng `RemoteCommandHandler` lắng nghe Realtime DB (`live_sessions/{id}/commands`).
- [ ] Cắm các sự kiện này vào hệ thống `EventChannel.cs` sẵn có để các GameObject tự động phản hồi (Ví dụ: NPC nói khi có Event PlayAudio).

---

## 🔮 CÁC BƯỚC MỞ RỘNG (TƯƠNG LAI XA)
*(Chỉ làm sau khi hoàn tất 4 block trên)*
- Tích hợp stream hình ảnh (WebRTC/Unity Render Streaming) từ Kính VR lên Web (Hiện tại khá nặng, cần xử lý cẩn thận).
- Tích hợp AI (API) để đọc `Session Logs` và tóm tắt thành Báo cáo tự nhiên (Natural Language Report) trên Web Dashboard.
- Tạo thêm Scene mới (Sang đường, Lớp học) áp dụng kiến trúc Parameter động từ đầu.
