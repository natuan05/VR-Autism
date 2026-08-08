# LIVEKIT AI AGENT SPECIFICATION & ARCHITECTURE (VR AUTISM)

## 1. 🎯 MỤC ĐÍCH & VAI TRÒ
* **Dự án**: VR Autism — Ứng dụng VR can thiệp và luyện tập kỹ năng giao tiếp/xã hội cho trẻ tự kỷ.
* **Vai trò của Agent**: Đóng vai **Chú gấu Teddy** (Giáo viên / Bạn đồng hành ảo), trò chuyện bằng **tiếng Việt** thân thiện, nhẹ nhàng để hướng dẫn và đánh giá câu trả lời giọng nói của trẻ trong không gian VR.

---

## 2. 🏛️ CÁC THÀNH PHẦN HỆ THỐNG
1. **Unity VR App (Kính VR / Editor)**:
   * Quản lý tiến trình bài học (`QuestController`).
   * Khi đến nhiệm vụ giọng nói (`VoiceQuest`), bật Micro thu âm giọng trẻ và phát tiếng phản hồi của Agent ra loa NPC (`NPCVoice`).
2. **LiveKit Server (Cloud / Self-hosted)**:
   * Hạ tầng WebRTC truyền luồng âm thanh 2 chiều (Audio Tracks) và kênh dữ liệu thời gian thực (Data Channel / Data Packets).
3. **LiveKit AI Agent Server (`LiveKitAgent/src/agent.py`)**:
   * Chạy bằng Python LiveKit Agents SDK (`livekit-agents>=1.6.0`).
   * Đăng ký làm Worker kết nối vào phòng LiveKit, lắng nghe âm thanh từ Mic của trẻ, đánh giá ý định và gọi tool gửi tín hiệu chuyển bài về Unity.

---

## 3. 🔄 LUỒNG VẬN HÀNH THỜI GIAN THỰC (REALTIME FLOW)

```text
 ┌────────────────┐       1. Gửi Data Packet SET_ACTIVE_QUEST        ┌────────────────┐
 │                │ ───────────────────────────────────────────────► │                │
 │                │                                                  │                │
 │  Unity VR App  │ ◄─── 2. Luồng tiếng Agent chào/hướng dẫn (Audio) ──│  LiveKit Agent │
 │  (Kính VR)     │ ─── 3. Luồng tiếng trẻ trả lời bài học (Audio) ──►│  (Python Server│
 │                │                                                  │   / Gemini AI) │
 │                │       4. Gửi Data Packet QUEST_MATCHED           │                │
 │                │ ◄─────────────────────────────────────────────── │                │
 └────────────────┘                                                  └────────────────┘
```

### Chi tiết 4 bước:
* **Bước 1 (Unity Kích hoạt Quest)**:
  Khi bài học chuyển sang `VoiceQuest`, Unity mở Mic và bắn một gói tin Data Packet `SET_ACTIVE_QUEST` sang LiveKit Room.
* **Bước 2 (Agent Mở Lời)**:
  Agent nhận gói tin `SET_ACTIVE_QUEST` chứa `quest_name` (Tên nhiệm vụ) và `default_phrases` (Các câu gợi ý của giáo viên). Agent tự động cất lời mở đầu chào trẻ qua loa NPC.
* **Bước 3 (Đánh Giá Ý Định Giọng Nói)**:
  Trẻ cất lời qua Micro ➔ Âm thanh truyền về Agent. AI đánh giá xem câu nói của trẻ có thể hiện đúng ý định hoàn thành nhiệm vụ `quest_name` hay không (ví dụ: Quest *"Báo cáo đã rửa tay xong"*, trẻ nói *"con xong rồi ạ"*, *"dạ xong rồi cô"*...).
* **Bước 4 (Hoàn Thành Quest & Nhảy Bài)**:
  Nếu trẻ trả lời đúng ý định ➔ AI gọi ngay hàm tool `complete_quest()`. Hàm này bắn gói tin Data Packet `QUEST_MATCHED` về Unity ➔ Unity tự động hoàn thành Quest hiện tại và chuyển sang Quest tiếp theo.

---

## 4. 📜 HỢP ĐỒNG DỮ LIỆU (DATA PACKET CONTRACTS)

### A. Gói tin Unity ➔ LiveKit Agent: `SET_ACTIVE_QUEST`
Gửi qua kênh LiveKit Data Channel (Reliable):
```json
{
  "event": "SET_ACTIVE_QUEST",
  "quest_name": "Báo cáo đã rửa tay xong",
  "default_phrases": [
    "Con đã rửa tay xong chưa?",
    "Báo cáo cho cô nghe nào!"
  ]
}
```

### B. Gói tin LiveKit Agent ➔ Unity: `QUEST_MATCHED`
Gửi qua kênh LiveKit Data Channel (Reliable):
```json
{
  "event": "QUEST_MATCHED"
}
```

---

## 5. 🛠️ TRẠNG THÁI CODE PHÍA UNITY & AGENT

* **Code Unity (`LiveKitService.cs`)**:
  * `Connect(url, token)`: Kết nối vào phòng LiveKit.
  * `EnableMicrophone(true)`: Bật và publish luồng Mic (tần số 48000Hz).
  * `SendActiveQuest(questName, phrases)`: Gửi JSON `SET_ACTIVE_QUEST`.
  * `OnDataReceived()`: Lắng nghe JSON `QUEST_MATCHED` để kích hoạt event `OnSpeechMatched`.
  * `OnTrackSubscribed()`: Nhận luồng tiếng Agent và gán vào `AudioSource` của NPC.

* **Code Python Agent (`LiveKitAgent/src/agent.py`)**:
  * SDK: `livekit-agents>=1.6.0`, `livekit-plugins-google`.
  * Tool Function: `@llm.function_tool` đặt tên `complete_quest()`.
  * Event listener: `@ctx.room.on("data_received")` để bắt `SET_ACTIVE_QUEST`.
