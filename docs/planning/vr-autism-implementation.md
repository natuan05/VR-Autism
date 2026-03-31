# Bảng Kế Hoạch Triển Khai (VR App)
> **Dự án:** VR Autism (Phần Ứng dụng VR - Unity)
> **Mục tiêu:** Nâng cấp kiến trúc VR, tích hợp Firebase chuẩn xác và chuẩn bị sẵn UI/Logic để ghép nối với Web Dashboard.

Do Web Dashboard sẽ là một project tách biệt, tập tài liệu này sẽ **chỉ tập trung vào những việc cần làm trên Unity (VR)**, cách làm cụ thể, và vạch rõ những gì làm được ngay (song song) so với những gì cần đợi Web.

> ✅ **Task 1.1 & 1.2 đã hoàn thành.** Xem chi tiết kiến trúc hiện tại trong `WALKTHROUGH.md`.

---

## 🟢 GIAI ĐOẠN 1: CÒN LẠI

### Task 1.3: Tích hợp Behavior Snapshots (Telemetry tần số cao)
* **Cần làm gì:** Đo lường Head/Hand Kinematics, Gaze Targets (theo `VR_APP_IDEAS.md`) để làm đầu vào cho Web AI phân tích.
* **Làm như thế nào:** 
  - Tạo script thu thập chỉ số từ `XR HMD` và `Oculus SDK`.
  - **KHÔNG** lưu dồn vào RAM hay Firestore. Thay vào đó, bắn (stream) trực tiếp lên Firebase Realtime Database (`behavior_snapshots/{session_id}/{timestamp}`) mỗi 2 giây.
  - Đây là luồng dữ liệu "chảy trôi" (ephemeral), giúp Web phát hiện ngay Auto-Alert (như Stimming, Freeze) theo thời gian thực.

---

## 🟡 GIAI ĐOẠN 2: CHUẨN BỊ API & LẮNG NGHE (Chạy song song với Web)

Ở giai đoạn này, team VR chuẩn bị sẵn "ổ cắm" (Listeners & Connectors). Chúng ta không cần Web code xong màn hình hiển thị, mà chỉ cần thống nhất với team Web về **cấu trúc dữ liệu JSON**. Chúng ta có thể dùng tính năng giả lập của Firebase Console để test.

### Task 2.1: Cơ chế Sinh mã PIN (Pairing) - ✅ **ĐÃ HOÀN THÀNH**
* **Kiến trúc:** Chuyển đổi thành công sang mô hình **Thin Client (Dumb Terminal)**. Kính VR không tự quyết định bài học mà lệ thuộc hoàn toàn vào luồng sự kiện từ Web.
* **Quy trình 2-bước (Chained Listeners):** 
  1. Sinh PIN ngẫu nhiên -> Push lên Firebase `pairing_codes/{PIN}` với `status: "waiting"`.
  2. Lắng nghe `status` chuyển thành `"paired"` (Web đã nhận diện).
  3. Tự động chuyển thang lắng nghe sang trường `lesson_id`. Ngay khi giáo viên bấm chọn bài trên Web, Firebase trigger Event cho Unity để `SceneMenuController.cs` tự động gọi lệnh `SceneManager.LoadScene()`.
* **Thành tựu dọn dẹp:** Đã xóa sạch rác kỹ thuật (Dead Code) bao gồm các module UI chọn bài và biểu đồ cục bộ (`LessonUI`, `ReportController`, v.v.). Giải phóng ~30% bộ nhớ UI cho kính VR.

### Task 2.2: Đồng bộ Cấu hình Bài học (Per-Lesson Params)
* **Cần làm gì:** Áp dụng setting (âm lượng, độ nhạy âm thanh, số lần hint tối đa) theo hồ sơ của Trẻ trước khi vào game.
* **Làm như thế nào:** Khi đã `paired`, lấy ID của Trẻ -> query Firestore lấy `default_lesson_params`. Ghi đè vào các biến của `GameManager` trước khi Scene được active.
* **Cách test:** Tự điền 1 child_profile vào Firestore Console rồi xem VR có nhận đúng tốc độ/âm lượng không.

