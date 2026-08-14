# Full-Stack Architecture Refactoring Plan: VR-Autism Platform

> **Goal**: Migrate from legacy P2P WebRTC + HTTP audio downloads + offline keyword matching to a **LiveKit Unified Room** architecture across all three subsystems.

---

## 1. Architecture & Scope Overview

The VR-Autism platform comprises three interconnected subsystems that must converge on a single LiveKit WebRTC Room for all real-time communication:

```mermaid
graph TD
    subgraph Firebase["🔥 Firebase (Persistent Storage)"]
        FS[Cloud Firestore<br/>Users, Sessions, Lessons]
        RTDB[Realtime DB<br/>PIN Pairing, Telemetry]
    end

    subgraph LK["🌐 LiveKit Unified Room (lesson_session_123)"]
        VT[📹 POV Video Track]
        AT[🎙️ User Audio Track]
        DP[📡 DataPacket Bus<br/>SET_QUEST / MATCHED / HINT / REMINDER / SPEAK_SCRIPT]
        AgentAudio[🔊 Agent Audio Track<br/>TTS → NPC Speaker]
    end

    subgraph VR["🥽 Unity VR Client"]
        VRC[LiveKitService.cs]
        QC[QuestController + VoiceQuest]
        MIC[Mic & POV Camera]
        NPC[NPC AudioSource]
    end

    subgraph Agent["🤖 LiveKit Agent (Python)"]
        VAD[Silero VAD]
        STT[Google STT Chirp3]
        LLM[Gemini LLM]
        TTS[Google TTS Chirp3-HD]
    end

    subgraph Web["💻 Web Dashboard (Next.js)"]
        POV[POV Video Viewport]
        Hint[VerbalHint Button]
        Script[PlayNPCScript Input]
        Status[Quest Status Panel]
    end

    MIC -->|Publish| VT
    MIC -->|Publish| AT
    VT -->|Subscribe| POV
    AT -->|Subscribe| VAD
    VAD --> STT --> LLM --> TTS
    TTS -->|Agent Audio| NPC
    LLM -->|complete_quest()| DP
    DP -->|QUEST_MATCHED| QC
    QC -->|SET_ACTIVE_QUEST| DP
    Hint -->|VERBAL_HINT| DP
    Script -->|SPEAK_SCRIPT| DP
    QC -->|ON_REMINDER| DP
    FS -.->|Lesson Metadata| Web
    RTDB -.->|PIN Pairing & Telemetry| VR
```

### Subsystems

| Subsystem | Repository | Tech Stack | Role |
|-----------|-----------|-----------|------|
| VR Client | `D:\Lab\VR-Autism` | Unity C# + `livekit-sdk-unity` | Publishes POV video + mic audio, receives agent audio, sends/receives DataPackets |
| AI Agent | `D:\Lab\VR-Autism\LiveKitAgent` | Python + `livekit-agents>=1.6.1` | Speech evaluation pipeline (VAD→STT→LLM→TTS), tool calling, phrase caching |
| Web Dashboard | `D:\Lab\VRA-web` | Next.js + React + TailwindCSS (target: `@livekit/components-react`) | POV monitoring, remote intervention (hints/scripts), session management |

---

## 2. User Review Required

> [!IMPORTANT]
> **Breaking Change: Web Dashboard WebRTC Migration**
> The entire Web Dashboard POV streaming and remote command system will be migrated from native WebRTC + Firebase RTDB to LiveKit SDK. This changes:
> - How the teacher sees the VR POV stream (LiveKit room subscription instead of P2P)
> - How remote commands are sent (LiveKit DataPackets instead of RTDB commands)
> - Requires a LiveKit token generation API route on the web server

