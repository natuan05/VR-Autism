# Bảng Kế Hoạch Triển Khai (VR App)
> **Dự án:** VR Autism (Phần Ứng dụng VR - Unity)
> **Mục tiêu:** Nâng cấp kiến trúc VR, tích hợp Firebase chuẩn xác và chuẩn bị sẵn UI/Logic để ghép nối với Web Dashboard.

Do Web Dashboard sẽ là một project tách biệt, tập tài liệu này sẽ **chỉ tập trung vào những việc cần làm trên Unity (VR)**, cách làm cụ thể, và vạch rõ những gì làm được ngay (song song) so với những gì cần đợi Web.

> ✅ **Task 1.1 & 1.2 đã hoàn thành.** Xem chi tiết kiến trúc hiện tại trong `WALKTHROUGH.md`.

---

## 🟢 GIAI ĐOẠN 1: CÒN LẠI

### Task 1.3: Tích hợp Behavior Snapshots (Telemetry tần số cao) - ✅ **ĐÃ HOÀN THÀNH**
* Tích hợp thành công hệ thống telemetry thu thập vị trí đầu và mắt của trẻ tần số cao lên RTDB. Chi tiết xem tại [TELEMETRY_GAZE_DESIGN.md](file:///d:/Lab/VR-Autism/docs/design/TELEMETRY_GAZE_DESIGN.md).

---

## 🟡 GIAI ĐOẠN 2: CHUẨN BỊ API & LẮNG NGHE (Chạy song song với Web)

Ở giai đoạn này, team VR chuẩn bị sẵn "ổ cắm" (Listeners & Connectors). Chúng ta không cần Web code xong màn hình hiển thị, mà chỉ cần thống nhất với team Web về **cấu trúc dữ liệu JSON**. Chúng ta có thể dùng tính năng giả lập của Firebase Console để test.

### Task 2.1: Cơ chế Sinh mã PIN (Pairing) - ✅ **ĐÃ HOÀN THÀNH**
* Chuyển đổi thành công sang mô hình **Thin Client (Dumb Terminal)**, kính VR hoàn toàn lệ thuộc vào luồng sự kiện từ Web.
* Hoàn tất cơ chế ghép đôi qua mã PIN, tự động chuyển đổi listener sang nhận diện bài học (`lesson_id`) để tải scene từ xa.
* Dọn dẹp triệt để các UI cũ không sử dụng, giải phóng bộ nhớ hệ thống.

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

### Task 3.2: Truyền Hình Ảnh (WebRTC / Video POV) - ✅ **ĐÃ HOÀN THÀNH**
* Triển khai hoàn tất luồng truyền hình ảnh thời gian thực chất lượng cao từ Unity sang Web Dashboard bằng WebRTC.
* Tách biệt camera phụ để tránh phá vỡ pipeline render VR (sử dụng camera phụ sao chép cấu hình render lên render texture).
* Quản lý lifecycle WebRTC sạch sẽ, thực hiện cơ chế Signaling (Offer/Answer/ICE) qua Firebase RTDB ổn định.

### Task 3.3: Auto-Alert (Cảnh báo & Can thiệp Vô hại) - ✅ **ĐÃ HOÀN THÀNH**
* Xây dựng pipeline dữ liệu thời gian thực từ VR hỗ trợ Web dashboard phân tích hành vi và gửi cảnh báo (Alert) cho giáo viên.
* Hỗ trợ kích hoạt các Soft-Interventions (như highlight vật thể) từ xa thông qua lệnh điều khiển.

---
