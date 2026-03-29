# 🏗️ Kiến trúc VR-Autism: Trạng thái Hiện tại

> **Cập nhật lần cuối:** 2026-03-29
> **Phiên bản:** Sau khi hoàn thành Task 1.1 & 1.2 (Firebase Firestore + Singleton Refactor)

---

## 📊 Sơ đồ tổng quan các thành phần

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           SCENE BATHROOM                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────────────────┐    │
│   │ GameManager │    │ TimeManager │    │    FirebaseManager       │    │
│   │ (Singleton) │    │ (Singleton) │    │    (Singleton)           │    │
│   └─────────────┘    └──────┬──────┘    └───────────┬─────────────┘    │
│                             │ BeginSession()         │ AccumulateQuestLog│
│                             │ LogQuestComplete()     │ SaveSession()     │
│                             └────────────────────────┘                  │
│                                        ▲                                 │
│                                        │                                 │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                     ActionManager (Singleton)                    │   │
│   │                 Điều phối bài học qua ActionLoop()              │   │
│   │                                                                  │   │
│   │  [Event 1: Khởi tạo] → [Event 2: Timeline] → [Event 3: Quest]  │   │
│   │                                       → [Event 4: Lưu kết quả] │   │
│   └───────────────────────────┬─────────────────────────────────────┘   │
│                               │ StartRunningQuest()                      │
│                               ▼                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                 QuestController (Observer Pattern)               │   │
│   │                                                                  │   │
│   │  Quest[0] → Quest[1] → Quest[2] → Quest[3] → ... → Quest[n]    │   │
│   │  (Mỗi Quest phát sự kiện, QuestController lắng nghe và phản hồi)│   │
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
    ├── GameManager.Awake()      → Instance = this
    ├── FirebaseManager.Awake()  → Instance = this, kết nối Firestore async
    ├── TimeManager.Awake()      → Instance = this
    ├── QuestController.Awake()  → Init() + subscribe Observer Events cho từng Quest
    └── ActionManager.Awake()    → Instance = this
            │
            ▼
    TimeManager.Start()
            └── BeginSession(lessonInfo...)  → tạo SessionData trong RAM
            
    ActionManager.Start()
            └── StartCoroutine(ActionLoop())  ← BẮT ĐẦU CHẠY TUẦN TỰ
```

---

### Phase 2: ActionLoop — Chạy tuần tự 4 Events

#### **EVENT 1: "Khởi tạo"**
```
OnStart:
  └─ TimeManager.StartLessonTime()
          ├─ _timer = new Stopwatch()
          ├─ _timer.Start()          (bắt đầu đếm giờ tổng)
          └─ lessonTime.Value = CurrentSecond

Duration: 0
Condition: None
OnFinished: (empty)
→ CHUYỂN NGAY SANG EVENT 2
```

#### **EVENT 2: "Chạy Timeline hướng dẫn"**
```
OnStart:
  ├─ washing_ha = TRUE               (reset BooleanVariable)
  └─ WashingHandTimeline.Play()      (phát video hướng dẫn rửa tay)

Duration: 0
CHỜ: washing_hand_timeline == TRUE   (Signal Emitter ở cuối Timeline set TRUE)

OnFinished:
  ├─ WaterLeak.SetActive(false)
  └─ SoapLiquid.SetActive(false)

→ CHUYỂN SANG EVENT 3
```

#### **EVENT 3: "Bắt đầu nhiệm vụ"**
```
OnStart:
  └─ QuestController.StartRunningQuest()
          ├─ isConditionMet.Value = false
          └─ StartNewQuest()
                  ├─ TimeManager.Instance.StartQuestTime()   (stamp thời gian bắt đầu quest)
                  └─ quests[curQuestId].SetState(Enable)     (kích hoạt Quest đầu tiên)

    ┌─────────────────────────────────────────────────────────────────┐
    │  QUEST LOOP (chạy tuần tự bên trong QuestController)            │
    │                                                                  │
    │  Quest[0] (Enable) → Trẻ chạm vào                               │
    │     └─ OnTriggerEnter → SetState(Completed)                     │
    │         └─ OnQuestCompleted.Invoke()                            │
    │             └─ QuestController.OnCompleteQuest()                │
    │                 ├─ TimeManager.Instance.LogQuestComplete(...)   │
    │                 │       └─ FirebaseManager.AccumulateQuestLog() │
    │                 ├─ curQuestId++                                  │
    │                 └─ StartNewQuest() → Quest[1].SetState(Enable)  │
    │                                                                  │
    │  ... (lặp lại cho Quest[1], [2], ...) ...                       │
    │                                                                  │
    │  Quest cuối → OnCompleteQuest()                                 │
    │     ├─ congratulationUI.SetActive(true)                         │
    │     ├─ this.SendEvent(EventID.ExitScene)                        │
    │     └─ isConditionMet.Value = TRUE  ← ĐÁNH THỨC ActionLoop     │
    └─────────────────────────────────────────────────────────────────┘

Duration: 0
CHỜ: isConditionMet == TRUE          (Quest cuối set TRUE)

OnFinished: (empty)
→ CHUYỂN SANG EVENT 4
```

#### **EVENT 4: "Lưu kết quả" (CUỐI CÙNG)**
```
OnStart:
  └─ TimeManager.SaveLessonTimeData()
          ├─ _timer.Stop()
          ├─ durationSeconds = _timer.Elapsed.TotalSeconds
          └─ FirebaseManager.Instance.SaveSession(status, score, duration)
                  └─ docRef.SetAsync(_currentSession)  → GHI 1 LẦN lên Firestore

