# Kế hoạch triển khai Telemetry & Pairing (VR App)
> **Dự án:** VR Autism (Phase 2)
> **Mục tiêu:** Xây dựng hệ thống luồng dữ liệu thời gian thực (Behavior Snapshots), cơ chế kết nối đa thiết bị bằng mã PIN, và nạp cấu hình bài học cá nhân hóa.

## 1. Cấu trúc thư mục dự kiến (File Structure)

Các file C# mới dự kiến sẽ nằm trong không gian của `VRAutism.Cloud`, bổ sung cho `FirebaseManager.cs`:

```text
Assets/Project/Scripts/Cloud/
├── FirebaseManager.cs        (Cập nhật thêm hàm RealtimeDB)
├── RealtimeDBManager.cs      (Tạo mới: chuyên xử lý Realtime Database)
├── PairingController.cs      (Tạo mới: Quản lý sinh PIN & Lắng nghe)
└── Models/
    ├── BehaviorSnapshot.cs   (Tạo mới: Payload gửi cách 2s)
    └── PairingData.cs        (Tạo mới: Cấu trúc báo danh)
```

## 2. Kế hoạch từng phần (Task Breakdown)

---

### PHẦN A: Báo danh & Nạp cấu hình (Task 2.1 & 2.2 - Nên làm trước)
*Lý do làm trước: Phải "Paired" thành công thì mới sinh ra `session_id`, có `session_id` thì mới stream Telemetry được.*

#### Task A1: Thiết lập cấu trúc Models cho Pairing
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Tạo class `PairingData` (pin, device_id, status, child_profile_id).
- **INPUT:** App khởi động.
- **OUTPUT:** Các Class C# có thể serialize thành JSON chuẩn Firebase.
- **VERIFY:** Code không báo lỗi syntax.

#### Task A2: Khởi tạo module `RealtimeDBManager`
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Khai báo Reference tới `FirebaseDatabase.DefaultInstance.RootReference`.
  - Viết method `GenerateAndPushPIN()`: ngẫu nhiên 6 số, bắn node `/pairing_codes/{PIN}` với status="waiting".
- **INPUT:** Nút bấm "Kết nối" trên giao diện sảnh chờ (Lobby).
- **OUTPUT:** Nhánh dữ liệu xuất hiện trên Firebase Console.
- **VERIFY:** Check Firebase RTDB Console có thấy `{PIN}: { status: "waiting", device_id: "..." }`.

#### Task A3: Lắng nghe trạng thái Paired
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Dùng `ValueChanged` event listener của Firebase SDK trên node `/pairing_codes/{PIN}/status`.
  - Khi status = `"paired"`, lấy `child_profile_id` trả về tĩnh.
- **INPUT:** Giả lập đổi `status="paired"` thủ công bằng tay trên Firebase Console.
- **OUTPUT:** Log Unity Editor in ra `"Paired success! Child ID = XYZ"`.
- **VERIFY:** Debug event chạy chính xác mà không bị duplicate.

#### Task A4: Tải Cấu hình Bài học cá nhân (Task 2.2)
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Đọc Firestore: `child_profiles/{child_profile_id}` để lấy `default_lesson_params` (Volume, Hints delay).
  - Đẩy setting này đè lên `GameManager` hoặc `LessonManager` trước khi Load Scene bài học.
- **INPUT:** `child_profile_id` từ Task A3.
- **OUTPUT:** Unity nạp Setting rác thành Setting đích xác.
- **VERIFY:** Âm thanh, tốc độ hiện quest bị thay đổi đúng với child profile trên Firebase.

---

### PHẦN B: Behavior Snapshots (Task 1.3)
*Phần này nhúng sâu vào Scene bài học lúc trẻ đang chơi.*

#### Task B1: Schema của Snapshot
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Lập class `BehaviorSnapshot` chứa: `time_offset`, `head_rotation_y`, `hand_velocity`, `focus_object_name`.
- **INPUT:** Thiết kế theo `DATABASE_SCHEMA_DESIGN.md`.
- **OUTPUT:** File `BehaviorSnapshot.cs` chuẩn Serializable.
- **VERIFY:** Compiler pass.

#### Task B2: Data Harvester (Trích xuất Sensor)
- **Agent:** `mobile-developer` / `VR-specialist`
- **Cần code gì:** 
  - Viết `SensorHarvester.cs` nhúng vào CameraRig.
  - Tính toán vận tốc (tốc độ di chuyển controller), góc xoay (Euler y của Camera).
  - Raycast từ giữa mắt ra trước để lấy `focus_object_name` (vật thể trẻ đang nhìn chằm chằm).
- **INPUT:** Vị trí Transform thật của User.
- **OUTPUT:** Đóng gói ra object `BehaviorSnapshot`.
- **VERIFY:** `Debug.Log` in ra luồng số liệu đúng mỗi khi user xoay đầu (trong Editor).

#### Task B3: Pipeline bắn lên RTDB mỗi 2 giây
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Trong `RealtimeDBManager.cs`, viết 1 Coroutine: cứ `yield return new WaitForSeconds(2f)`, gọi `SensorHarvester` lấy dữ liệu.
  - `Push()` dữ liệu đó vào `behavior_snapshots/{session_id}/{timestamp}` dưói dạng Dictionary thay vì đè lên vị trí cũ.
- **INPUT:** Dữ liệu có từ B2.
- **OUTPUT:** Truyền dữ liệu siêu tốc bắn lên đám mây.
- **VERIFY:** Mở tab RTDB trên Console, thấy data trôi cuộn liên tục (streaming) khi game đang play.

---

## 3. Checklist Hoàn thành (Phase X)
- [ ] Tính năng Random mã PIN (A1, A2).
- [ ] Tính năng Lắng nghe trạng thái (A3).
- [ ] Nạp thông số User setting (A4).
- [ ] Gói thư viện Sensor (B1, B2).
- [ ] Bắn Telemetry mỗi 2s mà không làm giật lag game (B3).
- [ ] Security DB Rules đã phủ cho cả RTDB nhánh `pairing_codes` và `behavior_snapshots`.
