# V2 NPC Audio Routing Addendum: Single Agent & Dynamic Audio Routing

**Owner:** Epic 2, Architectural Pivot  
**Status:** Approved architectural revision  
**Supercedes:** Multi-agent participant-per-NPC routing assumption  
**Related Review Findings:** ADV-01, ARCH-PIVOT-01  

---

## 1. Architectural Problem & Pivot Rationale

### The Multi-Agent Pitfall
The initial V2 design assumed that each NPC in a lesson scene would correspond to an independent LiveKit Agent participant (`participant.Identity == npc_binding_id`), each publishing its own WebRTC audio track. Analysis revealed three critical architectural flaws in this approach:
1. **Resource & Infrastructure Waste:** Running multiple containerized agent processes per room consumes unnecessary CPU, memory, and concurrent WebRTC connections.
2. **STT & Evaluation Collision (Race Condition):** If multiple agents listen to the child's microphone audio track, all agents execute concurrent Speech-to-Text (STT) and LLM phrase evaluation. This multiplies API token costs by $N$ and introduces dangerous race conditions where competing agents emit conflicting `QUEST_MATCHED` or `QUEST_STATUS` signals.
3. **Pedagogical Inconsistency:** In autism therapy VR, lesson interactions are strictly sequential (*turn-taking*). Children with ASD require predictable, non-competing auditory cues to avoid sensory overload. NPCs never speak concurrently.

### The Adopted Architecture
We adopt a **Single-Agent Topology with Voice Profile Switching and Dynamic Audio Routing**:
- **One Central Agent:** Exactly one LiveKit Voice Agent participant operates in the room. It acts as the unified "classroom brain," handling microphone intake, quest phrase evaluation, and spoken output for all characters.
- **Voice Profile Registry:** The Python agent maintains a profile registry mapping `npc_binding_id` to distinct TTS voice configurations (voice ID, pitch, rate). Spoken output switches voice profile dynamically per request.
- **Dynamic Unity Audio Routing:** The agent publishes a single WebRTC audio track. Unity's `LiveKitService` dynamically re-routes the target `AudioSource` of this single stream to whichever NPC is currently designated by `npc_binding_id`.
- **Unified Voice Quest Binding:** `QuestNodeConfig` and `VoiceQuestSourceV2` explicitly carry `npc_binding_id`, allowing specific scene NPCs to deliver opening prompts, verbal hints (`VERBAL_HINT`), and automatic reminders (`ON_REMINDER`).

---

## 2. Core Architectural Invariants

1. **Room Topology (1 Agent, 1 Audio Track):**
   - The room contains exactly one LiveKit Voice Agent participant.
   - The agent publishes exactly one audio track representing the current active speaker.

2. **Decoupled Data Routing Identity (`npc_binding_id`):**
   - The route identity is data (`npc_binding_id`), never a serialized Unity object reference or room participant identity.
   - Dialogue nodes and Voice Quest nodes explicitly declare the target `npc_binding_id`.

3. **Fallback Policy:**
   - If a request specifies an `npc_binding_id` that is not found in the Python Voice Profile Registry, the agent logs a warning and falls back to the configured default voice profile (e.g., standard teacher voice).
   - If Unity receives an audio stream for an `npc_binding_id` not yet bound to an `AudioSource`, the route is marked pending until registration. If no V2 routes are registered, it logs a warning and falls back safely to the primary scene source without throwing or dropping stream buffers.

4. **Microphone Device Exclusivity:**
   - `LiveKitService.cs` remains the sole capturer of the user microphone in Unity.

---

## 3. Acceptance Criteria

### A. Python Agent Voice Profile Switching
- **Given** a `SPEAK_SCRIPT`, `SET_ACTIVE_QUEST`, `VERBAL_HINT`, or `ON_REMINDER` packet with a valid `npc_binding_id`
- **When** the agent prepares speech synthesis
- **Then** it selects the matching `VoiceProfile` from the registry and synthesizes audio using that voice.
- **Given** an unknown or empty `npc_binding_id`
- **When** speech is requested
- **Then** the agent logs a descriptive warning and falls back to the default `VoiceProfile` without halting or failing the node.

### B. Unity Dynamic Audio Routing
- **Given** an active remote audio stream from the single LiveKit agent
- **When** a node begins speech with a specified `npc_binding_id`
- **Then** `LiveKitService` dynamically switches the stream's destination `AudioSource` to the `AudioSource` registered for that `npc_binding_id`.
- **Given** an audio track arrives before the NPC's `NpcAudioRouteBindingV2` is enabled/registered
- **When** the component registers later
- **Then** the route is bound immediately and pending audio plays on the newly registered source.
- **Given** an NPC GameObject is disabled, destroyed, or the scene unloads
- **When** `UnregisterNpcAudioRoute` executes
- **Then** the route binding is safely detached without writing audio samples to destroyed Unity objects or throwing exceptions.

### C. Voice Quest NPC Integration
- **Given** a Voice Quest node in the graph
- **When** authored in editor or executed at runtime
- **Then** it specifies an optional or required `npc_binding_id`.
- **Given** an active Voice Quest triggers `VERBAL_HINT` or `ON_REMINDER`
- **When** the event packet is dispatched to LiveKit
- **Then** it carries the quest's `npc_binding_id`, causing the agent to speak with the assigned NPC's voice and Unity to route sound to that NPC's 3D spatial position.

---

## 4. Non-Goals

- Running multiple agent participants concurrently in a single LiveKit room.
- Serializing `AudioSource` references into graph assets or LiveKit data packets.
- Modifying legacy ActionManager, legacy QuestController, or legacy WebRTC streaming.
- Permitting simultaneous overlapping speech across multiple NPCs (turn-taking remains invariant).
- Altering `TouchQuestSourceV2` or `HoldTouchQuestSourceV2` behavior, or forcing verbal speech/opening prompts onto non-voice quests. Visual hints and multi-tier automated reminder rule engines remain strictly scoped to Story 2.2 / Epic 3.
