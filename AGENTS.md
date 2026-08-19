# AGENTS.md — VR-Autism Platform Engineering & Agent Rules

> **System Topology**: Multi-platform autism therapy platform comprising Unity C# VR Client (`Assets/Project/Scripts`), Python Voice Agent (`LiveKitAgent/src`), and Next.js Web Dashboard (`d:/Lab/VRA-web/src`).

---

## 1. Context & Knowledge Graph Tools (MANDATORY)

The project maintains a physical Cross-Stack Code Knowledge Graph (`repomap.json`) and compressed repository map (`REPOMAP.md`). All AI agents working in this repository MUST adhere to the following command workflow:

1. **Blast Radius Analysis Before Modifications**:
   Before modifying any class, interface, method, or network contract, run:
   ```bash
   python scripts/jit_context.py --impact "<SymbolOrContract>"
   ```
   *(Example: `python scripts/jit_context.py --impact "VoiceQuest"` or `python scripts/jit_context.py --impact "SET_ACTIVE_QUEST"`)*
   Review the reported upstream/downstream dependencies across Unity, Python, and Web before editing code.

2. **JIT Context Extraction**:
   When implementing a feature, retrieve only relevant code slices within token budget:
   ```bash
   python scripts/jit_context.py --query "<TopicOrFeature>" --budget 2000
   ```

3. **RepoMap Synchronization**:
   After creating, renaming, or refactoring classes, methods, or contracts, refresh the architecture map:
   ```bash
   python scripts/repomap_generator.py
   ```

---

## 2. Deterministic Project Boundaries

Agents must respect strict filesystem boundaries across all three subsystems:

| Subsystem | Editable Root | Prohibited / Read-Only Directories |
| :--- | :--- | :--- |
| **Unity VR Client** | `Assets/Project/Scripts/`, `Assets/Project/Scenes/` | `Library/`, `Temp/`, `obj/`, `Packages/`, `Assets/Plugins/`, `Assets/ReadyPlayerMe/`, `Assets/Samples/` |
| **Python Voice Agent** | `LiveKitAgent/src/`, `LiveKitAgent/tests/` | `LiveKitAgent/.venv/`, `__pycache__/`, `.ruff_cache/` |
| **Web Dashboard** | `d:/Lab/VRA-web/src/` | `d:/Lab/VRA-web/.next/`, `d:/Lab/VRA-web/node_modules/` |
| **Tooling & Scripts** | `scripts/`, `repomap.config.json` | Generated cache files in `scripts/__pycache__/` |

- **No Third-Party Pollution**: Never edit vendor SDKs (`UniGLTF`, `ReadyPlayerMe`, `uLipSync`). Custom logic belongs solely in `Assets/Project/Scripts/`.
- **Targeted Output**: Never write temporary scripts to Desktop or system temp folders; keep tooling scripts strictly in `scripts/`.

---

## 3. Architectural Invariants

Every agent must preserve the following architectural non-negotiables:

1. **Unified LiveKit Room Transport**:
   - Real-time POV video streaming (720p @ 30 FPS) and bidirectional microphone/NPC audio stream exclusively over LiveKit RTC room.
   - All real-time quest events (`SET_ACTIVE_QUEST`, `QUEST_MATCHED`, `VERBAL_HINT`, `SPEAK_SCRIPT`, `ON_REMINDER`, `QUEST_STATUS`) must travel via LiveKit `DataPacket` channel.
   - Legacy P2P WebRTC (`WebRTCManager.cs`) and HTTP audio downloads are strictly deprecated.

2. **Microphone Device Exclusivity**:
   - `Microphone.Start()` must only be called by `LiveKitService.cs` on the VR headset. Never introduce competing microphone capture scripts to avoid hardware contention on Meta Quest / HTC Vive.

3. **Firebase Separation of Concerns**:
   - **Cloud Firestore**: Persistent entity metadata (Users, Session logs, Curriculum lessons).
   - **Firebase Realtime Database (RTDB)**: Low-frequency telemetry, pairing PINs (`pairing_codes/{pin}`), and session states (`live_sessions/{sessionId}`).

4. **Cross-Stack Contract Synchronization**:
   - Any modification to DataPacket payload schemas or RTDB keys must be updated in tandem across C# models, Python packet handlers, and TypeScript web interfaces.

---

## 4. Workflow Automation & Quality Gates

1. **Automated Verification**:
   - Run the E2E verification suite before finalizing cross-stack refactoring:
     ```bash
     python scripts/tests/test_repomap.py
     ```
2. **Git Commit Standards**:
   - Follow Conventional Commits: `feat(...)`, `fix(...)`, `refactor(...)`, `tools(...)`, `test(...)`, `docs(...)`.
