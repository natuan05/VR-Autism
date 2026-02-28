# 🏗️ Kiến trúc VR-Autism: Scene Bathroom

## 📊 Sơ đồ tổng quan các thành phần

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           SCENE BATHROOM                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐                 │
│   │GameManager  │    │TimeManager  │    │FirebaseManager│               │
│   │(Singleton)  │    │(Singleton)  │    │(Cloud sync) │                 │
│   └─────────────┘    └─────────────┘    └─────────────┘                 │
│                             │                  ▲                         │
│                             │                  │                         │
│                             ▼                  │                         │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                     ActionManager                                │   │
│   │                 (Điều phối bài học)                             │   │
│   │  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐             │   │
│   │  │Event 1  │→ │Event 2  │→ │Event 3  │→ │Event 4  │             │   │
│   │  │Khởi tạo │  │Timeline │  │Nhiệm vụ │  │Lưu KQ   │             │   │
│   │  └─────────┘  └─────────┘  └─────────┘  └─────────┘             │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                                    │                                     │
│                                    ▼                                     │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                    QuestController                               │   │
│   │               (Quản lý nhiệm vụ thực hành)                      │   │
│   │  ┌───────┐  ┌───────┐  ┌───────┐  ┌───────┐  ┌───────┐         │   │
│   │  │Quest 0│→ │Quest 1│→ │Quest 2│→ │Quest 3│→ │Quest 4│→ ...    │   │
│   │  └───────┘  └───────┘  └───────┘  └───────┘  └───────┘         │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 🔄 Luồng hoạt động chi tiết

### Phase 1: Khởi động Scene

```
SCENE LOAD
    │
    ├── GameManager.Awake()     → Instance = this
    ├── TimeManager.Awake()     → Instance = this, Load LessonInfo
    ├── QuestController.Awake() → Init tất cả Quest
    └── ActionManager.Awake()   → Instance = this
            │
            ▼
    ActionManager.Start()
            │
            └── StartCoroutine(ActionLoop())  ← BẮT ĐẦU CHẠY TUẦN TỰ
```

---

### Phase 2: ActionLoop - Chạy tuần tự các Event

#### **EVENT 1: "Khởi tạo"**
```
┌─────────────────────────────────────────────────────────────────────────┐
│ OnStart:                                                                 │
│   ├─ washing_hands = TRUE           (đánh dấu bắt đầu bài học)          │
│   └─ TimeManager.StartLessonTime()                                      │
│               │                                                          │
│               ├─ timer.Start()       (bắt đầu đếm giờ)                  │
│               └─ StartCoroutine(TrackSkillUpdate())                     │
│                           │                                              │
│                           └─ CHẠY NGẦM MỖI GIÂY (suốt bài học)          │
│                                                                          │
│ Duration: 0                                                              │
│ Condition: None                                                          │
│ OnFinished: (empty)                                                      │
│                                                                          │
│ → CHUYỂN NGAY SANG EVENT 2                                              │
└─────────────────────────────────────────────────────────────────────────┘
```

#### **EVENT 2: "Chạy Timeline hướng dẫn"**
```
┌─────────────────────────────────────────────────────────────────────────┐
│ OnStart:                                                                 │
│   ├─ washing_hand = TRUE                                                │
│   └─ WashingHand.Play()              (phát video hướng dẫn rửa tay)     │
│                                                                          │
│ Duration: 0                                                              │
│ CHỜ: washing_hand_timeline == TRUE   (video xong sẽ set TRUE)           │
│                                                                          │
│ OnFinished:                                                              │
│   ├─ WaterLeak.SetActive(false)      (tắt hiệu ứng nước)                │
│   └─ SoapLiquid.SetActive(true)      (bật xà phòng)                     │
│                                                                          │
│ → CHUYỂN SANG EVENT 3                                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

#### **EVENT 3: "Bắt đầu nhiệm vụ"**
```
┌─────────────────────────────────────────────────────────────────────────┐
│ OnStart:                                                                 │
│   ├─ TimeManager.StartQuestTime()    (chuẩn bị ghi thời gian quest)     │
│   └─ QuestController.StartRunningQuest()                                │
│               │                                                          │
│               ▼                                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │  QUEST LOOP (chạy tuần tự bên trong QuestController)            │   │
│   │                                                                  │   │
│   │  Quest_0: Mở vòi nước                                           │   │
│   │     └─ Trẻ chạm vào → OnCompleteQuest() → curQuestId++          │   │
│   │                                                                  │   │
│   │  Quest_1: Lấy xà phòng                                          │   │
│   │     └─ Trẻ chạm vào → OnCompleteQuest() → curQuestId++          │   │
│   │                                                                  │   │
│   │  Quest_2: Xoa tay                                               │   │
│   │     └─ Trẻ giữ tay → OnCompleteQuest() → curQuestId++           │   │
│   │                                                                  │   │
│   │  ... (các Quest tiếp theo) ...                                  │   │
│   │                                                                  │   │
│   │  Quest cuối → isConditionMet = TRUE                             │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│ Duration: 0                                                              │
│ CHỜ: washing_hand_quest == TRUE      (tất cả quest hoàn thành)          │
│                                                                          │
│ OnFinished: (empty)                                                      │
│                                                                          │
│ → CHUYỂN SANG EVENT 4                                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

