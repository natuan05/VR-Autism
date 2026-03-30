# 🖼️ HỆ THỐNG BIỂU ĐỒ KIẾN TRÚC & NGỮ CẢNH (SYSTEM DIAGRAMS)
Dự án: **VR-Autism** (Hệ sinh thái Giáo dục & Trị liệu cho Trẻ tự kỷ bằng VR)

> **Mục đích file này:** Tập hợp toàn bộ các bản vẽ phác thảo, biểu đồ thiết kế hệ thống quan trọng nhất. Phục vụ cho System Architect, BA và các Developer cái nhìn trực quan nhất về luồng chạy và phân chia khu vực của dự án.

---

## 2.3 Biểu đồ Luồng Dữ liệu Mức 1 (Data Flow Diagram - Level 1)

```mermaid
flowchart TD
    %% Styles cho DFD chuẩn
    classDef external fill:#f9f9f9,stroke:#333,stroke-width:2px,color:#000
    classDef process fill:#1e4c9e,stroke:#fff,stroke-width:2px,color:#fff,shape:circle
    classDef datastore fill:#1168bd,stroke:#fff,stroke-width:2px,color:#fff,shape:rect,stroke-dasharray: 5 5

    %% EXTERNAL ENTITIES
    E0A["\uD83D\uDD11 System Admin"]:::external
    E0B["\uD83C\uDFEB Quản lý Trung tâm"]:::external
    E1A["\uD83D\uDC68\u200D\uD83C\uDFEB Chuyên gia"]:::external
    E1B["\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC66 Phụ huynh"]:::external
    E2["\uD83D\uDC66 Trẻ tự kỷ"]:::external

    %% DATA STORES
    D0[(D0: Hệ thống Trung tâm)]:::datastore
    D1[(D1: Hồ sơ Trẻ)]:::datastore
    D2[(D2: Bài học)]:::datastore
    D3[(D3: Live DB)]:::datastore
    D4[(D4: Lịch sử & Logs)]:::datastore

    %% QUẦNG BÊN WEB DASHBOARD
    subgraph WEB [\uD83D\uDCBB Tuyến Web Dashboard]
        P0(("0.0\u003cbr/\u003eQuản lý\nTrung tâm")):::process
        P1(("1.0\u003cbr/\u003eQuản lý\nHồ sơ")):::process
        P2(("2.0\u003cbr/\u003eCấu hình\nBài học")):::process
        P5(("5.0\u003cbr/\u003ePhân tích\nBáo cáo")):::process
    end

    %% QUẦNG BÊN VR APP
    subgraph VR [🥽 Tuyến Ứng dụng VR]
        P4(("4.0<br/>Khởi chạy<br/>VR Game")):::process
    end

    %% QUẦNG CHUNG
    P3(("3.0<br/>Đồng bộ<br/>Real-time")):::process


    %% Từ Chuyên gia
    E1A -- "Tạo/Sửa Hồ sơ" --> P1
    E1A -- "Set Phác đồ" --> P2
    P5 -- "Báo cáo chuyên môn" --> E1A
    E1A -- "Lệnh Co-pilot Live" --> P3
    P3 -- "Ghi AUTO_ALERTS (Realtime) & Logs Manual (Batch)" --> D4
    
    %% Từ Phụ huynh
    E1B -- "Cập nhật Nhạy cảm" --> P1
    P5 -- "Tóm tắt kết quả" --> E1B
    E1B -- "Lệnh khởi động (PIN)" --> P3

    %% Tương tác Kho dữ liệu
    P1 -- "Lưu/Tải dữ liệu" --> D1
    D1 -- "Trích xuất hồ sơ" --> P1
    D2 -- "Đọc File Bài Học" --> P2
    P5 -- "Truy vấn Lịch sử" --> D4
    P5 -- "Truy vấn Thông tin" --> D1

    %% Luồng Cấu hình
    P2 -- "Gửi dữ liệu Session" --> P3
    P3 -- "Các tham số môi trường" --> P4
    
    %% Tương tác Kho Live
    P3 -- "Ghi nhận trạng thái" --> D3
    D3 -- "Đồng bộ realtime" --> P3
    
    %% Quá trình chơi VR
    P4 -- "Hình ảnh 3D, Âm thanh" --> E2
    E2 -- "Hành vi, Phản xạ" --> P4
    
    %% Ghi nhận Data
    P4 -- "Tọa độ sensor (Telemetry)" --> P3
    P3 -- "Cập nhật Tiến độ" --> E1A
    P3 -- "Cập nhật Tiến độ" --> E1B

    P4 -- "Ghi Session + Quest Logs (Bulk Write)" --> D4

```

---

## 2.4 Biểu đồ Tuần tự: Luồng Kết nối Thiết bị (Device Pairing Sequence)
> **Mục tiêu:** Trả lời cho lập trình viên biết quá trình "nhấn nút" và "load giao diện" giữa Kính VR và Web khi **chuyên gia** nhập mã PIN và khởi động buổi học diễn ra theo thứ tự API nào.

