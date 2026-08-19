# PRODUCT_CONTEXT.md — Nghiệp Vụ & Bối Cảnh Sản Phẩm VR-Autism

> **Mô tả & Loại thông tin cung cấp**: Tài liệu cung cấp bối cảnh nghiệp vụ, đặc thù tâm lý trị liệu cho trẻ tự kỷ (ASD), các luồng hành trình người dùng (User Journeys) và từ điển thuật ngữ chuẩn (Ubiquitous Language) của hệ thống VR-Autism.

## 1. Mục tiêu Dự án & Đối tượng Phục vụ
- **Mục tiêu**: Nền tảng trị liệu thực tế ảo kết hợp giám sát Web hỗ trợ trẻ em tự kỷ (ASD) rèn luyện kỹ năng giao tiếp và tự phục vụ trong môi trường an toàn, có thể kiểm soát quá tải giác quan.
- **Đối tượng**: Trẻ em mắc ASD (4–12 tuổi), Giáo viên can thiệp / Trị liệu viên, và Phụ huynh / Quản trị viên trung tâm.

## 2. Các Luồng Hành trình Người dùng (User Journeys)
- **Ghép nối thiết bị (PIN Pairing)**: Kính VR tạo mã PIN 6 số -> Giáo viên nhập PIN trên Web Dashboard để kích hoạt phiên trị liệu.
- **Truyền phát trực tiếp (POV Streaming)**: Góc nhìn của trẻ truyền theo thời gian thực về máy tính giáo viên qua LiveKit Video Track (720p@30fps).
- **Tương tác Nhiệm vụ & Hội thoại NPC**: Trẻ tương tác với đồ vật ảo và trò chuyện với nhân vật ảo (NPC). AI Agent đánh giá giọng nói và phản hồi.
- **Can thiệp từ xa (Remote Intervention)**: Giáo viên gửi lệnh hỗ trợ (Verbal Hint, Visual Cue, tạm dừng) khi trẻ gặp khó khăn.
- **Ghi nhận & Đánh giá**: Tự động đo lường độ tập trung, thời gian phản hồi và lưu trữ biểu đồ tiến bộ vào Firebase Firestore.

## 3. Từ Điển Thuật Ngữ Chuẩn (Ubiquitous Language)
- **Quest**: Nhiệm vụ đơn lẻ trong bài học (gồm VoiceQuest, TouchQuest, HoldTouchQuest, VisualQuest).
- **Verbal Hint**: Lời thoại gợi ý từ NPC khi trẻ im lặng hoặc do giáo viên kích hoạt.
- **Visual Cue**: Hiệu ứng thị giác (viền sáng, mũi tên) chỉ dẫn mục tiêu trong kính VR.
- **Sensory Overload (Quá tải giác quan)**: Trạng thái căng thẳng do âm thanh/hình ảnh; hệ thống hỗ trợ giới hạn âm lượng và tốc độ di chuyển để phòng tránh.
- **Echolalia (Nhại lời)**: Hiện tượng trẻ lặp lại lời NPC mà không hiểu nghĩa; Voice Agent xử lý bằng cách hướng dẫn lại nhẹ nhàng, không coi là lỗi thất bại.
- **POV Stream**: Luồng video trực tiếp góc nhìn của trẻ truyền về màn hình giám sát của giáo viên.
