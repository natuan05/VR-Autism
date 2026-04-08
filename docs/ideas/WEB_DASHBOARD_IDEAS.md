# 🌐 Ý tưởng phát triển Web Dashboard For Expert

> Xây dựng Web Dashboard để phục vụ độc quyền cho các chuyên gia, giáo viên và bác sĩ trị liệu giám sát trẻ tự kỷ trong môi trường Co-located (Cùng phòng).
> **Lần cập nhật cuối:** 2026-04-04

---

## 🎯 Tầm nhìn & Vai trò của Chuyên gia

Hệ thống Web Dashboard (VRA-web) đóng vai trò là "Trung tâm chỉ huy" (Command Center). Trong khi trẻ hoàn toàn đắm chìm vào Kính VR, chuyên gia sẽ ngồi bên cạnh dùng màn hình Laptop/Tablet để:
1. Nạp và thay đổi bài học (Không cần Dev Build lại app).
2. Nhìn thấy điểm mù của trẻ theo thời gian thực.
3. Can thiệp thủ công vào môi trường VR khi trẻ hoảng loạn hoặc gặp khó.
4. Ghi nhận Data hành vi để xuất hồ sơ bệnh án.

---

## 👥 1. Quản lý Hồ sơ trẻ (Child Profile Management)

Hệ thống tài khoản sẽ đi theo hướng **Role-Based cho Người lớn**. Trẻ em không cần tài khoản, chúng được quản lý dưới dạng "Hồ sơ" bên trong tài khoản của chuyên gia.

### 1.1 Khởi tạo Hồ sơ Trẻ
Được quản lý và chỉ định bởi role **CENTER_MANAGER** trước khi gán cho chuyên gia.

| Thuộc tính | Mô tả | Nhập liệu trên Web |
|------------|-------|--------------------|
| `display_name` | Tên hiển thị của trẻ | Gõ Text |
| `age` & `height_cm` | Tuổi và Chiều cao | Dùng để Unity tự động lùi/nâng Camera VR đúng tầm mắt trẻ |
| `sound_sensitivity` | Mức nhạy cảm âm thanh (1-5) | Giới hạn trần âm lượng tối đa để tránh Meltdown |
| `anxiety_triggers` | Yếu tố gây hoảng sợ (Khối hộp đỏ, chó sủa...) | Note lưu ý cho giáo viên |
| `diagnosis_notes` | Ghi chú chẩn đoán lâm sàng | "ASD Level 1, kém Joint Attention..." |

### 1.2 Thiết lập Cá nhân hóa (Personal Settings / Alert Profile)
Thiết lập các thông số sẽ được lưu xuống Firestore làm mặc định mỗi khi Profile này học bất kỳ bài nào:
- Mức âm lượng tiếng ồn (Noise level) mặc định.
- Tần suất tạo hint báo hiệu: `reminder_interval` (VD: Đợi 10s mới gợi ý), `max_hints` (Giới hạn 3 lần).
- Alert profile: Ngưỡng cảnh báo xoay cổ, ngưỡng cảnh báo Stimming...
- Tốc độ di chuyển và tốc độ voice của NPC (`npc_voice_speed`, `object_move_speed`).
| 📈 Báo cáo Học tập | Xem biểu đồ lịch sử quá trình của từng trẻ. Xuất PDF thẳng ra bệnh án báo cáo y tế định kỳ. |

---

## 📚 2. Thư viện Bài học (Lesson Library)

Mặc dù tính năng tự động tạo Quiz (CMS) tạm thời bị hoãn lại, nhưng **Danh sách các bài học có sẵn** vẫn phải được đưa lên Firestore. Điều này giúp Web Dashboard hiển thị menu chọn bài học cực kỳ trực quan với đầy đủ Metadata để giáo viên phân tích và chọn bài phù hợp với hồ sơ trẻ.

