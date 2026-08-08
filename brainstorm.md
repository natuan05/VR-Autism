# Brainstorm & Thảo Luận Quy Trình Bài Tập Giao Tiếp (Voice Quest)

> **TÀI LIỆU SINGLE SOURCE OF TRUTH (NGUỒN SỰ THẬT DUY NHẤT)**  
> Mọi thống nhất về quy trình, kiến trúc dữ liệu và xử lý tình huống thực tế cho bài học Giao tiếp / Quest System đều được lưu trữ và cập nhật tại đây.

---

## 1. Cấu Trúc Dữ Liệu Thực Tế Từ Firestore (Authoritative Lesson Schema)





---

## 2. QUYẾT ĐỊNH KIẾN TRÚC THỐNG NHẤT (CONFIRMED 5-POINT ARCHITECTURE)

> [!IMPORTANT]
> **5 TRỤ CỘT KIẾN TRÚC ĐÃ THỐNG NHẤT VỚI USER**:
> 
> 1. **WebRTC POV Camera Stream**: Refactor toàn bộ luồng P2P cũ sang sử dụng **LiveKit SDK** (`livekit-sdk-unity` trên Unity và `@livekit/components-react` trên Web).
> 2. **Lệnh Can Thiệp Lời Nói Giáo Viên (`PlayNPCScript`)**: Chuyển sang phát trực tiếp qua kênh **LiveKit WebRTC Stream**, loại bỏ hoàn toàn cơ chế `DownloadAndPlayVoice` tải file MP3 qua HTTP cũ.
> 3. **Tải Trước Âm Thanh Bài Học (`DownloadVoice`)**: Chuyển đổi cơ chế `DownloadAndPlayVoice` cũ thành `DownloadVoice` để tải trước toàn bộ file `.mp3` của `default_phrases` từ Firebase Storage về mảng `ReminderData[]` trong bộ nhớ RAM Kính VR khi Load Scene.
> 4. **Xử Lý Bài Tập Giao Tiếp (`VoiceQuest`) Qua LiveKit & Gemini 2.5 Flash**:
>    - Khi câu trả lời của trẻ đạt yêu cầu (`matched: true`): Chuyển thẳng sang Quest tiếp theo ngay lập tức, không cần NPC phản hồi rườm rà.
>    - Khi câu trả lời không đạt / lạc đề (`matched: false`): LiveKit Agent sử dụng **Gemini 2.5 Flash LLM** để đánh giá ngữ cảnh và stream âm thanh câu bắc cầu (TTS) về Kính VR để lôi kéo trẻ về bài học.
> 5. **Nút Phát Gợi Ý (`VerbalHint`)**: Bốc ngẫu nhiên 1 câu âm thanh từ mảng `ReminderData[questId]` đã tải sẵn để phát ngay tức thì (**0ms độ trễ, độc lập mạng**).

---

### 🏗️ Sơ Đồ Kiến Trúc Tích Hợp LiveKit Mới

```
                       ┌─────────────────────────────────────────────────────────┐
                       │          LIVEKIT AGENT SERVER (Python / Node.js)        │
                       ├─────────────────────────────────────────────────────────┤
                       │ • LiveKit Agent Worker                                  │
                       │ • Silero VAD + Turn-Taking + Interruption Handling      │
                       │ • STT (Whisper/Deepgram) ➔ 100% Gemini 2.5 Flash Engine  │
                       │ • Function Calling `complete_quest()` + Dynamic TTS      │
                       └────────────────────────────┬────────────────────────────┘
                                                    │
                 ┌──────────────────────────────────┴──────────────────────────────────┐
                 │ WebRTC Room: "lesson_session_123" (Unified Video POV + Audio Stream)│
                 └──────────────┬─────────────────────────────────────┬────────────────┘
                                │                                     │
                                ▼                                     ▼
 ┌──────────────────────────────────────────────┐     ┌──────────────────────────────────────────────┐
 │          KÍNH VR (Unity Client)              │     │             WEB DASHBOARD (React)            │
 │             (VR-Autism Repo)                 │     │                (VRA-web Repo)                │
 ├──────────────────────────────────────────────┤     ├──────────────────────────────────────────────┤
 │ • `livekit-sdk-unity` package                │     │ • `@livekit/components-react` package        │
 │ • Stream Video POV Camera lên LiveKit Room   │     │ • Xem Stream POV Camera & Nghe Audio mượt mà │
 │ • `VoiceQuest.cs` gửi RPC chọn Quest active  │     │ • Can thiệp thoại / Phát lệnh từ xa ngay     │
 │ • Nhận Data Packet `matched: true` ➔ Advance │     │   trong cùng phòng WebRTC LiveKit            │
 └──────────────────────────────────────────────┘     └──────────────────────────────────────────────┘
```

