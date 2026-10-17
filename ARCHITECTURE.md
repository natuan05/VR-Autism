# ARCHITECTURE.md — Kiến Trúc Tổng Thể & Luồng Dữ Liệu

> **Mô tả & Loại thông tin cung cấp**: Tài liệu cung cấp sơ đồ cấu trúc hệ thống (System Topology), luồng dữ liệu thời gian thực (Real-time Data Flow) và các quyết định kiến trúc cốt lõi (Architectural Decisions) kết nối giữa Unity VR Client, Python Voice Agent, Next.js Web Dashboard và Firebase.

## 1. Sơ đồ Cấu trúc Hệ thống (System Topology)
- **Unity VR Client (`Assets/Project/Scripts`)**: C# Client trên Meta Quest / HTC Vive, xử lý rendering VR, vật lý tương tác, thu âm microphone và stream video góc nhìn (POV).
- **AI Voice Agent (`LiveKitAgent/src`)**: Python RTC Worker xử lý pipeline hội thoại giọng nói thời gian thực (Silero VAD -> Google STT -> Gemini LLM -> Google TTS Chirp 3-HD).
- **Web Dashboard (`d:/Lab/VRA-web/src`)**: Next.js Dashboard cho giáo viên giám sát luồng POV trực tiếp và can thiệp từ xa.
- **LiveKit Cloud RTC Room**: Hạ tầng WebRTC trung tâm truyền tải đồng thời Video POV, Âm thanh 2 chiều và Kênh gói tin điều khiển (DataPacket Bus).
- **Firebase Cloud**: Firestore lưu trữ dữ liệu vĩnh viễn (Users, Sessions, Lessons); Realtime Database (RTDB) lưu trạng thái tức thời (Mã PIN ghép nối, Telemetry).

## 2. Luồng Dữ Liệu Thời Gian Thực (Real-time Data Flow)

## 3. Các Quyết Định Kiến Trúc Cốt Lõi (Architectural Decisions)
