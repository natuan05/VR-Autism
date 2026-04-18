# Bản Kế Hoạch Tái Cấu Trúc File & Folder (Folder Refactoring Plan)

**Ngày lập:** 2026-04-18
**Người lập kế hoạch:** AI Senior Architect
**Hệ tư tưởng Kiến trúc:** Domain-Driven Design (DDD) & Feature-Based Organization (Phân rã theo tính năng).

---

## 1. Lý do & Mục tiêu (Why & Objectives)

**Vấn đề hiện tại:**
- **Thư mục `Core/Manager` đang bị phình to (God Folder):** Chứa cả những hệ thống chung cấp thấp (Time, Sound) lẫn các game logic cụ thể (Quest, Quiz, AnimalLesson).
- **Thư mục `Quests/` bị phân mảnh:** Data và Models nằm ở `Quests/`, nhưng bộ điều khiển (Controller) lại nằm ở `Core/`. Vi phạm tính gắn kết (High Cohesion).
- **Thư mục `Gameplay/` (treo đầu dê bán thịt chó):** Chỉ chứa các script điều khiển đồ vật (Props) lẻ tẻ như Vòi xà phòng, thay vì chứa Luồng chơi Game.

**Mục tiêu:**
- Bảo vệ sự "thuần khiết" của thư mục `Core`. Trả game logic về cho thư mục `Gameplay`.
- Gom nhóm theo tính năng (Tính đóng gói chuẩn Unity): Mọi thứ của một tính năng (Controllers, Models, Components, Events) phải nằm chung trong một "Gói".

---

## 2. Tiêu chuẩn Kiến trúc Thư mục Mới (Rule of Thumb)

| Thư mục Gốc | Mục đích Ràng buộc (Không được vi phạm) |
|---|---|
| `Scripts/Core/` | **Hạ tầng cơ sở (Infrastructure):** Chứa các Singleton/Tâm điểm chạy toàn bộ app (Time, SessionContext, Sound, SceneManager). Hoàn toàn mù (Unaware) về cụ thể bài học đang chơi là Quest hay Quiz. |
| `Scripts/Gameplay/` | **Tính năng Trò chơi (Game Features):** Chứa toàn bộ Game Mode, Logic điều hành từng loại bài học khác nhau (Quests, Quizzes, Interactions). |
| `Scripts/Entities/` | **Diễn viên & Đạo cụ (Actors & Props):** Các vật thể tương tác trên màn hình (Door, NPC, SoapDispencer) nhưng không phải là người ra quyết định. |
| `Scripts/UI/` | **Giao diện thuần (Views):** Chỉ lo việc in text ra màn hình và bắt event bấm nút. |

---

## 3. Các Bước Thực Thi Cụ Thể (Actionable Steps)

### Bước 1: Dọn rác khỏi thư mục `Gameplay/Objects`
Chuyển toàn bộ các script đại diện cho vật thể từ thư mục Gameplay về đúng nơi ở của chúng (Entities).

- 🚚 **Mục tiêu di chuyển:** Chuyển nội dung trong `Scripts/Gameplay/Objects/`
  - Di dời `SoapDispencer.cs` $\rightarrow$ Đưa đến `Scripts/Entities/Objects/SoapDispencer.cs`
  - Di dời `ProcessMilestone.cs` $\rightarrow$ Đưa đến `Scripts/Entities/Objects/ProcessMilestone.cs`
  - Di dời `BillboardEffect.cs` $\rightarrow$ Đưa đến `Scripts/Entities/Objects/BillboardEffect.cs`
- 🗑️ **Xóa sạch thư mục** `Scripts/Gameplay/Objects` tĩnh nếu nó trống rỗng.

### Bước 2: Gom nhóm Hệ thống Bài học (Actions, Quizzes, Exploration)

Tạo các thư mục tính năng mới bên trong `Gameplay/`:
1. `Scripts/Gameplay/Actions/` (Nhiệm vụ thực hành - Action-based)
2. `Scripts/Gameplay/Quizzes/` (Bài tập trắc nghiệm - Quiz-based)
3. `Scripts/Gameplay/Exploration/` (Khám phá tự do - Exploration-based)

### Bước 3: Mời "Vua" rời khỏi "Core"
Tìm trong `Scripts/Core/Manager/` và dọn các class sau trả về thư mục `Gameplay/` vừa tạo:

- Hành lý thuộc nhóm **Actions** (Chế độ thực hành):
  - `QuestController.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Controllers/QuestController.cs`
  - `ActionManager.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Controllers/ActionManager.cs`
  - `MissionManager.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Controllers/MissionManager.cs`

- Hành lý thuộc nhóm **Quizzes** (Chế độ trắc nghiệm):
  - `QuizController.cs` $\rightarrow$ `Scripts/Gameplay/Quizzes/Controllers/QuizController.cs`
  - `QuestionCollection.cs` $\rightarrow$ `Scripts/Gameplay/Quizzes/Models/QuestionCollection.cs`
  - `QuizUIController.cs` $\rightarrow$ `Scripts/Gameplay/Quizzes/UI/QuizUIController.cs`

- Hành lý thuộc nhóm **Exploration** (Chế độ khám phá):
  - `AnimalLessonManager.cs` $\rightarrow$ `Scripts/Gameplay/Exploration/AnimalLessonManager.cs`

### Bước 4: Tích hợp thư mục gốc `Scripts/Quests/` cũ
Gộp tất cả mọi thứ rải rác ngoài thư mục `Scripts/Quests` cũ vào trong `Scripts/Gameplay/Actions/`.
- `Scripts/Quests/Quest/Quest.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Models/Quest.cs`
- `Scripts/Quests/Quest/QuestEventData.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Models/QuestEventData.cs`
- `Scripts/Quests/CompleteMission.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Components/CompleteMission.cs`
- `Scripts/Quests/TutorialController.cs` $\rightarrow$ `Scripts/Gameplay/Actions/Controllers/TutorialController.cs`

*Sau khi gộp, hãy xóa hoàn toàn thư mục thừa gốc `Scripts/Quests`*

---

## 4. Cảnh báo Kỹ thuật quan trọng (Unity Developer Tips)

🚫 **ĐỪNG kéo thả trên Windows Explorer (File Explorer):** Điều này sẽ làm mất file `.meta` của Unity, khiến toàn bộ Inspector của Editor bị văng Reference (Missing Reference Script).
✅ **Hãy thực hiện trong màn hình Project của Unity Editor:** Hãy tạo thư mục và kéo các file `.cs` trực tiếp bằng chuột bên trong panel `Project` của Unity. Unity sẽ tự động lo việc dời cái file `.meta` ẩn đi theo.
✅ **Nếu sử dụng IDE (Rider / Visual Studio):** Bạn có thể nhấn Refactor (F6 hoặc Move) để IDE tự động đổi Namespace (nếu bạn có dùng Namespace) cho đồng bộ với thư mục mới.
