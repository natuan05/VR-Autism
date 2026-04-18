# 🔭 Thiết kế Hệ thống Telemetry: Gaze & Kinematics

**Ngày tạo:** 2026-04-18
**Context:** Thiết kế lại luồng thu thập dữ liệu SensorHarvester để đảm bảo tính khoa học Y khoa (Joint Attention, Hesitation, Kinematics).

---

## 1. Tóm tắt Đích đến (Understanding Summary)
- **Mục tiêu:** Nâng cấp SensorHarvester để đo Gaze (Tầm nhìn) và Hand Proximity (Độ chần chừ của tay) một cách chính xác mà không bị nhiễu bởi các vật thể dày đặc trong môi trường VR.
- **Tại sao:** Dữ liệu Y khoa cần tính chính xác cao để máy chủ (VRA-web AI) đánh giá triệu chứng Stimming, Freeze, Hesitation. Raycast thông thường là không đủ.
- **Ràng buộc:** Tính toán Gaze và Hand Proximity phải bị khóa chặt với **Vật thể của Quest Hiện Tại** (Context-Aware) để khử nhiễu.
- **Nằm ngoài phạm vi:** VR App tuyệt đối không tự tính toán thuật toán y khoa (Ví dụ: is_stimming) mà chỉ thực hiện đẩy "Raw Data" tinh khiết lên Web.

## 2. Giả định (Assumptions)
- `QuestController` hoặc các System Managers khác có quyền nắm giữ toàn bộ Transform của Vật Thể mục tiêu hiện tại.
- Kính VR có đủ năng lực tính toán phép `Vector3.Angle` và `Vector3.Distance` mà không ảnh hưởng tới hiệu năng FPS của người dùng.

## 3. Nhật ký Quyết định (Decision Log)

| Vấn đề | Quyết định chốt | Giải pháp từng Cân nhắc | Lý do chọn |
|---|---|---|---|
| **Lọc Target** | Context-Aware tracking | Global Raycast (cũ) vs Context-Aware | Môi trường VR quá nhiều đồ đạc, Raycast sẽ dính nhiễu. Tracking theo Target giúp loại bỏ hoàn toàn đồ vật ngoại cảnh. |
| **Gaze (Tầm nhìn)**| Hình nón **30 Độ** | Tia (Ray) vs Hình nón (Cone) | Căn cứ trên hành vi liếc mắt/nhìn ngoại vi của trẻ tự kỷ, một hình nón mở rộng 30 độ bao quát màn hình hiệu quả hơn tia Laser. |
| **Báo cáo Dữ liệu** | **Raw Data Streaming** | Xử lý tại VR biên (Edge computing) | Để thay đổi ngưỡng Y khoa mà không phải compile cài lại App VR. Web Dashboard sẽ chịu trách nhiệm phân tích. |
| **Kiến trúc Liên kết**| **Event-Driven (Action)** | Singleton Getter vs Event | Chia rẽ (Decouple) hoàn toàn Hệ thống Gameplay và Hệ thống Thu thập dữ liệu. Không gây lỗi rác memory. |


## 4. Đặc tả Thiết kế (Final Design)

### 4.1. Khâu Phát Sóng (Gameplay Layer)
- Nơi triển khai: `QuestController.cs` (Hoặc các class điều khiển Scene tương đương).
- Cập nhật: Thêm Action event `public static event Action<Transform> OnTargetObjectChanged;`. Event này được kích hoạt kèm theo Transform của vật thể mỗi khi một Task/Quest mới bắt đầu chờ trẻ tương tác.

### 4.2. Khâu Bắt Sóng (Telemetry Layer)
- Nơi triển khai: `SensorHarvester.cs`
- Cơ chế: Đăng ký lắng nghe event từ gameplay trong `OnEnable()`.
- Biến lưu trữ: Ghi nhớ vào `private Transform _currentQuestTarget;`.

### 4.3. Cơ chế Khai thác Dữ liệu Định kỳ (Mỗi 2 giây)
Khi `TelemetryStreamer` yêu cầu `TakeSnapshot()`:

1. **Gaze (Joint Attention):** 
   - Kiểm tra `Vector3.Angle` giữa Vector hướng mặt (`Camera.main.transform.forward`) và Vector nối từ Camera đến `_currentQuestTarget`. 
   - Nếu $\text{Góc} \le 30$ độ $\rightarrow$ Ghi nhận `focus_object` là tên của Target.
2. **Proximity (Hesitation - Rụt rè):** 
   - Tính khoảng cách $\text{Distance} < 30 \text{cm}$ (Ví dụ) giữa Hands và `_currentQuestTarget`.
   - Lập cờ `near_object = true`.
3. **Kinematics (Vận tốc, Góc đầu):**
   - Lấy Vector nguyên bản, không làm biến đổi logic y khoa ở bước này.

### 4.4 Cấu trúc Model mới
Đưa Model `BehaviorSnapshot` ra khỏi namespace `.Cloud` thành lập một cầu nối chung tại `VRAutism.Core.Models` để không vi phạm chiều phụ thuộc Module.
