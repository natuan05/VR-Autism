# 🔧 Kế Hoạch Tái Cấu Trúc RealtimeDBManager

**Ngày lập:** 2026-04-18  
**Trạng thái:** Đã phê duyệt thiết kế, chờ triển khai  
**Tài liệu tham chiếu:**
- `docs/ideas/WEB_DASHBOARD_IDEAS.md` — Remote Control, Live Monitoring, Auto Alerts
- `docs/ideas/VR_APP_IDEAS.md` — Behavior Snapshots nâng cấp, Gaze & Proximity
- `docs/planning/telemetry-and-pairing.md` — Task Breakdown gốc (Phase 2)
- `docs/design/TELEMETRY_GAZE_DESIGN.md` — Thiết kế Gaze Cone 30° & Context-Aware Tracking

---

## 1. Bối cảnh & Lý do Tách

### 1.1 Hiện trạng
`RealtimeDBManager.cs` hiện 375 dòng, đang gánh **4 trách nhiệm** trong cùng một file:

| # | Trách nhiệm | Dòng code | Phương thức chính |
|---|---|---|---|
| 1 | **Pairing** (Ghép nối PIN) | ~160 dòng | `GenerateAndPushPIN()`, `ResumeListening()`, `HandlePinNodeChanged()` |
| 2 | **Handshake** (Bắt tay Session) | ~30 dòng | `SendLiveSessionHandshake()` |
| 3 | **Telemetry** (Bắn Snapshot) | ~20 dòng | `PushBehaviorSnapshot()` |
| 4 | **Live Session State** | ~50 dòng | `UpdateCurrentActivity()`, `SendLiveSessionEnded()` |

### 1.2 Dự báo tăng trưởng (Dựa trên Roadmap)

Các tính năng sắp tới sẽ làm file phình lên **~700+ dòng**:

| Tính năng sắp tới | Nguồn tham chiếu | Ước tính code mới | Module bị ảnh hưởng |
|---|---|---|---|
| **Remote Control** (5 loại lệnh: trigger_hint, set_volume, play_npc_script, skip_quest, pause_lesson) | `WEB_DASHBOARD_IDEAS.md` §4 | +150–200 dòng | Cần listener RTDB hoàn toàn mới trên nhánh `remote_commands/` |
| **Watchdog / Heartbeat** (Task C2) | `telemetry-and-pairing.md` §C2 | +40–60 dòng | Ghi `last_ping` định kỳ, dọn dẹp orphaned sessions |
| **Telemetry nâng cấp** (Gaze Cone 30°, Proximity, thêm fields) | `TELEMETRY_GAZE_DESIGN.md` | +30–50 dòng | `PushBehaviorSnapshot` payload phức tạp hơn |
| **Nạp User Settings cá nhân hóa** (Task A5) | `telemetry-and-pairing.md` §A5 | +50–80 dòng | Đọc Firestore child_profiles rồi apply |
| **Manual Behavior Logs** (Bấm nút nhanh từ Web) | `WEB_DASHBOARD_IDEAS.md` §3 | +30–40 dòng | Lắng nghe thêm nhánh `behavior_logs/` |

**Tổng dự kiến: 375 + ~350 = ~725 dòng** → Vi phạm ngưỡng cho phép (~500 dòng/file).

### 1.3 Kết luận
Việc tách bây giờ — khi file còn đủ nhỏ để đọc hiểu — sẽ dễ dàng hơn rất nhiều so với việc tách khi nó đã vượt 700 dòng và đầy rẫy side-effects.

---

## 2. Kiến Trúc Mới

### 2.1 Sơ đồ cấu trúc thư mục

```text
Scripts/Cloud/
├── FirebaseManager.cs              (GIỮ NGUYÊN — Firestore, Session lifecycle)
├── FirebasePaths.cs                (GIỮ NGUYÊN — Constants/URLs)
├── SessionSyncTracker.cs           (GIỮ NGUYÊN — Event bridge Quest/Quiz → Cloud)
│
├── RTDB/                           (THƯ MỤC MỚI)
│   ├── RTDBConnection.cs           (MỚI — Singleton gốc, sở hữu _rootRef)
│   ├── PairingManager.cs           (TÁCH — Toàn bộ logic PIN & Pairing)
│   ├── LiveSessionReporter.cs      (TÁCH — Handshake, Activity, Ended, Heartbeat)
│   ├── TelemetryUploader.cs        (TÁCH — Push BehaviorSnapshot)
│   └── RemoteCommandListener.cs    (MỚI — Lắng nghe lệnh điều khiển từ Web)
│
├── Models/
│   ├── BehaviorSnapshot.cs         (GIỮ NGUYÊN)
│   ├── PairingData.cs              (GIỮ NGUYÊN)
│   └── RemoteCommand.cs            (MỚI — Model cho lệnh Remote Control)
```