---

### 🔄 Luồng Vận Hành Bài Học Chi Tiết Với LiveKit SDK & Gemini 2.5 Flash

```
 [Giọng trẻ nói trong VR] ➔ [LiveKit WebRTC Stream]
                                     │
                                     ▼
                      [LiveKit Agent: Silero VAD] (Phát hiện dứt câu)
                                     │
                                     ▼
                      [STT Whisper] (Chuyển thành Text thô)
                                     │
                                     ▼
              ┌──────────────────────────────────────────────┐
              │      100% GEMINI 2.5 FLASH LLM EVALUATION    │
              ├──────────────────────────────────────────────┤
              │ • Nhận toàn bộ Context: Quest hiện tại,      │
              │   default_phrases & chuỗi văn bản của trẻ    │
              │ • Phân tích ý định tự nhiên, chấp nhận       │
              │   mọi biến thể ngữ nghĩa / ngập ngừng        │
              └──────────────────────┬───────────────────────┘
                                     │
         ┌───────────────────────────┴───────────────────────────┐
         ▼                                                       ▼
 [TRẺ TRẢ LỜI ĐÚNG / ĐỦ Ý ĐỊNH]                         [TRẺ NÓI LAN MAN / LẠC ĐỀ]
 • Gemini kích hoạt Function Call `complete_quest()`     • Gemini tự sinh câu bắc cầu khéo léo
 • LiveKit bắn Data RPC `matched: true` về Unity         • LiveKit stream âm thanh TTS câu bắc cầu
 • Kính VR chuyển ngay sang Quest tiếp theo!               về loa NPC để kéo trẻ về bài học!
```

---

## 3. PHÂN TÁCH RÕ RÀNG 2 TÍNH NĂNG ÂM THANH NPC

### 🔹 TÍNH NĂNG 1: Hệ Thống Auto Reminder Trong Bài Học (Nạp Vào `ReminderData`)
- **Mục đích**: Phục vụ việc tự động mở lời và tự động nhắc nhở khi trẻ im lặng quá lâu trong VR mà không cần giáo viên can thiệp thủ công.
- **Trách nhiệm sinh âm thanh (AUTOMATED CLOUD FUNCTION)**:
  - Cả Web App lẫn App Unity VR đều không cần chạy code sinh TTS nào ở Runtime.
  - Ngay khi một Bài học được lưu/cập nhật trên Firestore, **Firebase Cloud Function (Background Trigger)** tự động chạy ngầm trên Cloud: đọc `default_phrases`, gọi Google Cloud TTS sinh các file âm thanh `.mp3` và lưu thẳng lên **Firebase Storage**, đồng thời lưu danh sách link `audio_urls[]` vào Firestore.
- **Quy trình nạp (`DownloadVoice`) & phát trong VR**:
  1. Khi Load Scene, Kính VR gọi `DownloadVoice` đọc các link `audio_urls[]` từ Firestore.
  2. Tải các file `.mp3` tĩnh từ Firebase Storage về và **nạp trực tiếp vào mảng `reminders[questId]` (`ReminderData`)** trong bộ nhớ Unity.
  3. Khi bấm nút `VerbalHint` hoặc đếm ngược im lặng hết giờ, Kính VR bốc một file âm thanh từ mảng `reminders[questId]` để phát ngay tức thì (**0ms độ trễ, độc lập mạng**).

