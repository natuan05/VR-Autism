# Epic & Stories: VR-Autism

Tập hợp các Epic và Story phục vụ cho việc theo dõi tiến trình và triển khai các tính năng trên Unity VR Client và Web Dashboard.

---

## Epic 1: Telemetry & Gaze Tracking
Thu thập hành vi và thông số tương tác của trẻ trong kính VR tần số cao.

### Story 1.1: Gaze Tracking Design
Thiết kế và mô phỏng luồng dữ liệu theo dõi ánh mắt của trẻ trong không gian 3D.
*Status: done*

### Story 1.2: Telemetry Structure
Định nghĩa cấu trúc dữ liệu JSON để gửi về Firebase RTDB tối ưu.
*Status: done*

### Story 1.3: High-Frequency Behavior Snapshots
Triển khai hệ thống telemetry tần số cao gửi tọa độ đầu/mắt mỗi 2 giây lên RTDB.
*Status: done*

### Story 1.4: Dynamic Gaze Cone Calculation
Tính toán độ rộng hình nón góc nhìn động $\theta = 2 \cdot \arctan(R/d)$ clamp 5° - 15° để thu được Focus Ratio vật lý chính xác.
*Status: backlog*

---

## Epic 2: Connection & Pairing
Thiết lập cơ chế kết nối giữa kính VR (Thin Client) và Web Dashboard cùng hệ thống cấu hình cá nhân hóa.

### Story 2.1: PIN Pairing Thin Client
Triển khai cơ chế ghép đôi bằng mã PIN kết nối và chuyển kính VR hoàn toàn sang mô hình nhận sự kiện điều khiển.
*Status: done*

### Story 2.2: Customizable Lesson Core
Nâng cấp và cải tiến hệ thống kịch bản bài học cũ (ActionManager, AnimalLessonManager, v.v.) để hỗ trợ ghi đè cấu hình động (tốc độ di chuyển, trần âm lượng, thời gian gợi ý).
*Status: done*

### Story 2.3: Lesson Parameter Syncing
Truy vấn cấu hình cá nhân hóa `default_lesson_params` từ profile của Trẻ trên Firestore sau khi `paired`, tự động áp dụng vào GameManager trước khi load Scene.
*Status: done*

---

## Epic 3: Remote Control & Monitoring
Giám sát thời gian thực, cho phép giáo viên can thiệp hoặc điều khiển bài học của trẻ từ xa, và đo lường hiệu quả của gợi ý trị liệu.

### Story 3.1: Controllable Lesson Event Bridge
Nâng cấp hệ thống kịch bản bài học cũ để nhận tín hiệu điều khiển ngoài (Hint, Skip, Pause).
*Status: done*

### Story 3.2: WebRTC POV Stream
Triển khai truyền hình ảnh thời gian thực chất lượng cao POV từ kính VR sang Web qua kết nối WebRTC P2P LAN.
*Status: done*

### Story 3.3: Auto-Alert Interventions
Triển khai cảnh báo tự động thông qua telemetry hành vi và hỗ trợ giáo viên kích hoạt các soft-interventions.
*Status: done*

### Story 3.4: Remote Control Commands Listener
Xây dựng Listener đọc nhánh lệnh `live_sessions/{session_id}/commands` trên Firebase RTDB từ Web Dashboard và phát tín hiệu cho kính phản hồi tương ứng.
*Status: done*

### Story 3.5: Web Dashboard Remote Control Interface
Xây dựng giao diện điều khiển trên Web Dashboard (Next.js) để chuyên gia gửi lệnh điều khiển thời gian thực (Hint, Skip, Volume, v.v.) xuống RTDB cho kính VR thực thi.
*Status: done*

### Story 3.6: Dynamic NPC Speech & Verbal Hint Engine
Cho phép nhập kịch bản nói (NPC Script) trực tiếp trên Web Dashboard và truyền xuống kính VR thời gian thực qua lệnh `play_npc_script` để NPC phát loa tiếng nói.
*Status: done*