> [!WARNING]
> **Unity Legacy Code Removal**
> Several legacy scripts will be deprecated:
> - `SpeechRecognition.cs` / `VoiceProcessor.cs` (HuggingFace HTTP ASR) — conflicts with LiveKit mic
> - `NPCRemoteBridge.cs` (HTTP audio download) — replaced by LiveKit agent audio streaming
> - `WebRTCManager.cs` / `WebRTCSignaling.cs` (P2P WebRTC) — replaced by LiveKit video tracks
> - `SpeechResponser.cs` (offline keyword matching) — replaced by server-side Gemini evaluation

> [!CAUTION]
> **Microphone Contention on VR Headset**
> Legacy `SpeechRecognition.cs` and LiveKit both try to capture the mic via `Microphone.Start()`. They **cannot coexist**. Legacy scripts must be fully disabled before LiveKit mic publish is enabled.

---

## 3. Open Questions

> [!IMPORTANT]
> 1. **POV Video Resolution & FPS**: What resolution and framerate should the VR camera publish to LiveKit? (720p@30fps is recommended for bandwidth balance). Does the existing `WebRTCStreamer.cs` capture at a specific resolution?
> 2. **LiveKit Cloud vs Self-Hosted**: The agent `.env` references `wss://vra-9jrt51dr.livekit.cloud`. Will the POV video also route through LiveKit Cloud, or should it use a self-hosted SFU for LAN optimization?
> 3. **Firebase RTDB Commands Deprecation Timeline**: Should we keep RTDB commands as a fallback for non-LiveKit features (pause_lesson, set_volume, skip_quest for non-voice quests), or migrate everything to DataPackets?
> 4. **Telemetry Data Path**: Behavior telemetry (head/hand kinematics) currently uses Firebase RTDB at 2s intervals. Should this remain on RTDB or also move to LiveKit DataPackets?

---

## 4. Full Codebase Audit & Gap Analysis

### 4.1 Unity VR Client (`D:\Lab\VR-Autism\Assets\Project\Scripts\`)

#### ✅ Existing & Compatible (KEEP)

| File | Class | Purpose | Status |
|------|-------|---------|--------|
| [LiveKitService.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs) | `LiveKitService` | Room connection, mic publish, audio subscribe, DataPacket send/receive | ✅ Aligned |
| [VoiceQuest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs) | `VoiceQuest` | Activates mic, sends `SET_ACTIVE_QUEST`, handles `QUEST_MATCHED` | ✅ Aligned |
| [QuestController.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs) | `QuestController` | Quest sequencer, hint timers, completion tracking | ✅ Aligned |
| [SessionContext.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Manager/SessionContext.cs) | `SessionContext` | Session data, dynamic phrases by quest index | ✅ Aligned |
| [FirebaseManager.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/FirebaseManager.cs) | `FirebaseManager` | Firestore batch write for session logs | ✅ Keep |
| [Quest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs) | `Quest` (base) | Abstract quest base class | ✅ Keep |
| [TouchQuest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/TouchQuest.cs) | `TouchQuest` | Physical touch quests | ✅ Keep |
| [HoldTouchQuest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs) | `HoldTouchQuest` | Hold-touch quests | ✅ Keep |
| [NPCVoice.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Entities/NPC/NPCVoice.cs) | `NPCVoice` | NPC audio source management | ✅ Keep |
| [QuestRemoteBridge.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestRemoteBridge.cs) | `QuestRemoteBridge` | Routes remote commands to QuestController | ✅ Keep |
| [RemoteCommandListener.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs) | `RemoteCommandListener` | Listens to RTDB commands | ✅ Keep (for non-LiveKit commands) |
| [LiveSessionReporter.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs) | `LiveSessionReporter` | Reports VR state to RTDB | ⚠️ Requires refactor (remove WebRTCManager trigger) |
| [PairingManager.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs) | `PairingManager` | PIN pairing state machine | ✅ Keep |
| [SensorHarvester.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs) | `SensorHarvester` | Kinematic data collection | ✅ Keep |
| [TelemetryStreamer.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs) | `TelemetryStreamer` | Telemetry upload cycle | ✅ Keep |

#### 🔧 Requires Refactoring (MODIFY)