| Thuộc tính (Metadata) | Mô tả | Ứng dụng trên Web |
|-----------------------|-------|--------------------|
| `age_range` | Độ tuổi phù hợp (VD: [5, 10]) | Dùng để Filter (Lọc) danh sách bài học |
| `duration_min` | Thời gian ước tính (VD: 15 phút) | Xem bài học có phù hợp với `attention_span_min` của trẻ không |
| `skills` | Kỹ năng rèn luyện (Giao tiếp, Tự phục vụ..) | Khớp với `diagnosis_notes` để ra quyết định |
| `level` | Độ khó 1–3 | Đánh giá lộ trình tăng dốc |

*Hoạt động:* Giáo viên đọc các thông số này trên Web ➔ Chốt bài Farm ➔ Bấm Start ➔ Truyền `lesson_id="Farm"` sang VR.

---

## 🎥 3. Co-located Live Monitoring (Giám sát Real-time)

| Tính năng cốt lõi | Mô tả |
|-------------------|-------------------|
| 👁️ Live POV View | **WebRTC P2P (Local LAN):** Truyền hình ảnh 30FPS trực tiếp từ Kính sang Web với độ trễ < 50ms, không tốn băng thông Internet. |
| 📊 Live Auto Alert | Nhận Behavior Snapshots từ VR và tính toán, gửi các cảnh báo ở bảng bên dưới |

---

### Auto Alerts (`auto_alerts` collection)
*Được tính toán tự động từ Behavior Snapshots do VR gửi lên.*

| Hành vi | Nguồn thu thập | Cơ sở Y khoa / Lâm sàng |
|----------|---------------|--------------|
| Freeze (Đứng hình) | Gia tốc tay/đầu = 0 | Sensory Overload / Stress response |
| Distraction (Xao nhãng) | Angle > 30 độ | Né tránh ánh nhìn (Gaze aversion) |
| Stimming (Tự kích thích) | Dao động gia tốc tay | Điều hòa thần kinh (Rhythmic movements) |

### Manual Behavior Logs (`behavior_logs` collection)
*Chuyên gia chủ động ghi nhận trên Web Dashboard trong lúc quan sát POV.*

| Hành vi | Cách nhập | Mục đích |
|----------|----------|----------|
| Meltdown / Cáu gắt | Bấm nút nhanh trên Web | Cảm quan trực tiếp không thể đo bằng máy |
| Phản ứng tích cực | Bấm nút nhanh trên Web | Khuyến khích hành vi tốt |
| Ghi chú văn bản tự do | Gõ Note thả vào stream | Đóng băng 1 khoảnh khắc kèm Text y khoa để lưu vào bệnh án |

---

## 🎮 4. Remote Control (Can thiệp Trực tiếp)

Công cụ để giáo viên đóng vai "Chúa tể môi trường" khi trẻ đang học. Tín hiệu này bắn qua Firebase RTDB từ Web xuống VR (Các lệnh điều khiển tức thời):

| Command Buttons trên Web | Hiệu ứng trên Kính VR |
|--------------------------|------------------------|
| `trigger_hint` (Gợi ý chủ động) | Sử dụng âm thanh phát từ NPC, hiển thị hình ảnh trực quan (Highlight, mũi tên, hình ảnh minh họa). Dùng khi trẻ bị bí. |
| `set_volume` (Môi trường) | Thanh trượt Tăng/Giảm tiếng ồn môi trường (tiếng quạt, xe cộ, chim hót). |
| `play_npc_script` (Thoại khẩn cấp) | Giáo viên gõ chữ trên web ➔ Text-to-Speech (hoặc audio có sẵn) phát thành tiếng nhân vật trong VR để dỗ dành/ra lệnh. |
| `skip_quest` (Bỏ qua) | Dùng khi câu hỏi quá khó ngăn trẻ làm tiếp. |
| `pause_lesson` (Fade Black) | Ấn nút che đen màn hình VR nếu trẻ trở nên bạo lực hoặc quá khích kích thích giác quan. *(Tính năng này chưa có trong kế hoạch hiện tại)*. |

---

## 🔑 5. Luồng Ghép Đôi Nhanh (Smart TV Pairing)

Thay vì bắt trẻ phải gõ bàn phím ảo trong kính VR (Rất cực), ta áp dụng luồng ghép mã PIN:

