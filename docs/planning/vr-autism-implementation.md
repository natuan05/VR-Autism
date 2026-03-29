# Bảng Kế Hoạch Triển Khai (VR App)
> **Dự án:** VR Autism (Phần Ứng dụng VR - Unity)
> **Mục tiêu:** Nâng cấp kiến trúc VR, tích hợp Firebase chuẩn xác và chuẩn bị sẵn UI/Logic để ghép nối với Web Dashboard.

Do Web Dashboard sẽ là một project tách biệt, tập tài liệu này sẽ **chỉ tập trung vào những việc cần làm trên Unity (VR)**, cách làm cụ thể, và vạch rõ những gì làm được ngay (song song) so với những gì cần đợi Web.

> ✅ **Task 1.1 & 1.2 đã hoàn thành.** Xem chi tiết kiến trúc hiện tại trong `WALKTHROUGH.md`.

---

## 🟢 GIAI ĐOẠN 1: CÒN LẠI

### Task 1.3: Nâng cấp Hệ thống Thu thập Dữ liệu (Telemetry)
* **Cần làm gì:** Ghi nhận thêm Gaze (hướng mắt), Head Rotation (xoay đầu) thay vì chỉ lưu Response Time và Hint Count.
* **Làm như thế nào:** 
  - Gắn script `XR HMD` hoặc dùng SDK của Oculus để track vị trí/góc xoay của đầu người chơi mỗi `x` giây. 
  - Lưu vào 1 list tạm trong RAM, đến cuối Session serialize thành file JSON và bắn lên Firestore.

---

## 🟡 GIAI ĐOẠN 2: CHUẨN BỊ API & LẮNG NGHE (Chạy song song với Web)

Ở giai đoạn này, team VR chuẩn bị sẵn "ổ cắm" (Listeners & Connectors). Chúng ta không cần Web code xong màn hình hiển thị, mà chỉ cần thống nhất với team Web về **cấu trúc dữ liệu JSON**. Chúng ta có thể dùng tính năng giả lập của Firebase Console để test.

### Task 2.1: Cơ chế Sinh mã PIN (Pairing)
* **Cần làm gì:** VR in ra mã PIN 6 số trên màn hình để đợi Web nhập.
* **Làm như thế nào:** 
  - Tại Scene khởi chạy, tạo một UI hiển thị "Mã kết nối: XYZ123".
  - Code C#: random 6 ký tự -> push lên Realtime DB (`pairing_codes/XYZ123 = { device_id: "QUEST_...", status: "waiting" }`).
  - Lắng nghe event thay đổi giá trị tại node đó. Khi `status` chuyển thành `"paired"`, tự động Load Scene tiếp theo.
  - **Cách test không cần Web:** Mở Firebase Console trên trình duyệt, sửa tay biến `"waiting"` thành `"paired"` xem kính VR có nhảy scene không.

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

---

## 📓 TÓM TẮT TIẾN ĐỘ & PHÂN CÔNG (ROADMAP SUMMARY)

| Giai đoạn | Tính năng (Unity VR focus) | Mức phụ thuộc Web | Nơi kiểm thử |
|---|---|:---:|---|
| **Phase 1** | Refactor Code, Tách Model, Namespace | 0% | Unity Editor Console |
| **Phase 1** | Viết Logs Session sau khi chơi lên Firestore | 0% | Firestore Console |
| **Phase 2** | Random PIN, đẩy lên nhánh `pairing_codes` | Nhẹ (Chỉ cần chung Schema) | Sửa thủ công trên Firebase RTDB |
| **Phase 2** | Pre-load Params settings trước khi vào Map | Nhẹ (Chỉ cần chung Schema) | Sửa thủ công trên Firestore |
| **Phase 3** | Cổng lắng nghe Remote Command (Hint, Pause) | 50% (Web cần gọi đúng lệnh) | Bấm nút trên Web Dashboard |
| **Phase 3** | Truyền Video Stream (WebRTC) | 100% (Blocker lớn) | Cả Web và VR phải chạy LAN |