| File | Issue | Required Change |
|------|-------|----------------|
| [LiveKitService.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs) | Missing POV video publish | Add `RtcVideoSource` + camera capture + `LocalVideoTrack` publish |
| [LiveKitService.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs) | Missing VERBAL_HINT/SPEAK_SCRIPT/ON_REMINDER DataPacket handling | Add DataPacket receive handlers + dispatch events |
| [LiveSessionReporter.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs) | Triggers legacy `WebRTCManager` | Replace with `LiveKitService` connection trigger |
| [VoiceQuest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs) | `OnVerbalHint()` only logs phrases locally | Should send `VERBAL_HINT` DataPacket to agent via LiveKit |

#### 🆕 Missing Components (NEW)

| Component | Purpose | File to Create |
|-----------|---------|---------------|
| POV Video Publisher | Capture VR camera and publish as LiveKit video track | Extend `LiveKitService.cs` with `EnablePOVCamera()` |
| LiveKit Token Provider | Generate LiveKit access tokens for VR client | Can use hardcoded test tokens or add a token endpoint |
| DataPacket Router | Centralized handler for incoming DataPackets with event dispatch | Add to `LiveKitService.cs` |

#### ❌ Obsolete / Redundant (DELETE or DISABLE)

| File | Class | Reason |
|------|-------|--------|
| [WebRTCManager.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs) | `WebRTCManager` | Legacy P2P WebRTC replaced by LiveKit video tracks |
| [WebRTCSignaling.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/WebRTCSignaling.cs) | `WebRTCSignaling` | Legacy RTDB signaling for P2P |
| [WebRTCStreamer.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/WebRTCStreamer.cs) | `WebRTCStreamer` | Old POV capture for P2P |
| [SpeechRecognition.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Player/Player/SpeechRecognition.cs) | `SpeechRecognition` | HuggingFace HTTP ASR — mic contention with LiveKit |
| [VoiceProcessor.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Player/Player/VoiceProcessor.cs) | `VoiceProcessor` | Offline ASR variant — same conflict |
| [SpeechResponser.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Player/Player/SpeechResponser.cs) | `SpeechResponser` | Offline keyword matching — now server-side via Gemini |
| [NPCRemoteBridge.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Entities/NPC/NPCRemoteBridge.cs) | `NPCRemoteBridge` | HTTP audio download (`DownloadAndPlayVoice`) — replaced by LiveKit agent audio stream |

---

### 4.2 LiveKit Agent Server (`D:\Lab\VR-Autism\LiveKitAgent\src\`)

#### ✅ Existing & Compatible (KEEP)

| Component | File | Status |
|-----------|------|--------|
| Agent entrypoint & worker registration | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| `SET_ACTIVE_QUEST` handler | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| `complete_quest()` tool function | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| `QUEST_MATCHED` DataPacket sending | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| VAD (Silero, 0.5s silence) | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| STT (Google Chirp3, vi-VN) | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| LLM (Gemini 3.5 Flash Lite) | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| TTS (Google Chirp3-HD, OGG Opus) | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| Phrase pre-synthesis caching | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| `AGENT_INIT_FAILED` error handling | [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py) | ✅ Working |
| Tests (LLM-as-judge) | [test_agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/tests/test_agent.py) | ✅ Working |

#### 🆕 Missing Components (NEW — to add in `agent.py`)

| Feature | DataPacket | Direction | Description |
|---------|-----------|-----------|-------------|
| **VERBAL_HINT / ON_REMINDER** handler | `{"event": "VERBAL_HINT"}` or `{"event": "ON_REMINDER"}` | Web/Unity → Agent | **Same behavior**: Agent picks a random phrase from `runtime.quest_state.phrases` (the currently active quest), looks it up in `_tts_cache` (keyed by phrase text), and plays it via `session.say()`. No `quest_name` param needed — the agent already knows the active quest. |
| **SPEAK_SCRIPT** handler | `{"event": "SPEAK_SCRIPT", "text": "..."}` | Web → Agent | Agent synthesizes arbitrary text via TTS and streams it to NPC |
| **QUEST_STATUS** broadcast | `{"event": "QUEST_STATUS", "quest_name": "...", "status": "active\|matched"}` | Agent → Web | Notify web dashboard of current quest state for UI sync |