1. **VR App:** Khi khởi động, thả 1 mã PIN (VD: `7A39B`) lơ lửng giữa không trung và chờ. 
2. **Web Dashboard:** Giáo viên chọn Hồ sơ Bé Nam → Giao diện hỏi mã PIN để ghép (Cũng có thể ghép sau khi vào dashboard)
3. **Thao tác:** Sau khi đã pair, giáo viên có thể vào giao diện chọn bài học và start, kính VR sẽ nhận lệnh và bắt đầu bài học.

---

## 🤖 6. Triết lý AI cho Chuyên gia (Ai chưa nằm trong kế hoạch)

AI trên Web Dashboard hoạt động theo nguyên tắc **Human-in-the-Loop** (Phụ tá cho Giáo viên, không được phép điều khiển bài học của trẻ). 

- **Tự động hóa Báo Cáo:** AI đúc kết đống log raw (2000 dòng/buổi) thành đoạn văn xuôi dễ đọc cho giáo viên: *"Hôm nay Nam phản xạ với câu hỏi Hình khối chậm hơn bình thường 2 giây, nhưng khả năng chịu ồn đã tốt hơn 15%"*.
- **Cảnh báo Thông Minh:** Highlight đỏ những điểm dữ liệu thất thường trên biểu đồ Telemetry. (VD: "Nam đang ngoảnh đầu quá 120 độ liên tục trong 10 giây").

---

## ⚠️ 7. Rủi ro Kỹ thuật & Biện pháp Xử lý (Dành cho Dev & Architect)

Để đảm bảo hệ thống Web Dashboard có thể scale lên hàng ngàn bệnh nhân ở môi trường Clinic/Trường học mà không sập nguồn:

### 7.1. Rủi ro về Chi phí (Cloud Billing) 🚨
* **Vấn đề:** Trực tiếp ghi logs/telemetry 60s/lần từ VR lên Firestore.
* **Hậu quả:** Bùng nổ số lượt Write, chi phí tăng theo cấp số nhân.
* **Cách fix:** Đẩy telemetry tốc độ cao vào Realtime DB (Không tính tiền theo Request). Kính VR hoặc Cloud Function định kỳ gom lại thành 1 file JSON và ghi duy nhất 1 lần vào Firestore khi Buổi học kết thúc.

### 7.2. Rủi ro Mạng & Kết nối (Networking/P2P) 📡
* **Vấn đề:** 100% phụ thuộc mạng LAN cho WebRTC Stream.
* **Hậu quả:** Không stream được nếu cục Router ở trung tâm chặn P2P (AP Isolation). Dữ liệu bị đứt gãy nếu rớt mạng.
* **Cách fix:** Thêm **STUN/TURN Server** làm fallback. Kính VR phải xử lý lưu cache offline (Local Save).

### 7.3. Xung đột Trạng thái (Async/State Conflicts) ⚙️
* **Vấn đề:** Giáo viên spam click nút "Chuyển câu hỏi" trên Web khi mạng đang lag.
* **Hậu quả:** Phát sai âm thanh, lệnh thực thi dồn dập khiến trẻ hoảng sợ (Meltdown).
* **Cách fix:** Mọi phím bấm điều khiển trên Web phải có cơ chế **Debounce (0.5s)**. Đặc biệt, Kính VR là Nguồn Chân Lý (Source of Truth), tự động vô hiệu hóa các lệnh trễ nhịp tới từ Web.

### 7.4. Rủi ro Đồng bộ Nội dung (Content Sync) 📋
* **Vấn đề:** Form soạn câu hỏi trên Web thêm 1 trường dữ liệu mới (Ví dụ: `hintColor`), nhưng Kính VR chưa được cập nhật phiên bản mới.
* **Hậu quả:** App VR bị Crash im lặng khi Parse file JSON.
* **Cách fix:** Luôn gắn `schema_version` vào DB. VR App dùng `[JsonIgnore]` để lờ đi các field không tương thích thay vì báo lỗi.

---