Duration: 0
Condition: None
→ BÀI HỌC KẾT THÚC
```

---

## 🗂️ Kiến trúc Quest: Observer Pattern

Kể từ lần refactor gần nhất, `Quest.cs` **không còn** giữ tham chiếu trực tiếp đến `QuestController`. Thay vào đó dùng C# Actions:

```csharp
// Quest.cs phát ra tín hiệu (broadcaster)
public Action<bool, Vector3> RequestShowBubble;
public Action<bool, Vector3> RequestShowProgressBar;
public Action<float>         RequestSetProgress;
public Action                OnQuestCompleted;

// QuestController.cs đăng ký lắng nghe trong Awake()
quest.RequestShowBubble    += ShowBubble;
quest.RequestShowProgressBar += ShowProgressBar;
quest.RequestSetProgress   += SetProgress;
quest.OnQuestCompleted     += OnCompleteQuest;

// QuestController.cs huỷ đăng ký trong OnDestroy() → ngăn Memory Leak
quest.OnQuestCompleted -= OnCompleteQuest;
```

**Lợi ích:** `Quest.cs` hoàn toàn độc lập, có thể dùng lại trong bất kỳ scene nào mà không cần `QuestController`.

---

## 🔥 Firebase Data Flow (Kiến trúc Batch Write)

```
Gameplay
    │
    ├── Mỗi Quest hoàn thành:
    │   └─ TimeManager.LogQuestComplete()
    │       └─ FirebaseManager.AccumulateQuestLog(log)  → lưu vào RAM (_currentSession.quest_logs)
    │
    ▼
Bài học kết thúc (Event 4):
    └─ TimeManager.SaveLessonTimeData()
        └─ FirebaseManager.SaveSession()
            └─ docRef.SetAsync(_currentSession)   → GHI 1 LẦN DUY NHẤT lên Firestore
```

> ⚡ **Thay đổi so với trước:** Không còn nhiều lần ghi lên Realtime Database sau mỗi quest.
> Toàn bộ session được tích lũy trong RAM rồi ghi batch 1 lần duy nhất khi bài học kết thúc.

---

## 📋 Cấu trúc dữ liệu Firestore hiện tại

```
Firestore
└── sessions/
    └── {session_id}/           ← GUID tự động sinh
        ├── session_id          (string)
        ├── device_id           (string)
        ├── lesson_id           (string)
        ├── lesson_name         (string)
        ├── level_name          (string)
        ├── level_index         (int)
        ├── type                ("theoretical" | "practical")
        ├── start_time          (string ISO 8601)
        ├── finish_time         (string ISO 8601)
        ├── duration            (double, giây)
        ├── score               (int)
        ├── completion_status   ("success" | "aborted")
        └── quest_logs/
            └── [array]
                ├── index             (int)
                ├── quest_name        (string)
                ├── response_time     (double, giây)
                ├── completion_status (string)
                ├── hints_verbal      (int)
                ├── hints_visual      (int)
                └── hints_physical    (int)
```

---

## 🔑 Các Script chính & Vai trò

| Script | Namespace | Singleton | Vai trò |
|--------|-----------|-----------|---------|
| `ActionManager.cs` | `VRAutism.Core` | ✅ | Điều phối tuần tự các bước lớn của bài học |
| `TimeManager.cs` | `VRAutism.Core` | ✅ | Đếm giờ, tích lũy log quest, trigger lưu Firebase |
| `FirebaseManager.cs` | `VRAutism.Cloud` | ✅ | Kết nối Firestore, tích lũy session RAM, batch write |
| `QuestController.cs` | `VRAutism.Quests` | ❌ | Điều phối tuần tự từng Quest, nhận sự kiện Observer |
| `Quest.cs` | `VRAutism.Quests` | ❌ | Xử lý collision VR, phát Observer Events |
| `BooleanVariable.cs` | `VRAutism.Core` | ❌ | ScriptableObject dùng làm tín hiệu giữa ActionManager và QuestController |

---

## 💡 Ghi nhớ nhanh

| Thành phần | Ví von |
|---|---|
| **ActionManager** | "Giáo viên" điều phối tiết học từ đầu đến cuối |
| **QuestController** | "Danh sách bài tập cụ thể" trong tiết học |
| **TimeManager** | "Đồng hồ + Sổ ghi chép tổng hợp" |
| **FirebaseManager** | "Nộp bài cuối buổi lên server" (ghi 1 lần khi xong) |
| **Quest** | "Từng bài tập vật lý" — trẻ chạm vào để hoàn thành |

---

## 📚 Chủ đề nghiên cứu tiếp theo (Task 1.3+)

| # | Chủ đề | Mô tả | Trạng thái |
|---|--------|-------|------------|
| 1 | **Gaze & Head Tracking** | Thu thập hướng nhìn và xoay đầu bằng XR HMD SDK | 🔴 Chưa bắt đầu |
| 2 | **PIN Pairing** | Sinh mã PIN 6 số để ghép nối với Web Dashboard | 🔴 Chưa bắt đầu |
| 3 | **Remote Control Listener** | Nhận lệnh hint/pause từ chuyên gia qua RTDB | 🔴 Chờ Web Team |
| 4 | **Convai AI NPC** | Chatbot AI cho NPC trò chuyện với trẻ | 🔴 Chưa bắt đầu |
| 5 | **Video Stream (WebRTC)** | Đẩy khung hình Camera lên Web | 🔴 Chờ Web Team |
