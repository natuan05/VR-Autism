# Epic & Stories: VR-Autism

Tập hợp các Epic và Story phục vụ cho việc theo dõi tiến trình và triển khai các tính năng trên Unity VR Client.

---

## Epic 1: Telemetry & Gaze Tracking
Thu thập hành vi và thông số tương tác của trẻ trong kính VR tần số cao.

### Story 1.1: Gaze Tracking Design
Thiết kế và mô phỏng luồng dữ liệu theo dõi ánh mắt của trẻ trong không gian 3D.

### Story 1.2: Telemetry Structure
Định nghĩa cấu trúc dữ liệu JSON để gửi về Firebase RTDB tối ưu.

### Story 1.3: High-Frequency Behavior Snapshots
Triển khai hệ thống telemetry tần số cao gửi tọa độ đầu/mắt mỗi 2 giây lên RTDB.

---

## Epic 2: Connection & Pairing
Thiết lập cơ chế kết nối giữa kính VR (Thin Client) và Web Dashboard cùng hệ thống cấu hình cá nhân hóa.

### Story 2.1: PIN Pairing Thin Client
Triển khai cơ chế ghép đôi bằng mã PIN kết nối và chuyển kính VR hoàn toàn sang mô hình nhận sự kiện điều khiển.

### Story 2.2: Customizable Lesson Core
Nâng cấp và cải tiến hệ thống kịch bản bài học cũ (ActionManager, AnimalLessonManager, v.v.) để hỗ trợ ghi đè cấu hình động (tốc độ di chuyển, trần âm lượng, thời gian gợi ý).

### Story 2.3: Lesson Parameter Syncing
Truy vấn cấu hình cá nhân hóa `default_lesson_params` từ profile của Trẻ trên Firestore sau khi `paired`, tự động áp dụng vào GameManager trước khi load Scene.

---

## Epic 3: Remote Control & Monitoring
Giám sát thời gian thực và cho phép giáo viên can thiệp hoặc điều khiển bài học của trẻ từ xa.

### Story 3.1: Controllable Lesson Event Bridge
Nâng cấp hệ thống kịch bản bài học cũ để nhận tín hiệu điều khiển ngoài (Hint, Skip, Pause).

### Story 3.2: WebRTC POV Stream
Triển khai truyền hình ảnh thời gian thực chất lượng cao POV từ kính VR sang Web qua kết nối WebRTC P2P LAN.

### Story 3.3: Auto-Alert Interventions
Triển khai cảnh báo tự động thông qua telemetry hành vi và hỗ trợ giáo viên kích hoạt các soft-interventions.

### Story 3.4: Remote Control Commands Listener
Xây dựng Listener đọc nhánh lệnh `live_sessions/{session_id}/commands` trên Firebase RTDB từ Web Dashboard và phát tín hiệu cho kính phản hồi tương ứng.

### Story 3.5: Web Dashboard Remote Control Interface
Xây dựng giao diện điều khiển trên Web Dashboard (Next.js) để chuyên gia gửi lệnh điều khiển thời gian thực (Hint, Skip, Volume, v.v.) xuống RTDB cho kính VR thực thi.

---

## Epic 4: Cross-Platform VR Hardware Compatibility
Đảm bảo hệ thống hoạt động ổn định trên nhiều nền tảng phần cứng VR khác nhau, cụ thể là hỗ trợ song song Meta Quest 2 và dòng kính HTC Vive (Vive Pro 2 - PCVR / Vive Focus 3 - Standalone).

### Story 4.1: OpenXR Interaction Mapping
Cấu hình và kiểm tra các Action Maps của Unity Input System thông qua OpenXR để đảm bảo tương thích tay cầm HTC Vive (Vive controllers) song song với Meta Quest Touch.

### Story 4.2: Hardware Detection & Visual Adjustments
Xây dựng lớp script tự động nhận diện phần cứng đang kết nối để tải đúng mô hình 3D của tay cầm và điều chỉnh Camera Offset (chiều cao, tầm nhìn) phù hợp cho từng loại kính.

### Story 4.3: HTC Vive Platform Deployment & Testing
Thiết lập cấu hình build pipeline cho HTC Vive (PCVR/Wave SDK) và thực hiện kiểm thử thực địa đảm bảo hiệu năng tối ưu trên 60FPS.