#### **EVENT 4: "Lưu kết quả" (CUỐI CÙNG)**
```
┌─────────────────────────────────────────────────────────────────────────┐
│ OnStart:                                                                 │
│   ├─ TimeManager.SaveLessonTimeData()                                   │
│   │       │                                                              │
│   │       ├─ timer.Stop()                                               │
│   │       ├─ Tính duration = endTime - startTime                        │
│   │       ├─ Gửi Firebase: finish_time, duration, quest_list, skills    │
│   │       └─ Lưu backup local                                           │
│   │                                                                      │
│   └─ ExitScene.SetActive(TRUE)       (hiện UI kết thúc)                 │
│                                                                          │
│ Duration: 0                                                              │
│ Condition: None                                                          │
│ OnFinished: (empty)                                                      │
│                                                                          │
│ → BÀI HỌC KẾT THÚC                                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 📊 Song song vs Tuần tự

| Thành phần               | Loại       | Mô tả                                    |
|--------------------------|------------|------------------------------------------|
| ActionLoop (Events)      | **TUẦN TỰ** | Event 1 → 2 → 3 → 4 theo thứ tự         |
| TrackSkillUpdate         | **SONG SONG** | Chạy ngầm mỗi giây, suốt bài học       |
| QuestController (Quests) | **TUẦN TỰ** | Quest 0 → 1 → 2 → ... theo thứ tự       |
| Firebase uploads         | **SONG SONG** | Gửi ngầm, không chặn game              |

---

## 📋 Dữ liệu được thu thập

```
LessonTimeData
├── lesson_name: "Rửa tay"
├── level_name: "Cơ bản"
├── start_time: "2024-01-15T10:00:00"
├── finish_time: "2024-01-15T10:05:30"
├── duration: 330 (giây)
├── device_id: "abc123..."
├── quest_list:
│   ├── Quest 0: { response_time: 15s, hint_count: 0 }
│   ├── Quest 1: { response_time: 20s, hint_count: 1 }
│   └── ...
└── skills:
    ├── Skill[0]: { initiation: 0, negotiation: 0, ... }
    └── Skill[1]: { initiation: 0, negotiation: 0, ... }  (mỗi phút thêm 1)
