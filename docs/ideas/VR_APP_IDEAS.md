# 📱 Ý tưởng phát triển VR App

---

## 🎯 Mục tiêu
01/01/2026
Nâng cấp VR App hiện tại để:
- Thu thập dữ liệu phong phú hơn
- Hỗ trợ cá nhân hóa cho từng trẻ
- Bổ sung thêm bài học mới
- Tối ưu hóa trải nghiệm

---

## 📊 1. Nâng cấp hệ thống Data Collection

Dựa trên thiết kế Schema mới nhất (`DATABASE_SCHEMA_DESIGN.md`), hệ thống thu thập dữ liệu tự động của VR được chia làm 2 cấp độ:

### 1.1 Khối lượng nhẹ: Quest Logs (Ghi nhận sau mỗi Quest)
*Dữ liệu tĩnh (Static Data), ghi nhận khi trẻ hoàn thành một bước nhỏ (Quest).*

| Metric | Mô tả (Thu thập trong Unity) |
|--------|------------------------------|
| `response_time` | Thời gian từ lúc nhận Quest đến lúc kích hoạt đúng vật thể |
| `completion_status` | Trạng thái: `success` (tự làm), `assisted` (có gợi ý), `skipped` |
| `hints_verbal` | Số lần hệ thống (hoặc chuyên gia) nhấn nút phát âm thanh gợi ý |
| `hints_visual` | Số lần hệ thống làm sáng vật thể đích (Highlight) |
| `hints_physical` | (Ghi nhận bằng tay từ Web) Số lần chuyên gia cầm tay trẻ chỉ việc |

### 1.2 Khối lượng nặng: Behavior Snapshots (Chụp mỗi 2 giây)
*Luồng Telemetry siêu tốc, bắn qua Realtime Database để Web AI phân tích.*
Nằm ở folder Design

---

## ⚙️ 2. Hệ thống Settings & Cá nhân hóa

> ➡️ **Toàn bộ cấu trúc về Hệ thống Hồ sơ, Thông số độ khó cá nhân hóa (Parameters) và Phân loại Bài học (Metadata) đã được di chuyển sang file `WEB_DASHBOARD_IDEAS.md` (Phần 1 và 2) do Web Dashboard nắm quyền quản lý độc quyền.**

---

## 📚 3. Bổ sung bài học mới

### Các Scene hiện có

| Scene | Chủ đề | Trạng thái |
|-------|--------|------------|
| Bathroom | Rửa tay | ✅ Hoàn thành |
| Farm | Trang trại | ✅ Hoàn thành |
| Grassland | Đồng cỏ | ✅ Hoàn thành |
| Ocean | Động vật biển | ✅ Hoàn thành |
| Hellofriend + LearnToAsk | Giao tiếp | Có lỗi |

### Nâng cấp Scene hiện có

#### 🛒 Supermarket – Nâng cấp "Mua đồ siêu thị"

> Scene đã tồn tại nhưng cần bổ sung luồng tương tác phức tạp hơn.

- Nhìn tổng quan các kệ hàng theo hàng/cột
- Tìm đúng vật phẩm theo danh sách mua hàng
- **Đẩy xe hàng** đến đúng gian hàng
- Thanh toán, nhận lại tiền thừa

### Đề xuất bài học mới (Scene mới)

| Chủ đề | Kỹ năng dạy |
|--------|-------------|
| **Băng qua đường** | An toàn giao thông, nhìn trái-phải, chờ đèn xanh, đi vạch |
| **Đi siêu thị** | Mua sắm |

### Kỹ năng cần bổ sung

| Kỹ năng | Mô tả | Gợi ý bài học |
|---------|-------|---------------|
| **Joint Attention** | Chú ý vào cùng vật thể với người khác | NPC chỉ vào đồ vật, trẻ cần nhìn theo |
| **School Environment** | Tương tác trong lớp học, hành lang | Giơ tay phát biểu, xếp hàng, chào thầy cô |
| **Băng qua đường** | An toàn giao thông | Nhìn trái-phải, chờ đèn xanh, đi trên vạch |
| **Mua sắm nâng cao** | Tìm đồ, so sánh giá, thanh toán | Theo list mua hàng, trả tiền, nhận lại tiền thừa |
| **Điều hòa cảm xúc** | Kiểm soát cảm xúc trong tình huống stress | Môi trường ồn ào, đông người, thay đổi bất ngờ |

### Câu hỏi cần làm rõ

- [ ] Có cần thêm các bài tập mang tính **tương tác hơn** không?
  - Hiện tại: Chủ yếu làm theo hướng dẫn
  - Đề xuất: Tình huống mở, nhiều lựa chọn, roleplay
- [ ] Cần nghiên cứu thêm với chuyên gia về thứ tự ưu tiên kỹ năng

---

## 📱 4. Tính năng hỗ trợ Remote Control

> ➡️ **Toàn bộ danh sách Command Line (Điều khiển môi trường, Âm thanh, Bỏ qua Quest) đã được quy hoạch sang `WEB_DASHBOARD_IDEAS.md` (Phần 4. Remote Control). Về phía VR, chúng ta chỉ cần viết Handler để đọc các lệnh này từ Firebase nhánh RemoteCommand.**