---

### 4.3 Web Dashboard (`D:\Lab\VRA-web\src\`)

#### ✅ Existing & Compatible (KEEP)

| File | Purpose | Status |
|------|---------|--------|
| [middleware.ts](file:///D:/Lab/VRA-web/src/middleware.ts) | Auth session cookie verification, role-based routing | ✅ Keep |
| [page.tsx](file:///D:/Lab/VRA-web/src/app/page.tsx) (login) | Firebase Auth login | ✅ Keep |
| [layout.tsx](file:///D:/Lab/VRA-web/src/app/layout.tsx) | Root layout with AuthProvider | ✅ Keep |
| `src/actions/*.ts` | Server Actions for Firestore CRUD | ✅ Keep |
| `src/lib/firebase/client.ts` & `admin.ts` | Firebase initialization | ✅ Keep |
| `src/lib/firebase/rtdb.ts` | RTDB functions (pairing, telemetry) | ⚠️ Keep for pairing/telemetry, remove command functions |
| [StartLessonButton.tsx](file:///D:/Lab/VRA-web/src/app/dashboard/expert/lessons/_components/StartLessonButton.tsx) | Lesson start trigger | ✅ Keep (still triggers via RTDB for pairing) |
| Dashboard pages (expert, center, admin, parent) | Full dashboard UI | ✅ Keep |

#### 🔧 Requires Refactoring (MODIFY)

| File | Issue | Required Change |
|------|-------|----------------|
| [session/[id]/page.tsx](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/%5Bid%5D/page.tsx) | Uses RTDB for remote commands | Replace `pushRemoteCommand` calls with LiveKit DataPacket sends |
| [POVMonitor.tsx](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/_components/POVMonitor.tsx) | Uses native `<video>` + P2P WebRTC | Replace with `@livekit/components-react` `<VideoTrack>` component |

#### 🆕 Missing Components (NEW)

| Component | Purpose | File to Create |
|-----------|---------|---------------|
| **LiveKit Token API** | Generate viewer tokens for the web dashboard | `src/app/api/livekit-token/route.ts` |
| **LiveKit Room Provider** | React context wrapping `@livekit/components-react` | `src/components/livekit/LiveKitRoomProvider.tsx` |
| **LiveKit POV Viewer** | Subscribe to VR POV video track via LiveKit | `src/components/livekit/LiveKitPOVViewer.tsx` |
| **LiveKit DataPacket Sender** | Send VERBAL_HINT / SPEAK_SCRIPT DataPackets | `src/hooks/useLiveKitDataChannel.ts` |
| **Quest Status Receiver** | Listen to QUEST_STATUS DataPackets for UI sync | Integrate into session page |

#### ❌ Obsolete / Redundant (DELETE or DISABLE)

| File | Reason |
|------|--------|
| [useWebRTCViewer.ts](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/_hooks/useWebRTCViewer.ts) | Legacy native WebRTC viewer — replaced by LiveKit |
| [useWebRTCSignaling.ts](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/_hooks/useWebRTCSignaling.ts) | Legacy RTDB-based WebRTC signaling |
| `/api/tts/route.ts` | Server-side TTS generation — now handled by LiveKit Agent |
| RTDB `pushRemoteCommand` for `verbal_hint` / `play_npc_script` | Commands now via LiveKit DataPackets |

---

## 5. DataPacket Event Contracts (Unified Schema)

All DataPackets are sent as **reliable** UTF-8 JSON strings via LiveKit's Data Channel.

### 5.1 Unity VR → Agent (via LiveKit Room)

```json
// Activate a voice quest for the agent to evaluate
{
  "event": "SET_ACTIVE_QUEST",
  "quest_name": "Báo cáo đã rửa tay xong",
  "default_phrases": [
    "Con đã rửa tay xong chưa?",
    "Báo cáo cho cô nghe nào!"
  ]
}
```

```json
// Auto-reminder timer expired in QuestController
// No quest_name needed — agent uses runtime.quest_state.phrases
{
  "event": "ON_REMINDER"
}
```

### 5.2 Agent → Unity VR (via LiveKit Room)

```json
// Child's speech matched the quest intent
{
  "event": "QUEST_MATCHED"
}
```

```json
// Agent initialization failed
{
  "event": "AGENT_INIT_FAILED",
  "reason": "missing_gemini_key"
}
```

### 5.3 Web Dashboard → Agent (via LiveKit Room)

```json
// Teacher requests verbal hint playback
// No quest_name needed — agent uses runtime.quest_state.phrases
{
  "event": "VERBAL_HINT"
}
```

```json
// Teacher sends custom speech script
{
  "event": "SPEAK_SCRIPT",
  "text": "Nam ơi, hãy vặn vòi nước nào!"
}
```

### 5.4 Agent → Web Dashboard (via LiveKit Room)

```json
// Quest status update for web dashboard UI sync
{
  "event": "QUEST_STATUS",
  "quest_name": "Báo cáo đã rửa tay xong",
  "status": "active",
  "phrases_cached": true
}
```

---

## 6. Phased Action Plan

### Phase 1: Unity VR — POV Video Publishing (Foundation)
**Dependency**: None. This unblocks Web Dashboard POV viewing.

#### [MODIFY] [LiveKitService.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs)
- Add `EnablePOVCamera(Camera vrCamera)` method
- Create `RtcVideoSource` that captures from the VR camera's render texture
- Create `LocalVideoTrack` and publish it to the room
- Add `DisablePOVCamera()` for cleanup

#### [MODIFY] [LiveSessionReporter.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs)
- Remove reference to `WebRTCManager.StartStream()`
- Replace with `LiveKitService.Instance.EnablePOVCamera(vrCamera)`
- Update connection state reporting to include LiveKit room status

#### [DELETE/DISABLE] Legacy P2P files
- Mark `WebRTCManager.cs`, `WebRTCSignaling.cs`, `WebRTCStreamer.cs` as `[Obsolete]`
- Remove their initialization calls from `LiveSessionReporter` and `TimeManager`

---

### Phase 2: Web Dashboard — LiveKit Integration & POV Viewer
**Dependency**: Phase 1 (VR publishes video track)

#### [NEW] Install LiveKit SDK
```bash
cd D:\Lab\VRA-web
npm install @livekit/components-react livekit-client livekit-server-sdk
```

#### [NEW] `src/app/api/livekit-token/route.ts`
- Generate LiveKit access tokens using `livekit-server-sdk`
- Accept `roomName`, `participantName`, `role` (viewer vs publisher)
- Return signed JWT token
- Use env vars: `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`, `LIVEKIT_URL`

#### [NEW] `src/components/livekit/LiveKitRoomProvider.tsx`
- Wrap `@livekit/components-react` `<LiveKitRoom>` component
- Fetch token from `/api/livekit-token` on mount
- Handle connection state and error display
- Expose room context to children

#### [NEW] `src/components/livekit/LiveKitPOVViewer.tsx`
- Subscribe to the VR client's video track using `useRemoteParticipantTracks()`
- Render via `<VideoTrack>` component
- Show connection status overlay
- Fallback to placeholder image when disconnected

#### [MODIFY] [POVMonitor.tsx](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/_components/POVMonitor.tsx)
- Replace native `<video ref>` with `<LiveKitPOVViewer>`
- Remove `useWebRTCViewer` hook usage
- Keep placeholder/fallback UI

#### [NEW] `src/hooks/useLiveKitDataChannel.ts`
- Expose `sendDataPacket(event, payload)` function
- Listen for incoming DataPackets (QUEST_STATUS, etc.)
- Parse JSON and dispatch to React state

#### [MODIFY] [session/[id]/page.tsx](file:///D:/Lab/VRA-web/src/app/dashboard/expert/session/%5Bid%5D/page.tsx)
- Wrap session page in `<LiveKitRoomProvider>`
- Replace `handleTriggerVerbalHint` to use `sendDataPacket("VERBAL_HINT", {...})`
- Replace `handlePlayNPCScript` to use `sendDataPacket("SPEAK_SCRIPT", {...})`
- Add QUEST_STATUS listener for real-time quest display

#### [DELETE/DISABLE] Legacy WebRTC hooks
- `useWebRTCViewer.ts` → delete
- `useWebRTCSignaling.ts` → delete
- `/api/tts/route.ts` → delete (TTS now in LiveKit Agent)

---

### Phase 3: Agent Server — New DataPacket Handlers
**Dependency**: Phase 2 (Web sends DataPackets via LiveKit)

#### [MODIFY] [agent.py](file:///D:/Lab/VR-Autism/LiveKitAgent/src/agent.py)

**Add unified VERBAL_HINT / ON_REMINDER handler:**

> [!NOTE]
> `VERBAL_HINT` (from Web) and `ON_REMINDER` (from Unity QuestController timer) are **semantically identical**. Both mean: "play a random cached phrase from the current quest to encourage the child." The `_tts_cache` is keyed by **phrase text** (not quest name), and the active phrases live in `runtime.quest_state.phrases`.

```python
elif event_type in ("VERBAL_HINT", "ON_REMINDER"):
    if not runtime.quest_state.active or not runtime.quest_state.phrases:
        logger.warning("[DATA] %s received but no active quest.", event_type)
        return
    # Pick a random phrase from the current quest's phrases
    phrase = random.choice(runtime.quest_state.phrases)
    cached_frames = _tts_cache.get(phrase)
    if cached_frames:
        logger.info("[HINT] Playing cached phrase: %r", phrase)
        await session.say(phrase, audio=_frames_to_async_gen(cached_frames), allow_interruptions=True)
    else:
        # Fallback: live TTS if cache miss
        logger.info("[HINT] Cache miss, using live TTS for: %r", phrase)
        await session.say(phrase, allow_interruptions=True)
```

**Add SPEAK_SCRIPT handler:**
```python
elif event_type == "SPEAK_SCRIPT":
    text = payload.get("text", "")
    if text:
        logger.info("[SCRIPT] Teacher script: %r", text)
        await session.say(text, allow_interruptions=True)
```

**Add QUEST_STATUS broadcast:**
- After handling `SET_ACTIVE_QUEST`, broadcast `QUEST_STATUS` to all participants
- After `complete_quest()`, broadcast `QUEST_STATUS` with `status: "matched"`

#### [NEW] Add tests for new handlers
- Test VERBAL_HINT / ON_REMINDER plays random cached phrase from `runtime.quest_state.phrases`
- Test VERBAL_HINT / ON_REMINDER fallback to live TTS on cache miss
- Test SPEAK_SCRIPT synthesizes custom text
- Test VERBAL_HINT when no quest is active (should log warning, not crash)

---

### Phase 4: Unity VR — Enhanced DataPacket Routing
**Dependency**: Phase 3 (Agent sends new DataPacket types)

#### [MODIFY] [LiveKitService.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs)
**Enhance `OnDataReceived`:**
```csharp
// Replace simple string.Contains with proper JSON parsing
var packet = JsonUtility.FromJson<DataPacketEvent>(json);
switch (packet.@event)
{
    case "QUEST_MATCHED":
        OnSpeechMatched?.Invoke();
        break;
    case "AGENT_INIT_FAILED":
        OnAgentError?.Invoke(packet.reason);
        break;
    case "QUEST_STATUS":
        OnQuestStatusUpdate?.Invoke(packet.quest_name, packet.status);
        break;
}
```

**Add new DataPacket senders:**
```csharp
// No quest_name needed — agent already knows the active quest
public void SendVerbalHint() { /* send {"event": "VERBAL_HINT"} */ }
public void SendOnReminder() { /* send {"event": "ON_REMINDER"} */ }
```

#### [MODIFY] [VoiceQuest.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs)
- Update `OnVerbalHint()` to call `LiveKitService.Instance.SendVerbalHint()` (no args — agent uses its current quest state)
- This makes the agent speak a random cached phrase through its TTS pipeline

#### [MODIFY] [QuestController.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs)
- When auto-reminder timer fires for a `VoiceQuest`, call `LiveKitService.Instance.SendOnReminder()` (no args — agent uses its current quest state) to route through the agent

#### [DELETE] Legacy speech scripts
- Fully remove or disable: `SpeechRecognition.cs`, `VoiceProcessor.cs`, `SpeechResponser.cs`, `SpeechReminderSystem.cs`
- Remove references from any scenes/prefabs

#### [DELETE/DISABLE] Legacy NPC remote bridge
- Remove `NPCRemoteBridge.cs` (HTTP audio download)
- Remove `DownloadAndPlayVoice` coroutine and related Firebase Storage references

---

### Phase 5: Integration Testing & Polish
**Dependency**: All previous phases

#### End-to-End Flow Validation
1. **PIN Pairing**: Web Dashboard → RTDB → VR Client (unchanged, validated)
2. **LiveKit Room Join**: VR Client connects to room → Agent auto-joins → Web Dashboard connects as viewer
3. **POV Video**: VR publishes camera → Web subscribes and displays
4. **Voice Quest Flow**:
   - VR sends `SET_ACTIVE_QUEST` → Agent caches phrases, starts listening
   - Child speaks → Agent evaluates via Gemini → sends `QUEST_MATCHED` → VR advances quest
5. **Teacher Intervention**:
   - Web sends `VERBAL_HINT` → Agent picks random phrase from `runtime.quest_state.phrases`, plays from `_tts_cache`
   - Web sends `SPEAK_SCRIPT` with custom text → Agent synthesizes and streams
6. **Auto-Reminder**: QuestController timer fires → VR sends `ON_REMINDER` → Agent handles identically to VERBAL_HINT (same random cached phrase)

---

## 7. Verification Plan

### Automated Tests

```bash
# Agent server tests
cd D:\Lab\VR-Autism\LiveKitAgent
uv run pytest tests/ -v

# Web dashboard build verification
cd D:\Lab\VRA-web
npm run build
npm run lint
```

### Manual Verification Checklist

| Test | Subsystem | Expected Result |
|------|-----------|-----------------|
| VR connects to LiveKit room | Unity | Room status shows connected, participant SID logged |
| VR publishes POV video track | Unity | Track appears in LiveKit dashboard |
| Web sees POV video stream | Web | `<VideoTrack>` component renders live video |
| VR publishes mic audio | Unity | Agent receives audio, STT transcribes |
| Agent speaks opening phrase | Agent → Unity | NPC AudioSource plays cached TTS audio |
| Child answers correctly | End-to-End | Agent calls `complete_quest()`, VR receives `QUEST_MATCHED`, next quest activates |
| Child answers off-topic | End-to-End | Agent responds gently, does NOT send `QUEST_MATCHED` |
| Teacher clicks VerbalHint | Web → Agent → Unity | Agent plays random cached phrase through NPC speaker |
| Teacher sends PlayNPCScript | Web → Agent → Unity | Agent synthesizes custom text and streams to NPC |
| Auto-reminder fires | Unity → Agent → Unity | Agent plays gentle reminder after timeout |
| Web shows quest status | Agent → Web | Quest name and status update in real-time on dashboard |
| Legacy scripts disabled | Unity | No HuggingFace HTTP calls, no P2P WebRTC, no `DownloadAndPlayVoice` |
| Session end & data save | End-to-End | Firestore batch write with quest logs, response times, hint counts |