```

---

## 🔑 Các Script chính

| Script | Vai trò |
|--------|---------|
| `ActionManager.cs` | Điều phối các bước lớn của bài học (Events) |
| `TimeManager.cs` | Quản lý thời gian, ghi dữ liệu |
| `QuestController.cs` | Quản lý các nhiệm vụ nhỏ (Quests) |
| `Quest.cs` | Xử lý tương tác của từng nhiệm vụ |
| `FirebaseManager.cs` | Gửi dữ liệu lên cloud |
| `BooleanVariable.cs` | Biến chia sẻ giữa các script |

---

## 💡 Ghi nhớ

1. **ActionManager** = "Giáo viên" điều phối bài học
2. **TimeManager** = "Đồng hồ + Sổ ghi chép" 
3. **QuestController** = "Danh sách bài tập" cụ thể
4. **Firebase** = "Sổ điểm online" để phụ huynh/bác sĩ xem

---

# 🔥 Firebase Data Structure

## Cấu trúc Database

```
Firebase Realtime Database (Cloud)
│
└── sessions/
    │
    ├── "-NxYz123ABC"/                    ← sessionId (auto-generated)
    │   │
    │   ├── lesson_name: "Rửa tay"
    │   ├── level_name: "Cơ bản"
    │   ├── lesson_index: 0
    │   ├── lesson_id: "washing_hand_01"
    │   ├── level_index: 0
    │   ├── type: "practical"
    │   │
    │   ├── start_time: "2024-01-15T10:00:00"
    │   ├── finish_time: "2024-01-15T10:05:30"
    │   ├── duration: 330.5
    │   │
    │   ├── device_id: "abc123..."
    │   ├── video_url: "https://..."
    │   ├── score: 0
    │   ├── hasQuest: true
    │   │
    │   ├── quest_list/
    │   │   ├── 0/
    │   │   │   ├── index: 0
    │   │   │   ├── quest_name: "Bật vòi nước"
    │   │   │   ├── response_time: 15.5
    │   │   │   └── hint_count: 0
    │   │   │
    │   │   ├── 1/
    │   │   │   ├── index: 1
    │   │   │   ├── quest_name: "Lấy xà phòng"
    │   │   │   ├── response_time: 20.3
    │   │   │   └── hint_count: 1
    │   │   │
    │   │   └── 2/
    │   │       └── ...
    │   │
    │   └── skills/
    │       ├── 0/
    │       │   ├── initiation: 0
    │       │   ├── negotiation: 0
    │       │   ├── self_identity: 0
    │       │   └── cognitive_flexibility: 0
    │       │
    │       └── 1/
    │           └── ... (mỗi phút thêm 1)
    │
    └── "-NxYz456DEF"/                    ← Phiên học khác
        └── ...
```

---

## 📋 Chi tiết từng trường dữ liệu

### LessonTimeData (Thông tin bài học)

| Trường | Kiểu | Ý nghĩa | Được gán ở đâu |
|--------|------|---------|----------------|
| `lesson_name` | String | Tên bài học | [TimeManager.cs:39](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L39) - Awake() từ LessonInfo |
| `level_name` | String | Tên cấp độ | [TimeManager.cs:40](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L40) - Awake() từ LessonInfo |
| `lesson_index` | int | Số thứ tự bài học | [TimeManager.cs:41](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L41) - Awake() từ LessonInfo |
| `lesson_id` | String | ID định danh bài học | [TimeManager.cs:43](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L43) - Awake() từ LessonInfo |
| `level_index` | int | Số thứ tự cấp độ | [TimeManager.cs:42](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L42) - Awake() từ LessonInfo |
| `type` | String | "theoretical" hoặc "practical" | [TimeManager.cs:44](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L44) - Awake() từ LessonInfo |
| `start_time` | String | Thời điểm bắt đầu (ISO format) | [TimeManager.cs:55](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L55) - Start() |
| `finish_time` | String | Thời điểm kết thúc | [TimeManager.cs:129](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L129) - SaveDurationTime() |
| `duration` | double | Tổng thời gian (giây) | [TimeManager.cs:130](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L130) - SaveDurationTime() |
| `device_id` | String | ID thiết bị VR | [TimeManager.cs:60](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L60) - Start() |
| `video_url` | String | Link video ghi lại | (Chưa implement) |
| `score` | int | Điểm số | (Chưa implement) |
| `hasQuest` | bool | Có quest không | [TimeManager.cs:117](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L117) - StartQuestTime() |

### QuestTimeData (Dữ liệu từng quest)

| Trường | Kiểu | Ý nghĩa | Được gán ở đâu |
|--------|------|---------|----------------|
| `index` | int | Số thứ tự quest | [QuestController.cs:69](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Quest/QuestController.cs#L69) - OnCompleteQuest() |
| `quest_name` | String | Tên quest | [QuestController.cs:70](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Quest/QuestController.cs#L70) - OnCompleteQuest() |
| `response_time` | double | Thời gian hoàn thành (giây) | [QuestController.cs:64](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Quest/QuestController.cs#L64) - OnCompleteQuest() |
| `hint_count` | int | Số lần gợi ý | [ActionManager.cs:98](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/ActionManager.cs#L98) - ActionLoop() |

### SkillsData (Dữ liệu kỹ năng)

| Trường | Kiểu | Ý nghĩa | Được gán ở đâu |
|--------|------|---------|----------------|
| `initiation` | int | Khả năng chủ động | [TimeManager.cs:179](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L179) - TrackSkillUpdate() ⚠️ Luôn = 0 |
| `negotiation` | int | Khả năng đàm phán | [TimeManager.cs:180](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L180) - TrackSkillUpdate() ⚠️ Luôn = 0 |
| `self_identity` | int | Nhận thức bản thân | [TimeManager.cs:181](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L181) - TrackSkillUpdate() ⚠️ Luôn = 0 |
| `cognitive_flexibility` | int | Linh hoạt tư duy | [TimeManager.cs:182](file:///d:/Lab/VR-Autism-main/Assets/Dajunctic/Scripts/Manager/TimeManager.cs#L182) - TrackSkillUpdate() ⚠️ Luôn = 0 |

> ⚠️ **Lưu ý**: SkillsData hiện chưa có logic đánh giá thực sự (xem FUTURE_IDEAS.md)

---

## 🔄 Timeline gửi dữ liệu lên Firebase

```
Scene Load
    │
    ▼