### 2.2 Sơ đồ quan hệ giữa các module

```
┌─────────────────────────────────────────────────────────┐
│                    RTDBConnection                       │
│              (Singleton — DontDestroyOnLoad)            │
│                                                         │
│  ┌─ public DatabaseReference RootRef                    │
│  ┌─ public string DeviceId                              │
│  └─ OnApplicationQuit() → Dọn dẹp PIN                  │
├─────────────────────────────────────────────────────────┤
│                    Truy cập qua                         │
│            RTDBConnection.Instance.RootRef               │
└────────┬──────────┬───────────┬──────────┬──────────────┘
         │          │           │          │
         ▼          ▼           ▼          ▼
   ┌──────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐
   │ Pairing  │ │  Live  │ │Teleme- │ │   Remote     │
   │ Manager  │ │Session │ │  try   │ │  Command     │
   │          │ │Reporter│ │Uploader│ │  Listener    │
   └──────────┘ └────────┘ └────────┘ └──────────────┘
   Sở hữu:      Sở hữu:    Sở hữu:    Sở hữu:
   - _currentPin - Handshake - Push()   - Listener trên
   - _isPaired   - Activity  - Buffer   remote_commands/
   - Listener    - Ended     - Retry    - Dispatch Event
     trên PIN    - Heartbeat
```

---

## 3. Đặc Tả Từng Module

### 3.1 `RTDBConnection.cs` — Ổ cắm điện duy nhất

**Trách nhiệm:** Khởi tạo và chia sẻ `DatabaseReference` cho tất cả module con.

```csharp
// Pseudo-code minh hoạ
public class RTDBConnection : MonoBehaviour
{
    public static RTDBConnection Instance { get; private set; }
    public DatabaseReference RootRef { get; private set; }
    public string DeviceId { get; private set; }

    private void Awake()
    {
        // Singleton + DontDestroyOnLoad
        // Khởi tạo RootRef từ FirebasePaths.DatabaseUrl
        // Lưu DeviceId = SystemInfo.deviceUniqueIdentifier
    }

    private void OnApplicationQuit()
    {
        // Uỷ quyền cho PairingManager.Instance dọn PIN
    }
}
```

**Quy tắc:**
- Đây là MonoBehaviour **DUY NHẤT** trong folder RTDB được phép là Singleton.
- Không chứa bất kỳ logic nghiệp vụ nào (Không xử lý PIN, Session, hay Telemetry).

---

### 3.2 `PairingManager.cs` — Quản lý ghép nối PIN

**Trách nhiệm:** Sinh PIN, lắng nghe trạng thái paired/disconnected, xử lý lệnh Session mới từ Web.

**Di chuyển từ `RealtimeDBManager`:**
- `GenerateAndPushPIN()`
- `ResumeListening()`
- `StartListeningToPinNode()`
- `StopListeningAll()`
- `HandlePinNodeChanged()`
- Các biến: `_currentPin`, `_isPaired`, `_lastProcessedSessionId`
- Các Events: `OnPinGenerated`, `OnPairedSuccess`, `OnDisconnectedByWeb`, `OnNewSessionCommand`

**Thay đổi so với bản gốc:**
- Thay `GetRootRef()` → `RTDBConnection.Instance.RootRef`
- Thay `_deviceId` → `RTDBConnection.Instance.DeviceId`

---

### 3.3 `LiveSessionReporter.cs` — Báo cáo trạng thái Session

**Trách nhiệm:** Ghi nhận vòng đời phiên học lên nhánh `live_sessions/`.

**Di chuyển từ `RealtimeDBManager`:**
- `SendLiveSessionHandshake()`
- `UpdateCurrentActivity()`
- `SendLiveSessionEnded()`

**Code mới sẽ thêm vào (Roadmap):**
- `StartHeartbeat()` — Ghi `last_ping` mỗi 5 giây (Task C2)
- `StopHeartbeat()` — Dừng khi bài học kết thúc