---

## 🟠 GIAI ĐOẠN 3: TÍCH HỢP QUYÊN QUYẾT (Cần ghép nối với Web)

Đây là lúc tính năng Control từ xa được đưa vào mạch. 

### Task 3.1: Remote Control Listener (Điều khiển từ xa)
* **Phụ thuộc:** Cần Web thiết kế xong UI các nút bấm (Trigger Hint, Change Volume) và push đúng cấu trúc lệnh xuống RTDB.
* **Cần làm gì:** Unity đọc lệnh từ chuyên gia và phản hồi trong kính thời gian thực.
* **Làm như thế nào:** 
  - Lắng nghe sự kiện thêm mới tại nhánh `live_sessions/{session_id}/commands`.
  - Switch/case các lệnh (`verbal_hint`, `visual_cue`, `pause`) và gọi vào `EventChannel.cs` để các đối tượng 3D trong Unity react tương ứng.

### Task 3.2: Truyền Hình Ảnh (WebRTC / Video POV) - *Tính năng nâng cao*
* **Phụ thuộc:** 100% đợi team Web thiết lập xong kênh WebRTC Signaling.
* **Cần làm gì:** Đẩy khung hình Camera của Unity lên Web.
* **Làm như thế nào:** Dùng Unity Render Streaming (hoặc WebRTC package). Yêu cầu bắt tay P2P giữa 2 thiết bị trong cùng mạng LAN.

### Task 3.3: Auto-Assist (Cảnh báo & Can thiệp Vô hại)
* **Phụ thuộc:** Web phân tích xong BehaviorSnapshot và tự động bắn lệnh thay vì con người.
* **Cần làm gì / Lộ trình:** 
  - **Hiện tại (Giám sát):** VR chỉ gửi sensor data, Web hiện Cảnh báo (Alert) để Giáo viên tự ra quyết định bấm nút can thiệp.
  - **Tương lai (Co-pilot):** Web tự động bắt tín hiệu (vd: trẻ mất tập trung >10s) và bắn ngược lại các lệnh Can thiệp vô hại (Soft-Interventions như `highlight_object`), hoạt động như Trợ lý số sát cánh cùng chuyên gia.

---

## 📓 TÓM TẮT TIẾN ĐỘ & PHÂN CÔNG (ROADMAP SUMMARY)

| Giai đoạn | Tính năng (Unity VR focus) | Mức phụ thuộc Web | Nơi kiểm thử |
|---|---|:---:|---|
| **Phase 1** | Refactor Code, Tách Model, Namespace | 0% | ✅ Done |
| **Phase 1** | Viết Logs Session sau khi chơi lên Firestore | 0% | Firestore Console |
| **Phase 2** | Nâng cấp Kiến trúc Thin Client & Xóa UI chọn bài cục bộ | 0% | ✅ Done |
| **Phase 2** | Cơ chế Ghép nối 2 bước (PIN -> Paired -> Lesson) | Nhẹ (Chung Schema) | ✅ Done |
| **Phase 2** | Pre-load Params settings trước khi vào Map (Task 2.2) | Nhẹ (Chung Schema) | Sửa thủ công trên Firestore |
| **Phase 3** | Cổng lắng nghe Remote Command (Hint, Pause) | 50% (Web cần gọi đúng lệnh) | Bấm nút trên Web Dashboard |
| **Phase 3** | Truyền Video Stream (WebRTC) | 100% (Blocker lớn) | Cả Web và VR phải chạy LAN |
| **Phase 3** | Lắng nghe lệnh Auto-Assist | 100% (Web tự động tính & bắn lệnh) | Nhận lệnh Soft-Intervention |