FirebaseManager.Awake()
    │
    ├─ Khởi tạo Firebase connection
    └─ UploadLessonTimeData() → Tạo session mới với thông tin cơ bản
                                 (lesson_name, device_id, start_time, quest_list...)
    │
    ▼
Bài học bắt đầu
    │
    ├─ Mỗi Quest hoàn thành:
    │   └─ QuestController.OnCompleteQuest()
    │       └─ FirebaseManager.UpdateQuestData("response_time", time, index)
    │
    ├─ ActionManager (nếu onSendData = true):
    │   ├─ FirebaseManager.UpdateQuestData("response_time", time, index)
    │   └─ FirebaseManager.UpdateQuestData("hint_count", count, index)
    │
    ├─ Mỗi phút (TrackSkillUpdate):
    │   └─ FirebaseManager.PushNewSkillData(skill, index)
    │
    ▼
Bài học kết thúc
    │
    └─ TimeManager.SaveDurationTime()
        ├─ FirebaseManager.UpdateSessionData("finish_time", time)
        └─ FirebaseManager.UpdateSessionData("duration", seconds)
```

---

## 🏗️ Firebase lưu trữ như thế nào?

### 1. Cloud Storage
Firebase Realtime Database lưu trữ trên **server của Google**, không phải trên thiết bị người dùng.

### 2. Cấu trúc JSON
Dữ liệu được lưu dưới dạng **cây JSON**, có thể truy xuất bằng đường dẫn:
```
sessions/{sessionId}/quest_list/{index}/response_time
```

### 3. Truy xuất lại dữ liệu
Để đọc dữ liệu đã lưu:
```csharp
// Lấy tất cả sessions
DatabaseReference sessionsRef = dbReference.Child("sessions");
sessionsRef.GetValueAsync().ContinueWithOnMainThread(task => {
    DataSnapshot snapshot = task.Result;
    foreach (var session in snapshot.Children) {
        // Xử lý từng session...
    }
});
```

### 4. Persistence (Lưu trữ lâu dài)
- Dữ liệu được lưu **VĨNH VIỄN** trên server cho đến khi bạn xóa
- Có thể truy cập từ **BẤT KỲ thiết bị nào** có quyền
- Hỗ trợ **Firebase Console** để xem/sửa dữ liệu trực tiếp

---

## 🔐 Cách truy cập Firebase Console

1. Vào [https://console.firebase.google.com/](https://console.firebase.google.com/)
2. Chọn project VR-Autism
3. Chọn **Realtime Database** từ menu bên trái
4. Xem toàn bộ dữ liệu dạng cây JSON

---

# 📚 Chủ đề nghiên cứu tiếp theo

> Các chủ đề thú vị để tìm hiểu trong các buổi sau

| # | Chủ đề | Mô tả | Thư mục |
|---|--------|-------|---------|
| 1 | **Convai AI** | Chatbot AI cho NPC trò chuyện với trẻ | `Assets/Convai/` |
| 2 | **SpeechRecognition** | Nhận diện giọng nói của trẻ | `Assets/Dajunctic/Scripts/Player/SpeechRecognition.cs` |
| 3 | **uLipSync** | Đồng bộ khẩu hình NPC khi nói | `Assets/uLipSync/` |
| 4 | **VR Controllers** | Xử lý input tay VR (Oculus) | XR Interaction Toolkit |
| 5 | **Các Scene khác** | Farm, Zoo, Ocean, Supermarket... | `Assets/Scenes/` |