**Nơi gọi hiện tại:**
- `SendLiveSessionHandshake()` → Được gọi từ `TimeManager.Start()`
- `UpdateCurrentActivity()` → Được gọi từ `SessionSyncTracker`
- `SendLiveSessionEnded()` → Được gọi từ `TimeManager.SaveLessonTimeData()`

---

### 3.4 `TelemetryUploader.cs` — Bắn dữ liệu hành vi

**Trách nhiệm:** Đẩy `BehaviorSnapshot` lên nhánh `behavior_snapshots/`.

**Di chuyển từ `RealtimeDBManager`:**
- `PushBehaviorSnapshot()`

**Code mới sẽ thêm vào (Roadmap):**
- Mở rộng payload khi Gaze Cone & Proximity được triển khai
- (Tuỳ chọn) Buffer/Retry nếu mạng không ổn định

**Nơi gọi hiện tại:**
- Được gọi từ `TelemetryStreamer.cs`

---

### 3.5 `RemoteCommandListener.cs` — Nhận lệnh từ Web Dashboard (MỚI)

**Trách nhiệm:** Lắng nghe nhánh `remote_commands/{sessionId}` trên RTDB và dispatch ra C# Events để các hệ thống Gameplay xử lý.

**Events dự kiến (dựa trên `WEB_DASHBOARD_IDEAS.md` §4):**

```csharp
// Pseudo-code minh hoạ
public static event Action OnTriggerHint;           // Gợi ý chủ động
public static event Action<float> OnSetVolume;      // Thay đổi âm lượng
public static event Action<string> OnPlayNpcScript; // Text-to-Speech
public static event Action OnSkipQuest;             // Bỏ qua nhiệm vụ
public static event Action OnPauseLesson;           // Fade-to-black khẩn cấp
```

**Lưu ý:** Module này chưa cần triển khai ngay. Nhưng việc dành sẵn "chỗ ngồi" cho nó trong kiến trúc sẽ giúp tránh phải tách lại lần nữa.

---

## 4. Kế Hoạch Thực Thi (Step-by-step)

### Bước 1: Tạo cấu trúc thư mục
- Trong Unity Editor, tạo folder `Scripts/Cloud/RTDB/`.

### Bước 2: Tạo `RTDBConnection.cs`
- Chuyển logic `Awake()` (Singleton, DontDestroyOnLoad), `GetRootRef()`, `_deviceId` sang file mới.
- Expose `RootRef` và `DeviceId` dưới dạng public property.

### Bước 3: Tạo `PairingManager.cs`
- Chuyển toàn bộ logic PIN: `GenerateAndPushPIN`, `ResumeListening`, `StartListeningToPinNode`, `StopListeningAll`, `HandlePinNodeChanged`.
- Chuyển Events: `OnPinGenerated`, `OnPairedSuccess`, `OnDisconnectedByWeb`, `OnNewSessionCommand`.
- Chuyển biến state: `_currentPin`, `_isPaired`, `_lastProcessedSessionId`.
- Thay tất cả `GetRootRef()` → `RTDBConnection.Instance.RootRef`.

### Bước 4: Tạo `LiveSessionReporter.cs`
- Chuyển: `SendLiveSessionHandshake`, `UpdateCurrentActivity`, `SendLiveSessionEnded`.
- Thay `GetRootRef()` → `RTDBConnection.Instance.RootRef`.

### Bước 5: Tạo `TelemetryUploader.cs`
- Chuyển: `PushBehaviorSnapshot`.
- Thay `GetRootRef()` → `RTDBConnection.Instance.RootRef`.

### Bước 6: Cập nhật tham chiếu (Critical!)
Tất cả các file đang gọi `RealtimeDBManager.Instance.XXX` cần được cập nhật:

