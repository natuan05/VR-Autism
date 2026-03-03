# Kế Hoạch Code Refactoring – VR Autism

> **Dành cho Agent thực thi:**
> - Đây là tài liệu phân tích và kế hoạch thực thi code refactoring cho dự án VR-Autism (Unity C#).
> - Thực hiện **từng vấn đề một** theo thứ tự, không làm song song nhiều nhóm cùng lúc.
> - Sau mỗi nhóm thay đổi, **DỪNG LẠI** và nhắc người dùng mở Unity Editor để kiểm tra Console (không có lỗi đỏ) trước khi sang nhóm tiếp theo.
> - KHÔNG dùng Windows Explorer hay VS Code để di chuyển file `.cs` trong Unity – phải để Unity Editor tự quản lý `.meta`. Tuy nhiên **tạo file mới** bằng code là an toàn.
> - Với Unity, mỗi `namespace` phải khớp với đường dẫn `asmdef` hoặc ít nhất không được xung đột. Kiểm tra file `Assembly-CSharp.csproj` nếu có lỗi assembly.
> - Nếu đổi tên biến `public` (ví dụ: `_vertivalInput` → `_verticalInput`), đây là biến `private` nên không cần lo về Inspector reference. Nếu đổi tên biến `[SerializeField]`, phải dùng `[FormerlySerializedAs("oldName")]` để Unity không mất giá trị đã gán trong Inspector.

---

## 📊 Đánh Giá Tổng Quan

| Hạng mục | Mức độ | Ghi chú |
|---|---|---|
| Kiến trúc tổng thể | ✅ Tốt | EventChannel + BaseMono là pattern chuyên nghiệp |
| Singleton pattern | ⚠️ Không nhất quán | 3 cách viết Singleton khác nhau |
| Namespace | ⚠️ Thiếu | GameManager, Player, FirebaseManager... thiếu namespace |
| Dead code | ⚠️ Nhiều | Hàng trăm dòng comment-out trong TimeManager, FirebaseManager |
| Đặt tên biến | ⚠️ Lẫn lộn | snake_case lẫn camelCase, typo `_vertivalInput` |
| Đóng gói (Encapsulation) | ⚠️ Cần cải thiện | Nhiều `public` field nên là `[SerializeField] private` |
| Magic strings | ⚠️ Nguy hiểm | Path "sessions/{id}/skills/{index}" hardcode trong Firebase |

---

## 🔴 VẤN ĐỀ 1: Singleton Không Nhất Quán (Ưu tiên cao)

**Hiện tại:** Có 3 kiểu Singleton khác nhau trong cùng một dự án:
- `GameManager`: `public static GameManager Inst` (viết tắt tùy tiện)
- `TimeManager`, `ActionManager`: `public static XxxManager Instance` (không có duplicate guard)
- `EventChannel`: Singleton đầy đủ có lazy-init + destroy guard ✅ (chuẩn nhất)

**Vấn đề:** Nếu có 2 `GameManager` cùng tồn tại (do `DontDestroyOnLoad` + scene reload), `Inst` sẽ bị ghi đè mà không có cảnh báo.

### Proposed Changes

#### [MODIFY] `Assets/Project/Scripts/Core/Manager/GameManager.cs`
- Đổi tên `Inst` → `Instance` cho nhất quán
- Thêm singleton guard (kiểm tra duplicate, `Destroy` bản thừa)
- Thêm `namespace VRAutism.Core`
- Tách `MissionConfiguration` ra file riêng `MissionConfiguration.cs`

> **Lưu ý cho agent:** `GameManager` dùng `DontDestroyOnLoad`. Khi thêm guard, dùng mẫu sau:
> ```csharp
> private void Awake() {
>     if (Instance != null && Instance != this) { Destroy(gameObject); return; }
>     Instance = this;
>     DontDestroyOnLoad(gameObject);
> }
> ```

#### [MODIFY] `Assets/Project/Scripts/Core/Manager/TimeManager.cs`
- Thêm singleton guard như trên (không cần `DontDestroyOnLoad`)
- Tách 3 data class (`LessonTimeData`, `QuestTimeData`, `SkillsData`) ra thư mục `Scripts/Cloud/Models/`

#### [MODIFY] `Assets/Project/Scripts/Cloud/FirebaseManager.cs`
- Thêm singleton guard
- Thêm `namespace VRAutism.Cloud`

---

## 🔴 VẤN ĐỀ 2: Thiếu Namespace (Ưu tiên cao)

**Hiện tại:** Nhiều class quan trọng không có namespace, dễ xung đột tên khi project lớn:

| File | Hiện tại | Nên là |
|---|---|---|
| `GameManager.cs` | global | `VRAutism.Core` |
| `FirebaseManager.cs` | global | `VRAutism.Cloud` |
| `BaseMono.cs` | global | `VRAutism.Core` |
| `Player.cs` | global | `VRAutism.Player` |
| `SceneMenuController.cs` | global | `VRAutism.UI` |
| `LessonInfo.cs` | global | `VRAutism.Data` |
| `UIManager.cs` | global | `VRAutism.Core` |

### Proposed Changes
- Thêm `namespace VRAutism.Xxx { }` bao ngoài class tương ứng
- Thêm dòng `using` tương ứng ở các file gọi đến

> **Lưu ý cho agent:** Sau khi thêm namespace, tất cả file nào đang dùng `GameManager.Instance`, `FirebaseManager.Instance`... mà không có `using VRAutism.Core;` sẽ báo lỗi đỏ. Dùng global search để tìm và thêm `using`.

---

## 🟡 VẤN ĐỀ 3: Dead Code – Code Đã Comment-Out (Ưu tiên trung bình)

**Hiện tại:** ~60 dòng code bị comment-out:

```
TimeManager.cs  – dòng 17-18, 104, 139-158: VideoRecorder, GoogleDriveUploader
FirebaseManager.cs – dòng 48-88: phiên bản cũ AddSessionToFirebase (tính maxId thủ công)
ActionManager.cs – dòng 1-5: import trùng lặp (using VRAutism.Core x2)
```

### Proposed Changes

#### [MODIFY] `Assets/Project/Scripts/Core/Manager/TimeManager.cs`
- Xóa toàn bộ code đã comment-out (video recorder, google drive uploader)

#### [MODIFY] `Assets/Project/Scripts/Cloud/FirebaseManager.cs`
- Xóa block `/* */` phiên bản cũ của `AddSessionToFirebase`

#### [MODIFY] `Assets/Project/Scripts/Core/Manager/ActionManager.cs`
- Xóa dòng `using VRAutism.Core;` bị trùng lặp (line 4 và 5)

---

## 🟡 VẤN ĐỀ 4: Magic Strings cho Firebase Path (Ưu tiên trung bình)

**Hiện tại:** Firebase path hardcode dạng string:
```csharp
dbReference.Child("sessions").Child(sessionId).Child("quest_list").Child(index.ToString())
string path = $"sessions/{sessionId}/skills/{index}";
Application.persistentDataPath + "/Data/Saved/test.txt"
```

### Proposed Changes

#### [NEW] `Assets/Project/Scripts/Cloud/FirebasePaths.cs`
```csharp
namespace VRAutism.Cloud {
    public static class FirebasePaths {
        public const string Sessions  = "sessions";
        public const string QuestList = "quest_list";
        public const string Skills    = "skills";
    }
    public static class LocalPaths {
        public static string SessionData =>
            UnityEngine.Application.persistentDataPath + "/Data/Saved/session.json";
    }
}
```

#### [MODIFY] `Assets/Project/Scripts/Cloud/FirebaseManager.cs`
- Thay tất cả string literal path bằng các hằng số từ `FirebasePaths`

> **Lưu ý cho agent:** File save local hiện tại đang dùng tên `test.txt`. Khi đổi sang `session.json`, hãy chắc chắn `TimeManager.cs` và `FirebaseManager.cs` đều dùng cùng `LocalPaths.SessionData` để tránh đọc sai file.

---

## 🟡 VẤN ĐỀ 5: Đặt Tên Biến Sai Chuẩn (Ưu tiên trung bình)

**Hiện tại:**
```csharp
// Player.cs - typo
private float _vertivalInput;  // ❌ typo: "vertival" thay vì "vertical"

// TimeManager.cs - snake_case trong C# class fields
private DateTime start_time;
private DateTime end_time;
```

### Proposed Changes

#### [MODIFY] `Assets/Project/Scripts/Player/Player/Player.cs`
- `_vertivalInput` → `_verticalInput` (và cập nhật mọi chỗ dùng trong cùng file)

#### [MODIFY] `Assets/Project/Scripts/Core/Manager/TimeManager.cs`
- `start_time` → `_startTime`, `end_time` → `_endTime`

> **Lưu ý cho agent:** Vì đây là biến `private`, chỉ cần tìm-trong-file (Ctrl+H trong file), không cần lo về Inspector hay file khác.

---

## 🟢 VẤN ĐỀ 6: Encapsulation – Public Fields (Ưu tiên thấp)

**Hiện tại:** `Quest.cs` đang truy cập trực tiếp public fields của `QuestController`:
```csharp
controller.bubbleQuestion.SetActive(...)
controller.questProgressUI.transform.position = ...
```

### Proposed Changes

#### [MODIFY] `Assets/Project/Scripts/Quests/Quest/QuestController.cs`
- Đổi public fields → private + thêm các method:
  - `ShowBubble(bool show, Vector3 position)`
  - `ShowProgressBar(bool show, Vector3 position)`
  - `SetProgress(float value)`

#### [MODIFY] `Assets/Project/Scripts/Quests/Quest/Quest.cs`
- Thay các lần truy cập trực tiếp bằng cách gọi method ở trên

> **Lưu ý cho agent:** Do `questProgressUI` và `bubbleQuestion` là `[SerializeField]`, khi đổi sang `private` Unity sẽ vẫn giữ giá trị đã gán trong Inspector của từng Scene/Prefab.

---

## 🟢 VẤN ĐỀ 7: Tách Data Classes Ra File Riêng (Ưu tiên thấp)

**Hiện tại:** `TimeManager.cs` chứa 3 data class không liên quan đến timing logic.

### Proposed Changes

#### [NEW] `Assets/Project/Scripts/Cloud/Models/LessonTimeData.cs`
#### [NEW] `Assets/Project/Scripts/Cloud/Models/QuestTimeData.cs`
#### [NEW] `Assets/Project/Scripts/Cloud/Models/SkillsData.cs`

- Di chuyển (copy + xóa khỏi `TimeManager.cs`) từng class, thêm `namespace VRAutism.Cloud.Models`
- Thêm `using VRAutism.Cloud.Models;` vào `TimeManager.cs` và `FirebaseManager.cs`

---

## ✅ Phạm Vi KHÔNG Thay Đổi

- **EventChannel + BaseMono**: Kiến trúc Event-Driven đang hoạt động tốt – **giữ nguyên**
- **ActionManager.ActionLoop**: Logic Coroutine rõ ràng và đúng – **giữ nguyên**
- **Quest State Machine (State enum)**: đơn giản và hiệu quả – **giữ nguyên**
- **ScriptableObject variables** (`BooleanVariable`, `IntVariable`...): pattern tốt – **giữ nguyên**

---

## 🔍 Verification Plan – Kiểm Tra Sau Mỗi Bước

> [!IMPORTANT]
> **Agent PHẢI làm bước kiểm tra sau mỗi vấn đề** trước khi sang vấn đề tiếp theo.

### Thứ tự thực hiện an toàn

```
Bước 1: Dead code cleanup (Vấn đề 3, dễ nhất, không ảnh hưởng logic)
         → Báo user: mở Unity, kiểm tra Console không có lỗi đỏ

Bước 2: Namespace (Vấn đề 2)
         → Báo user: mở Unity, kiểm tra Console không có lỗi đỏ
         → Cần tìm tất cả file .cs dùng các class bị đổi namespace và thêm using

Bước 3: Singleton guard (Vấn đề 1)
         → Báo user: mở Unity, Play thử 1 Scene, kiểm tra Console

Bước 4: Tách Data Models (Vấn đề 7)
         → Báo user: mở Unity, kiểm tra Console không có lỗi đỏ

Bước 5: FirebasePaths constant (Vấn đề 4)
         → Báo user: chạy thử 1 session và kiểm tra Firebase Console xem data có lên không

Bước 6: Đặt tên biến (Vấn đề 5)
         → Báo user: mở Unity, kiểm tra Console không có lỗi đỏ, Play thử

Bước 7: Encapsulation QuestController (Vấn đề 6, phức tạp nhất)
         → Báo user: chơi thử 1 bài học hoàn chỉnh từ đầu đến cuối
```
