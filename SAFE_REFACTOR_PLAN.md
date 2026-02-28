# 🔧 Kế hoạch Refactor Dự án VR-Autism (Mức độ An Toàn Cao)

> Phân bổ lại cấu trúc Source Code đảm bảo **Zero-Breakage** (Không làm hỏng logic, mất references hay hỏng UI/Scene hiện có).

---

## 🛑 NGUYÊN TẮC TỐI THƯỢNG ĐỂ KHÔNG HỎNG LOGIC

Trong Unity, mọi file tài nguyên (Script, Prefab, Scene, Audio...) đều đi kèm một file ẩn là `.meta`. File `.meta` này chứa một **GUID (Globally Unique Identifier)** giúp Unity nhận diện file đó, bất kể nó nằm ở thư mục nào.

- ❌ **TỐI KỴ:** KHÔNG BAO GIỜ được dùng Windows Explorer (File Explorer) hay kéo thả file trong VS Code để thay đổi thư mục. Nếu làm vậy, file `.meta` có thể bị rớt lại hoặc tạo mới, dẫn đến **Missing Script / Missing Reference** hàng loạt trên các Scene.
- ✅ **NGUYÊN TẮC CHUẨN:** Mọi thao tác tạo thư mục mới (Create > Folder) và di chuyển file phải thực hiện **100% bên trong thẻ Project của Unity Editor**.

---

## 🎯 1. Tầm nhìn Cấu trúc Mới

Chúng ta sẽ chuyển từ cấu trúc "Theo cá nhân" (`Dang`, `Dajunctic`) sang cấu trúc "Theo Module/Chức năng" chuẩn công nghiệp, với tiền tố `_` để luôn hiển thị ở trên cùng.

```
Assets/
│
├── _Project/                  ← Mọi thứ bạn tạo ra sẽ nằm ở đây
│   ├── Art/                   ← Material, Textures, Models tự làm
│   ├── Audio/                 ← SFX, BGM, Voice
│   ├── Prefabs/               ← Các GameObject tái sử dụng
│   ├── Scenes/                ← Menu, Bathroom, Farm...
│   ├── ScriptableObjects/     ← Data config (LessonConfig, Event variables)
│   └── Scripts/               ← Mọi C# Script
│       ├── Core/              ← RunMonitor, TimeManager, GameManager, SceneSO, Utils
│       ├── Firebase/          ← FirebaseManager, Models API
│       ├── Quests/            ← QuestController, Quest, QuestAction
│       ├── UI/                ← SceneMenuController, LessonDetailUI
│       └── Entities/          ← Dữ liệu về Player, NPC, Flock...
│
├── ThirdParty/                ← SDK & Plugins không sửa code
│   ├── Convai/
│   ├── Firebase/
│   ├── Oculus/
│   └── uLipSync/
│
└── ImportedAssets/            ← Các Asset Pack từ Unity Asset Store
    ├── CoffeeShopStarterPack/
    ├── RPG Food Props/
    └── LowPoly_Animals/
```

---

## 🗺️ 2. Quy trình Thực thi 4 BƯỚC (An Toàn)

### 📦 Bước 1: Backup & Snapshot (BẮT BUỘC)
- Đảm bảo dự án không có lỗi (Console màu xám, không có chấm đỏ 🔴).
- Commit code lên **Git** (với message: `chore: backup before refactor`) hoặc copy hẳn nguyên file Project ra một ổ cứng khác.
- Đóng tất cả các Scene đang mở trong Unity Editor, chỉ mở một `Empty Scene`.

### 🗂️ Bước 2: Dọn dẹp & Khởi tạo (Trong Unity Editor)
1. Trong cửa sổ Project của Unity, chuột phải tạo thư mục `Assets/_Project`.
2. Trong `_Project`, tạo tiếp các thư mục: `Scripts`, `Prefabs`, `Scenes`, `ScriptableObjects`, `Art`, `Audio`.
3. Tạo thư mục `Assets/ThirdParty` và `Assets/ImportedAssets`.
4. Tìm các file rác (chưa được sử dụng như `NewBehaviourScript.cs`) và xóa chúng.