---

### 🔹 TÍNH NĂNG 2: Tính Năng Can Thiệp Từ Xa Của Giáo Viên Qua Web (`PlayNPCRemoteScript`)
- **Mục đích**: Phục vụ cho Giáo viên / Nhà trị liệu đang theo dõi màn hình VR trên Web Dashboard và muốn **chủ động gửi kịch bản/lời nói tùy chỉnh bất kỳ** xuống cho trẻ.
- **Quy trình vận hành**:
  1. Giáo viên gõ/chọn một câu nói tùy chỉnh trên Web Dashboard.
  2. Web App phát lệnh can thiệp thoại trực tiếp vào Phòng LiveKit WebRTC (`PlayNPCScript`).
  3. LiveKit Agent Server phát giọng thoại đè qua NPC để trẻ nghe thấy tức thì.

---

## 4. Tinh Gọn Lớp `NPCVoice.cs` (NPC Audio Architecture)
- **Bỏ mảng trùng lặp**: Loại bỏ `[SerializeField] private AudioClip[] audioClips;` và phương thức `PlayClipById()`.
- **Nguồn phát duy nhất cho Auto Reminder**:
  - Giữ lại trường `[SerializeField] private ReminderData[] reminders;` (mỗi phần tử tương ứng với 1 `questId`).
  - Khi cần phát âm thanh mở lời/nhắc nhở tự động cho Quest `questId`, gọi `NPCVoice.PlayRandomReminder(questId)` ➔ Lấy từ dữ liệu `ReminderData` đã nạp ở Tính Năng 1.

---

[Firestore / Child Profile] (Web Custom Phrases)
           │
           ▼ (Sync khi Load Scene)
  [SessionContext] (Lưu Dictionary Phrases theo Quest)
           │
           ▼ (VoiceQuest.OnBegin)
    [VoiceQuest] ──(SendActiveQuest)──► [LiveKit Agent]
           │
           ▼ (OnVerbalHint)
      [NPCVoice] ──(Play Audio Clip)──► [Loa NPC trong VR]

## 5. Các Tình Huống Edge Case Cần Thảo Luận (Open Issues)

### 🟢 Edge Case 1: Trẻ nhại lại lời NPC (Echolalia)
- Gemini 2.5 Flash nhận diện `user_speech` trùng 100% với câu nhắc NPC ➔ Đánh giá `matched: false`, chuyển sang gợi ý hành động/thị giác.

### 🟢 Edge Case 2: Trẻ trả lời lan man, lạc đề (Off-topic Speech)
- Gemini 2.5 Flash đánh giá `matched: false` ➔ Tự sinh câu bắc cầu khéo léo và stream audio về NPC để lôi kéo trẻ về bài học.

### 🟢 Edge Case 3: Trẻ nói ngập ngừng, dừng giữa chừng (Mid-speech Pauses)
- Silero VAD + Gemini 2.5 Flash hiểu câu trả lời lấp lửng nhưng đủ ý ➔ Cho qua Quest bình thường.

### 🟢 Edge Case 4: Trẻ im lặng hoàn toàn / Bị sững lại (Selective Mutism)
- Đếm ngược im lặng $X$ giây ➔ Kích hoạt `VerbalHint` bốc câu trong `ReminderData[questId]` phát 0ms tại chỗ.

### 🟢 Edge Case 5: Trẻ ngoảnh mặt đi chỗ khác / Không nhìn NPC (Lack of Joint Attention)
- Kính VR Head-tracking kiểm tra góc nhìn lệch $>45^\circ$ quá 5s ➔ NPC vẫy tay gọi chú ý.

### 🟢 Edge Case 6: Nhiễu tiếng phụ huynh/người lớn nhắc bài bên ngoài (Adult Prompting)
- LiveKit Noise Suppression lọc tiếng ồn xa + Gemini 2.5 Flash nhận diện cấu trúc câu của người lớn để không tính điểm oan.