### Story 3.7: Automated Multi-Tiered Prompt Hierarchy
Xây dựng chu kỳ tự động nhắc nhở phân bậc (Bậc 1: phát loa lời nói nhắc nhở Verbal Hint, Bậc 2: nhấp nháy viền sáng Visual Hint của Quest sau khoảng lặng) và khắc phục lỗi loa NPC im lặng trong các bài Actions (Bathroom). Bổ sung chế độ Bật/Tắt Auto-Hint lưu trực tiếp vào profile của trẻ trên Firestore, đồng bộ xuống kính để bật/tắt toàn bộ kịch bản tự động này. **Đồng thời đo lường chỉ số "thời gian phản hồi kể từ gợi ý gần nhất" (đo từ thời điểm gợi ý cuối cùng được phát đến khi trẻ hoàn thành Quest, lưu vào quest logs trên Firestore để đánh giá hiệu quả gợi ý).**
*Status: backlog*

### Story 3.8: Google Cloud TTS Voice Customization
Cho phép cấu hình các tham số giọng nói của NPC (giọng nam/nữ, tốc độ đọc, cao độ, mã ngôn ngữ) trực tiếp từ Web Dashboard, lưu vào cấu hình của trẻ và đồng bộ xuống kính để áp dụng cho Google Cloud TTS thời gian thực.
*Status: backlog*

### Story 3.9: Quick Template Phrases by Lesson
Xây dựng thư viện các mẫu câu thoại nhanh chuẩn bị sẵn cho NPC phân loại theo từng bài học (ví dụ: Rửa tay, Đánh răng, Siêu thị) trên giao diện Web Dashboard giúp giáo viên kích hoạt nhanh hơn mà không cần gõ phím.
*Status: backlog*

---

## Epic 4: Cross-Platform VR Hardware Compatibility
Đảm bảo hệ thống hoạt động ổn định trên nhiều nền tảng phần cứng VR khác nhau, cụ thể là hỗ trợ song song Meta Quest 2 và dòng kính HTC Vive (Vive Pro 2 - PCVR / Vive Focus 3 - Standalone).

### Story 4.1: OpenXR Interaction Mapping
Cấu hình và kiểm tra các Action Maps của Unity Input System thông qua OpenXR để đảm bảo tương thích tay cầm HTC Vive (Vive controllers) song song với Meta Quest Touch.
*Status: review*

### Story 4.2: Hardware Detection & Visual Adjustments
Xây dựng lớp script tự động nhận diện phần cứng đang kết nối để tải đúng mô hình 3D của tay cầm và điều chỉnh Camera Offset (chiều cao, tầm nhìn) phù hợp cho từng loại kính.
*Status: ready-for-dev*

### Story 4.3: HTC Vive Platform Deployment & Testing
Thiết lập cấu hình build pipeline cho HTC Vive (PCVR/Wave SDK) và thực hiện kiểm thử thực địa đảm bảo hiệu năng tối ưu trên 60FPS.
*Status: backlog*

---

## Epic 5: Social Interaction & Communication Training
Khôi phục, thích ứng và mở rộng các bài tập kỹ năng xã hội, cử chỉ và tương tác giao tiếp dành cho trẻ tự kỷ.

### Story 5.1: LearnToAsk Action Rework
Tái cấu trúc bài học giao tiếp "LearnToAsk" thuộc dạng Actions từ dự án cũ để tương thích với GameManager mới và cơ chế Customizable Lesson Core (âm lượng, thời gian im lặng).
*Status: backlog*

### Story 5.2: Gestures & Eye Contact Integration
Lên đặc tả kỹ thuật và thiết kế khung logic tích hợp nhận diện cử chỉ (vẫy tay, high-five) và tương tác ánh mắt hai chiều (Mutual Gaze) trong các phân cảnh tương tác xã hội.
*Status: backlog*
