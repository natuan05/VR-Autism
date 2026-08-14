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
> 2. **Lệnh Can Thiệp Lời Nói Giáo Viên (`PlayNPCScript`)**: Chuyển sang phát trực tiếp qua kênh **LiveKit WebRTC Stream**, thay thế cơ chế `DownloadAndPlayVoice` tải file MP3 qua HTTP cũ.?
> 3. **Cache Trước Audio của Quest (``)**
> 4. **Xử Lý Bài Tập Giao Tiếp (`VoiceQuest`) Qua LiveKit & Gemini**:
>    - Khi câu trả lời của trẻ đạt yêu cầu (`matched: true`): Chuyển thẳng sang Quest tiếp theo ngay lập tức, không cần NPC phản hồi rườm rà.
>    - Khi câu trả lời không đạt / lạc đề (`matched: false`): LiveKit Agent sử dụng **Gemini 2.5 Flash LLM** để đánh giá ngữ cảnh và stream âm thanh câu bắc cầu (TTS) về Kính VR để lôi kéo trẻ về bài học.
> 5. **Nút Phát Gợi Ý (`VerbalHint`)**: Cho Agent phát lại 1 câu ngẫu nhiên đã cache 

---

### 🏗️ Sơ Đồ Kiến Trúc Tích Hợp LiveKit Mới

```
                       ┌─────────────────────────────────────────────────────────┐
                       │          LIVEKIT AGENT SERVER (Python / Node.js)        │
                       ├─────────────────────────────────────────────────────────┤
                       │ • LiveKit Agent Worker                                  │
                       │ • Silero VAD + Turn-Taking + Interruption Handling      │
                       │ • STT (Whisper/Deepgram) ➔ 100% Gemini 3.5 Flash Engine  │
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

### 🔄 Luồng Vận Hành Bài Học Chi Tiết Với LiveKit SDK & Gemini 3.5 Flash

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
              │      100% GEMINI 3.5 FLASH LLM EVALUATION    │
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

### 🔹 TÍNH NĂNG 1: Hệ Thống Auto Reminder Trong Bài Học

---

### 🔹 TÍNH NĂNG 2: Tính Năng Can Thiệp Từ Xa Của Giáo Viên Qua Web (`PlayNPCRemoteScript`)
- **Mục đích**: Phục vụ cho Giáo viên / Nhà trị liệu đang theo dõi màn hình VR trên Web Dashboard và muốn **chủ động gửi kịch bản/lời nói tùy chỉnh bất kỳ** xuống cho trẻ.
- **Quy trình vận hành**:
  1. Giáo viên gõ/chọn một câu nói tùy chỉnh trên Web Dashboard.
  2. Web App phát lệnh can thiệp thoại trực tiếp vào Phòng LiveKit WebRTC (`PlayNPCScript`).
  3. LiveKit Agent Server phát giọng thoại đè qua NPC để trẻ nghe thấy tức thì.

---

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
