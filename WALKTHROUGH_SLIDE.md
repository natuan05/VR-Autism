# Luồng hoạt động chính của ứng dụng VR-Autism

---

## Bước 1: Khởi động bài học

Khi người dùng chọn một bài học từ menu, hệ thống sẽ tải Scene tương ứng. Các thành phần quản lý được khởi tạo, bộ đếm thời gian bắt đầu chạy, và dữ liệu ban đầu được gửi lên Firebase.

---

## Bước 2: Phát video hướng dẫn

NPC giáo viên xuất hiện và hướng dẫn trẻ thông qua video Timeline. Trẻ quan sát và làm quen với môi trường. Hệ thống chờ video hoàn thành trước khi chuyển sang bước tiếp theo.

---

## Bước 3: Thực hành các nhiệm vụ

Đây là phần chính của bài học. Hệ thống hiển thị từng nhiệm vụ một. Khi trẻ hoàn thành, hệ thống ghi nhận thời gian phản hồi và số lần gợi ý, sau đó chuyển sang nhiệm vụ tiếp theo.

**Ví dụ bài Rửa tay:**
1. Mở vòi nước
2. Lấy xà phòng
3. Xoa tay 20 giây
4. Rửa sạch xà phòng
5. Tắt vòi nước
6. Lau khô tay

Nếu trẻ không thực hiện sau một khoảng thời gian, hệ thống sẽ phát audio nhắc nhở. Số lần nhắc được ghi nhận để đánh giá mức độ cần hỗ trợ.

---

## Bước 4: Kết thúc và lưu kết quả

Khi hoàn thành tất cả nhiệm vụ, hệ thống hiển thị màn hình chúc mừng, dừng bộ đếm thời gian, tính tổng thời gian bài học, và gửi dữ liệu lên Firebase.
