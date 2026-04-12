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

#### Task A1: Thiết lập cấu trúc Models cho Pairing - ✅ **ĐÃ HOÀN THÀNH**
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Tạo class `PairingData` (pin, device_id, status, child_profile_id).
- **INPUT:** App khởi động.
- **OUTPUT:** Các Class C# có thể serialize thành JSON chuẩn Firebase.
- **VERIFY:** Code không báo lỗi syntax.

#### Task A2: Khởi tạo module `RealtimeDBManager` - ✅ **ĐÃ HOÀN THÀNH**
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Khai báo Reference tới `FirebaseDatabase.DefaultInstance.RootReference`.
  - Viết method `GenerateAndPushPIN()`: ngẫu nhiên 6 số, bắn node `/pairing_codes/{PIN}` với status="waiting".
- **INPUT:** Nút bấm "Kết nối" trên giao diện sảnh chờ (Lobby).
- **OUTPUT:** Nhánh dữ liệu xuất hiện trên Firebase Console.
- **VERIFY:** Check Firebase RTDB Console có thấy `{PIN}: { status: "waiting", device_id: "..." }`.

#### Task A3: Lắng nghe trạng thái Paired (Và Nạp Scene) - ✅ **ĐÃ HOÀN THÀNH**
- **Agent:** `backend-specialist`
- **Cần code gì:** 
  - Dùng `ValueChanged` event listener của Firebase SDK trên node `/pairing_codes/{PIN}/status`.
  - Khi status = `"paired"`, lấy `child_profile_id` trả về tĩnh.
- **INPUT:** Giả lập đổi `status="paired"` thủ công bằng tay trên Firebase Console.
- **OUTPUT:** Log Unity Editor in ra `"Paired success! Child ID = XYZ"`.
- **VERIFY:** Debug event chạy chính xác mà không bị duplicate.

#### Task A4: Migrate `LessonInfo` to Firestore - ✅ **ĐÃ HOÀN THÀNH**
- **Agent:** `backend-specialist` / `frontend-developer`
- **Cần làm gì:** 
  - Tạo collection `lessons` trên Firestore thay thế cho ScriptableObject cứng trong app VR.
  - Trên Web: Đọc danh sách `lessons` từ Firestore để hiển thị giao diện "Chọn bài học" thay vì hardcode.
  - Khi Web gửi lệnh, sẽ dùng ID thực từ Firestore gắn vào `current_lesson_id`.
  - Trên VR: (Tuỳ chọn) Tải meta-data của lesson từ Firestore nếu cần thiết cho UI/logic.
- **INPUT:** Firestore database.
- **OUTPUT:** Giao diện web hiển thị bài học thật, có ID lưu trên Cloud.
- **VERIFY:** Nút "Start Lesson" truyền chính xác ID của Object trên Firestore vào RTDB.

#### Task A5: Tải Cấu hình Bài học cá nhân (Task cũ 2.2)
- **Agent:** `backend-specialist`
- **Cần làm gì:** 
  - Đọc Firestore: `child_profiles/{child_profile_id}` để lấy `default_lesson_params` (Volume, Hints delay).
  - Đẩy setting này đè lên `GameManager` hoặc cấu hình mặc định của bài học trước khi Load Scene.
- **INPUT:** `child_profile_id` và `lesson_id` thực từ Task A4.
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

### PHẦN C: Giao tiếp & Ổn định Live Session (Handshake & Heartbeat)
*Lý do làm tiếp theo: Cần đảm bảo Web biết VR đã thực sự vào bài (Handshake) và đang sống sót (Heartbeat) để tránh lỗi "Bóng ma phiên học" (Orphaned Sessions) trước khi bắn dữ liệu phức tạp.*

#### Task C1: VR Handshake (Xác nhận nạp Scene thành công)
- **Agent:** `backend-specialist` / `VR-specialist`
- **Cần làm gì:** 
  - Tại VR, khi load xong Scene bài học và bắt đầu `QuestController`, cập nhật node `LIVE_SESSIONS/{session_id}/vr_state` với giá trị `current_scene`.
  - Trên Web, lắng nghe node `vr_state` này để chuyển UI từ trạng thái "Đang chờ tải..." sang giao diện Điều khiển chính thức.
- **INPUT:** VR Scene hoàn tất quá trình Load.
- **OUTPUT:** RTDB nhận flag báo hiệu, Web mở giao diện Live Dashboard.

#### Task C2: Web Watchdog (Theo dõi nhịp sống VR)
- **Agent:** `frontend-developer`
- **Cần làm gì:** 
  - Đọc `BEHAVIOR_SNAPSHOTS` hoặc một biến `last_ping` nhỏ định kỳ từ VR.
  - Trên Web Dashboard, nếu quá 10 giây không có cục Telemetry/Ping mới xuất hiện $\rightarrow$ Hiển thị cảnh báo "Mất kết nối với kính" lên màn hình chuyên gia.
- **INPUT:** Dữ liệu streaming Telemetry.
- **OUTPUT:** UI Cảnh báo mạng bị rớt giữa bé và hệ thống.

---

## 3. Checklist Hoàn thành (Phase X)
- [x] Tính năng Random mã PIN (A1, A2) - *Hoàn thành chuẩn MVC Event-Driven*.
- [x] Tính năng Lắng nghe trạng thái (A3) - *Hoàn thành mô hình Chained Listener chờ Lesson ID*.
- [x] Chuyển đổi LessonInfo thành Collection Firestore (A4) - *Hoàn thành tích hợp DB và khử toàn bộ UI tĩnh.*
- [ ] Triển khai VR Handshake xác nhận 2 chiều (C1) - **(Next Step)**
- [ ] Thiết lập Watchdog cảnh báo mất kết nối bề mặt Web (C2) - **(Next Step)**
- [ ] Nạp thông số User setting cá nhân hoá (A5).
- [ ] Gói thư viện Sensor (B1, B2).
- [ ] Bắn Telemetry mỗi 2s mà không làm giật lag game (B3).
- [ ] Security DB Rules đã phủ cho cả RTDB nhánh `pairing_codes` và `behavior_snapshots`.