```mermaid
sequenceDiagram
    autonumber
    
    actor Child as 👦 Trẻ (Oculus)
    participant VR as 🥽 Kính VR
    participant LiveDB as ⚡ Giàn Realtime DB
    participant Web as 💻 Web Dashboard
    actor Expert as 👨‍🏫 Chuyên gia

    Note over Child, VR: BƯỚC 1: KHỞI ĐỘNG KÍNH (VR-First)
    Child->>VR: Đội kính & Mở App VR
    VR->>LiveDB: Bắn API: Tạo mã PIN ngẫu nhiên (VD: 123456)
    LiveDB-->>VR: Trả về trạng thái "waiting" (Chờ ghép nối)
    VR->>Child: Hiển thị màn hình lơ lửng: "Nhập mã PIN 123456 vào Web"

    Note over Web, Expert: BƯỚC 2: CHUYEN GIA KET NOI (Web-side)
    Expert->>Web: Đăng nhập Web & Chọn Hồ sơ "Bé Nam"
    Expert->>Web: Nhập mã PIN "123456" vào ô Kết nối Thiết bị
    Note right of Web: Áp dụng Rate Limit (Chống spam API/Dò mã)
    Web->>LiveDB: Query: Có thiết bị nào mang mã 123456 đang chờ không?
    LiveDB-->>Web: Trả về Object { device_id: "QUEST_PRO", status: "waiting" }
    Web->>LiveDB: Ghi đè: Cập nhật status = "paired", gắn UID Chuyên gia
    
    LiveDB--)VR: (Realtime Listener bắt được status đổi thành "paired")
    VR->>Child: Kính phát âm thanh ting, màn hình báo "Đã ghép nối!"
    
    Note over Expert, VR: BƯỚC 3: KHỞI ĐỘNG BÀI HỌC
    Expert->>Web: Bấm chọn bài "Rửa tay" (Level 1) -> Khởi Động
    Web->>LiveDB: Tạo Session mới, ghi Cấu hình (độ lớn âm thanh, hint...)
    LiveDB--)VR: (Realtime Listener báo có Lesson mới)
    VR->>Child: Dọn dẹp màn hình chờ, Load ngay môi trường 3D bài Rửa tay
```

---

## 2.5 Biểu đồ Tuần tự: Luồng Co-located & Điều khiển (Live Control Sequence)
> **Mục tiêu:** Minh họa luồng dữ liệu liên tục trong suốt thời gian học tại cùng một địa điểm. Video POV được bắn thẳng từ kính VR sang Web Dashboard thông qua mạng LAN (Peer-to-Peer WebRTC) để Chuyên gia xem và điều khiển trên cùng 1 màn hình.

```mermaid
sequenceDiagram
    actor Child as 👦 Trẻ
    participant VR as 🥽 Kính VR (Unity)
    participant LiveDB as ⚡ Realtime DB (Signaling)
    participant Web as 💻 Web Dashboard
    actor Expert as 👨‍🏫 Chuyên gia

    Note over Child, Expert: PHẦN 1: KẾT NỐI P2P & TRUYỀN HÌNH ẢNH
    VR->>LiveDB: Gửi Signal WebRTC Offer 
    Web->>LiveDB: Gửi Signal WebRTC Answer
    par Video Streaming (WebRTC Local LAN)
        VR--)Web: Đẩy luồng Video POV 30fps trực tiếp
        Web->>Expert: Xem Video hình ảnh mượt mà trên Browser
    and State Update (Event-driven, khi có thay đổi)
        VR->>LiveDB: Push { quest_index, level, status } khi state thay đổi
        LiveDB--)Web: Đồng bộ thông số UI
        Web->>Expert: Cập nhật Widget (Quest hiện tại, Level, tiến độ)
    and Telemetry Stream (Tần suất cao - Phục vụ Web AI AlertEngine)
        VR->>LiveDB: Push BehaviorSnapshot { head.yaw, velocity, deviation_deg... }
        LiveDB--)Web: Nhận stream để chạy logic Auto-Detect
        Note right of Web: 🧠 AlertEngine (Web) phát hiện tín hiệu: Freeze!
        Web->>Firestore: Ghi AUTO_ALERTS ngay lập tức (Realtime)
    end
    Note over VR, Web: ⚡ Data Telemetry (Sensor 2s/lần) bắt buộc chạy qua Realtime DB để hệ thống Web Dashboard lọc tự động.

    Note over Expert, VR: PHẦN 2: CAN THIỆP VÀO VR (REMOTE CONTROL)
    Expert->>Web: Xem POV Trẻ và bấm nút "Hướng dẫn bằng lời / Visual cue"
    Note right of Web: Debounce/Throttle 0.5s (Chống spam Click)
    Web->>LiveDB: Ghi { command: "verbal_hint" | "visual_cue" | "adjust_level" } vào /commands
    Web->>RAM: Ghi tạm { time_offset, quest_index, command_type, command_detail } vào RAM (chờ cuối ván)
    
    LiveDB--)VR: Firebase đẩy lệnh xuống Kính
    Note left of VR: Kính kiểm tra State (Source of truth)<br/>Ignore nếu không khớp context
    VR->>VR: Unity EventChannel.cs phát sóng sự kiện
    alt verbal_hint
        VR->>Child: NPC phát tiếng: "Nam ơi, hãy vặn vòi nước nào!"
    else visual_cue
        VR->>Child: Hiện mũi tên / highlight vật thể cần tương tác trong VR
    end
    
    Note over Expert, Web: PHẦN 3: ĐÁNH DẤU HÀNH VI LÂM SÀNG
    Child->>VR: Trẻ tỏ ra sợ hãi (Thấy qua Video)
    Expert->>Web: Ấn nhanh nút Record: "Nhạy cảm thính giác"
    Web->>RAM: Ghi tạm { time_offset, event, triggered_by } vào RAM (chờ cuối ván)

    Note over Child, Web: PHẦN 4: KẾT THÚC BUỔI HỌC (POST-SESSION BATCH WRITE)
    Expert->>Web: Bấm "Kết thúc buổi học" -> Hiện bảng Cập nhật Note
    Web->>Expert: Cho phép sửa Note/Review ở màn hình Post-Session
    Expert->>Web: Bấm "Chốt báo cáo"
    participant Firestore as 🗄️ Cloud Firestore
    Web->>Firestore: Batch Write: Lưu đồng loạt INTERVENTION_LOGS, BEHAVIOR_LOGS
    Web->>LiveDB: Ghi trạng thái session = "finished"
    LiveDB--)VR: Lắng nghe sự kiện kết thúc
    VR->>VR: Gom toàn bộ Session Logs (Quest Logs, Raw Telemetry File) thành 1 file JSON
    VR->>Firestore: Bulk Write: Đẩy cục JSON lên lưu trữ định dạng gốc
    Note over VR, Firestore: Cảnh báo tự động (AI) -> Ghi rải rác Realtime.<br/>Ghi chú của Giáo viên (Intervention, Behavior) -> Web Gom Batch Write cuối giờ.<br/>Raw Data Gameplay (Quests, Sensor) -> Kính VR Bulk Write 1 cục khi thoát.
```