### 🚚 Bước 3: Di chuyển Asset (Trong Unity Editor)
**QUAN TRỌNG:** Làm từ từ từng nhóm một. Di chuyển xong 1 nhóm thì mở `GameMenu` hoặc `Bathroom` lên bấm Play để kiểm tra xem có hỏng gì không.

1. **Gom Modules:** 
   - Mở thư mục `Assets/Dang/Scripts` và `Assets/Dajunctic/Scripts`.
   - Cẩn thận kéo thả các script chức năng cốt lõi (TimeManager, ActionManager) vào `_Project/Scripts/Core`.
   - Kéo thả các script liên quan đến Quest vào `_Project/Scripts/Quests`.
   - Chuyển `FirebaseManager.cs` vào `_Project/Scripts/Firebase`.
2. **Gom Scenes:**
   - Chọn tất cả các file Scene hiện tại, kéo ném vào thư mục `_Project/Scenes/`.
3. **Gom bên thứ 3 (ThirdParty):**
   - Di chuyển các SDK như `Convai`, `Oculus`, `uLipSync`, `Firebase` vào mục `ThirdParty`.
4. **Gom Asset Kits:**
   - Đưa các folder đồ họa mua hoặc tải ngoài (`Food Pack`, `jewelry_shop`...) vào thư mục `ImportedAssets`.

### 🔄 Bước 4: Refactor Code (Namespaces) - Làm bằng VS Code / Rider
Khi file vật lý đã dọn gọn gàng, ta cần cho Code gọn theo.
Nếu trước đây file nằm trong `namespace Dajunctic.Scripts.Manager`, giờ ta đổi sang `namespace VRAutism.Core` hoặc `Project.Core`.
1. Dùng tính năng *Find and Replace in Files* của IDE hoặc phím tắt `Ctrl + R, R` (Visual Studio) / `F2` để đổi tên Namespace hàng loạt.
2. Kiểm tra lại các từ khóa `using ...` ở trên cùng các file để tham chiếu lại cho đúng.
3. Quay lại Unity → Đợi Unity Compile Code → Xóa các thư mục cũ rỗng (`Dang`, `Dajunctic`).

---

## 🛠️ 3. Xử lý Lỗi Sự cố (Troubleshooting)

| Dấu hiệu Lỗi | Nguyên nhân | Cách khắc phục |
| --- | --- | --- |
| **Báo lỗi `Type or namespace could not be found`** | Bạn đổi namespace nhưng quên cập nhật các file đang `using` file đó. | Mở IDE, tìm file báo lỗi và tự thêm dòng `using [Namespace_Mới];` lên đỉnh file. |
| **Object trong Inspector hiển thị `Missing (Mono Script)`** | Chuyển file Script bên ngoài Explorer làm Unity mất file `.meta` theo dõi. | Chọn object bị lỗi, trong ô Script kéo thả lại thủ công script tương ứng (mất thời gian). Nếu nhiều quá, dùng `Git checkout` phục hồi lại bản ban đầu. |
| **Missing Prefab (Màu hồng/xanh lá)** | Kéo GameObject ra khỏi đường dẫn gốc trong khi script tải chúng bằng hàm `Resources.Load()`. | Đảm bảo các file trong thư mục tên là `Resources` vẫn nằm đúng trong thư mục `Resources` ở vị trí mới (ví dụ `_Project/Resources/Prefabs`). Cập nhật url path nội bộ trong script. |

## 💡 Lời Khuyên 
Hãy **Refactor làm nhiều đợt commit** trên git.
Ví dụ:
1. Tạo folder + dọn rác -> `Commit`
2. Di dời Script Manager -> `Test Play` -> `Commit`
3. Di dời Scene phần cứng -> `Test Play` -> `Commit`
Hỏng ở đâu ta lùi lại ở đó, rất an toàn hạn chế mệt mỏi!