| File cần sửa | Gọi cũ | Gọi mới |
|---|---|---|
| `PairingUI.cs` | `RealtimeDBManager.Instance.GenerateAndPushPIN()` | `PairingManager.Instance.GenerateAndPushPIN()` |
| `PairingUI.cs` | `RealtimeDBManager.Instance.OnPinGenerated += ...` | `PairingManager.Instance.OnPinGenerated += ...` |
| `SceneMenuController.cs` | `RealtimeDBManager.Instance.OnNewSessionCommand += ...` | `PairingManager.Instance.OnNewSessionCommand += ...` |
| `SessionSyncTracker.cs` | `RealtimeDBManager.Instance.UpdateCurrentActivity()` | `LiveSessionReporter.Instance.UpdateCurrentActivity()` |
| `TelemetryStreamer.cs` | `RealtimeDBManager.Instance.PushBehaviorSnapshot()` | `TelemetryUploader.Instance.PushBehaviorSnapshot()` |
| `TimeManager.cs` | `RealtimeDBManager.Instance.SendLiveSessionHandshake()` | `LiveSessionReporter.Instance.SendLiveSessionHandshake()` |
| `TimeManager.cs` | `RealtimeDBManager.Instance.SendLiveSessionEnded()` | `LiveSessionReporter.Instance.SendLiveSessionEnded()` |

### Bước 7: Xoá `RealtimeDBManager.cs`
- Sau khi tất cả tham chiếu đã được cập nhật và test pass.
- Xoá file gốc và file `.meta` tương ứng.

### Bước 8: Cập nhật Unity Scene
- Trong mọi Scene có GameObject "RTDB Manager":
  - Xoá component `RealtimeDBManager` cũ (nếu còn).
  - Thêm 4 component mới: `RTDBConnection`, `PairingManager`, `LiveSessionReporter`, `TelemetryUploader`.
- Hoặc: Tạo một Prefab mới "RTDB Manager" chứa sẵn 4 component, thay thế prefab cũ.

### Bước 9: Tạo placeholder `RemoteCommandListener.cs`
- Tạo file rỗng với comment TODO, chưa cần logic.
- Mục đích: Đánh dấu "chỗ ngồi" trong kiến trúc.

---

## 5. Nhật Ký Quyết Định (Decision Log)

| Vấn đề | Quyết định | Các phương án đã cân nhắc | Lý do chọn |
|---|---|---|---|
| Có nên tách `RealtimeDBManager` không? | **Tách thành 4 module + 1 placeholder** | (A) Giữ nguyên dùng `#region` — (B) Tách ra 4 class | Roadmap cho thấy Remote Control sẽ thêm listener RTDB hoàn toàn mới. Kết hợp với Heartbeat + Telemetry nâng cấp → file sẽ vượt 700 dòng. Tách sớm khi còn nhỏ dễ hơn tách muộn |
| Xử lý Shared State (`_rootRef`) | **RTDBConnection Singleton** sở hữu duy nhất `RootRef` | (A) Mỗi module tự khởi tạo `DatabaseReference` — (B) Truyền qua constructor | Unity MonoBehaviour không hỗ trợ constructor injection. Mỗi module tự khởi tạo sẽ tạo nhiều connection redundant |
| `PairingManager` có cần là Singleton? | **Có** — vì `PairingUI` và `SceneMenuController` đang truy cập trực tiếp | (A) Dùng Event Bus giữa — (B) Singleton | Giữ đơn giản, tránh over-engineering. Sau này có thể chuyển sang DI nếu cần |
| `RemoteCommandListener` triển khai ngay? | **Không — chỉ tạo placeholder** | (A) Code đầy đủ — (B) Placeholder | YAGNI. Tính năng Remote Control chưa nằm trong sprint hiện tại. Nhưng cần giữ chỗ để kiến trúc không bị phá vỡ khi thêm sau |

---

## 6. Tiêu Chí Hoàn Thành (Definition of Done)

- [ ] Folder `Scripts/Cloud/RTDB/` đã tồn tại trong Unity Project.
- [ ] `RTDBConnection.cs` biên dịch thành công, expose `RootRef` và `DeviceId`.
- [ ] `PairingManager.cs` biên dịch thành công, PIN flow hoạt động như cũ.
- [ ] `LiveSessionReporter.cs` biên dịch thành công, Handshake + Ended signal hoạt động.
- [ ] `TelemetryUploader.cs` biên dịch thành công, Snapshot xuất hiện trên Firebase Console.
- [ ] Tất cả file gọi `RealtimeDBManager.Instance` đã được cập nhật sang module mới.
- [ ] File `RealtimeDBManager.cs` gốc đã bị xoá.
- [ ] Chạy luồng test: GameMenu → Pair (Nhập PIN trên Firebase Console) → Vào Bathroom → Xem data trên RTDB → Quay về GameMenu. **Không có lỗi.**
- [ ] `RemoteCommandListener.cs` placeholder đã được tạo (chỉ có class rỗng + comment).