---

## 2.6 Phân cấp Vai trò & Phân quyền (Role Hierarchy)
> **Mục tiêu:** Mô tả rõ ai có quyền làm gì, dữ liệu nào họ được truy cập. Bảo mật được thực thi tại **Firestore Security Rules** (server-side) và **Firebase Custom Claims** (token-based), không phải chỉ ở frontend routing.

```mermaid
flowchart TD
    classDef sysadmin fill:#6f42c1,stroke:#fff,color:#fff,stroke-width:2px
    classDef manager fill:#0d6efd,stroke:#fff,color:#fff,stroke-width:2px
    classDef expert fill:#198754,stroke:#fff,color:#fff,stroke-width:2px
    classDef parent fill:#fd7e14,stroke:#fff,color:#000,stroke-width:2px
    classDef deny fill:#dc3545,stroke:#fff,color:#fff,stroke-width:1px

    SA["🔑 SYSTEM ADMIN"]:::sysadmin
    CM["🏫 CENTER MANAGER - 1 per center"]:::manager
    EX["👨‍🏫 EXPERT - nhiều per center"]:::expert
    PA["👨‍👩‍👦 PARENT"]:::parent
    DENY["⛔ SYSTEM ADMIN không thấy dữ liệu lâm sàng"]:::deny

    SA -- "tạo Centers + CM account" --> CM
    CM -- "tạo Expert accounts" --> EX
    CM -- "tạo + liên kết Parent" --> PA
    SA -.-> DENY

    subgraph ISO ["🔒 DATA ISOLATION - Trung tâm A không thấy trung tâm B"]
        RULE1["Firestore Rule: allow read if request.auth.token.center_id == resource.data.center_id"]
        RULE2["Firebase Custom Claim: role + center_id trong mỗi token"]
        RULE1 --- RULE2
    end
```

### Bảng quyền đầy đủ

| Quyền | System Admin | Center Manager | Expert | Parent |
|---|:---:|:---:|:---:|:---:|
| Tạo / xóa Centers | ✅ | ❌ | ❌ | ❌ |
| Tạo Center Manager | ✅ | ❌ | ❌ | ❌ |
| Tạo Expert accounts | ❌ | ✅ | ❌ | ❌ |
| Tạo Parent accounts | ❌ | ✅ | ❌ | ❌ |
| Quản lý Child Profiles | ❌ | ✅ trung tâm mình | ✅ của mình | ❌ |
| Chạy Sessions | ❌ | ❌ | ✅ | ❌ |
| Xem báo cáo lâm sàng | ❌ | ✅ tổng trung tâm | ✅ trẻ của mình | ✅ con mình |
| Tạo / sửa Lessons | ❌ | ❌ | ❌ | ❌ |
| Lessons do ai quản lý? | Dev / DB migration | ❌ | ❌ | ❌ |
