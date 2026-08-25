# AGENTS.md — VR-Autism Platform Engineering & Agent Rules

> **System Topology**: Multi-platform autism therapy platform comprising Unity C# VR Client (`Assets/Project/Scripts`), Python Voice Agent (`LiveKitAgent/src`), and Next.js Web Dashboard (`d:/Lab/VRA-web/src`).

---

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **VR-Autism** (25489 symbols, 41111 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` (or CLI `gitnexus impact <symbolName>`) and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

## Resources & Multi-Repo Group

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/VR-Autism/context` | Codebase overview, check index freshness |
| `gitnexus://repo/VR-Autism/clusters` | All functional areas |
| `gitnexus://repo/VR-Autism/processes` | All execution flows |
| `gitnexus://repo/VR-Autism/process/{name}` | Step-by-step execution trace |
| `gitnexus group impact vr-platform <Symbol>` | Cross-repo impact between Unity VR Client & Next.js Web Dashboard |

## Skills & CLI Reference

| Task | Antigravity Skill | Claude Code Skill |
|------|-------------------|-------------------|
| Understand architecture / "How does X work?" | `~/.gemini/config/skills/gitnexus-exploring/SKILL.md` | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `~/.gemini/config/skills/gitnexus-impact-analysis/SKILL.md` | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `~/.gemini/config/skills/gitnexus-debugging/SKILL.md` | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `~/.gemini/config/skills/gitnexus-refactoring/SKILL.md` | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `~/.gemini/config/skills/gitnexus-guide/SKILL.md` | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, group CLI commands | `~/.gemini/config/skills/gitnexus-cli/SKILL.md` | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

---

## 2. Deterministic Project Boundaries

Agents must respect strict filesystem boundaries across all three subsystems:

| Subsystem | Editable Root | Prohibited / Read-Only Directories |
| :--- | :--- | :--- |
| **Unity VR Client** | `Assets/Project/Scripts/`, `Assets/Project/Scenes/` | `Library/`, `Temp/`, `obj/`, `Packages/`, `Assets/Plugins/`, `Assets/ReadyPlayerMe/`, `Assets/Samples/` |
| **Python Voice Agent** | `LiveKitAgent/src/`, `LiveKitAgent/tests/` | `LiveKitAgent/.venv/`, `__pycache__/`, `.ruff_cache/` |
| **Web Dashboard** | `d:/Lab/VRA-web/src/` | `d:/Lab/VRA-web/.next/`, `d:/Lab/VRA-web/node_modules/` |
| **GitNexus Database** | `.gitnexus/` | Managed automatically by GitNexus CLI |

- **No Third-Party Pollution**: Never edit vendor SDKs (`UniGLTF`, `ReadyPlayerMe`, `uLipSync`). Custom logic belongs solely in `Assets/Project/Scripts/`.
- **Targeted Output**: Never write temporary scripts to Desktop or system temp folders.

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
   - Run `detect_changes()` / `gitnexus detect-changes` before finalizing changes to confirm blast radius and impacted flows.
   - Refresh GitNexus index via `gitnexus analyze` after creating or restructuring major files.
2. **Git Commit Standards**:
   - Follow Conventional Commits: `feat(...)`, `fix(...)`, `refactor(...)`, `test(...)`, `docs(...)`.
