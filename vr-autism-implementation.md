# VR Autism Ecosystem Implementation Plan

## Overview
Based on the existing design documents and legacy plan, this project involves upgrading an existing Unity VR application and building a new Web Dashboard to create a complete ecosystem for Autism therapy. The system connects VR headsets with a Web Dashboard via WebRTC (P2P) for low-latency video streaming and Firebase for realtime commands and data storage.

## Project Type
**WEB + VR ECOSYSTEM**
- **Web Dashboard**: New React/Next.js application.
- **VR Application**: Existing Unity C# project (requires refactoring and feature additions).

## Success Criteria
1. **VR App Refactoring**: Legacy code cleaned up, singletons standardized, namespace implemented.
2. **Database Integration**: Both applications correctly connected to Firebase (Firestore & RTDB) using the new schemas.
3. **Web Dashboard Setup**: Next.js project initialized with Role-Based access, PIN pairing, and real-time intervention UI.
4. **Co-located Live Session**: WebRTC connection successfully streaming VR POV to the Web Dashboard.
5. **Data Collection**: VR app capturing detailed telemetry, joint attention, and logging to Firestore in batch at session end.

## Tech Stack
- **Web Dashboard**: Next.js (React), TailwindCSS, Firebase SDK. (Agent: `@frontend-specialist`)
- **VR App**: Unity C#, Oculus SDK, Firebase Unity SDK, WebRTC for Unity. (Agent: `@game-developer`)
- **Backend & DB**: Firebase Authentication, Cloud Firestore, Realtime Database. (Agent: `@backend-specialist` / `@database-architect`)

## File Structure
```text
/d:/Lab/VR-Autism/
├── Assets/                 # Existing Unity VR App
├── Packages/               # Unity Packages
├── docs/                   # Existing documentation
├── web-dashboard/          # [NEW] Next.js Web Dashboard project
└── vr-autism-implementation.md # This plan
```

## Task Breakdown

### Phase 1: Foundation & Architecture Setup

**Task 1.1: Initialize Web Dashboard Project**
- **Agent**: `@frontend-specialist`
- **Skills**: `app-builder`, `frontend-design`
- **Priority**: P0
- **Dependencies**: None
- **INPUT**: `DATABASE_SCHEMA_DESIGN.md`
- **OUTPUT**: A new Next.js project at `d:\Lab\VR-Autism\web-dashboard` with Firebase configured.
- **VERIFY**: Run `npm run dev` and ensure the default page loads without errors.

**Task 1.2: Refactor Unity VR Base Architecture**
- **Agent**: `@game-developer`
- **Skills**: `clean-code`
- **Priority**: P0
- **Dependencies**: None
- **INPUT**: `CODE_REFACTOR_PLAN.md`
- **OUTPUT**: Cleaned up code with standardized Singletons, Namespaces, removed dead code, and separated Models.
- **VERIFY**: Open Unity Editor and ensure Console has no compilation errors. Play a test scene successfully.

---

### Phase 2: Web Dashboard Core Features

**Task 2.1: Authentication & Role Management UI**
- **Agent**: `@frontend-specialist`
- **Skills**: `frontend-design`, `react-best-practices`
- **Priority**: P1
- **Dependencies**: Task 1.1
- **INPUT**: `SYSTEM_ARCHITECTURE_DIAGRAMS.md` (Role Hierarchy)
- **OUTPUT**: Login page and Role-based routing (System Admin, Center Manager, Expert, Parent).
- **VERIFY**: Successfully sign in as an Expert and be routed to the Expert Dashboard.

**Task 2.2: Live Session & Remote Control UI**
- **Agent**: `@frontend-specialist`
- **Skills**: `frontend-design`
- **Priority**: P1
- **Dependencies**: Task 2.1
- **INPUT**: `WEB_DASHBOARD_IDEAS.md` (Live POV View, Remote Control commands)
- **OUTPUT**: UI panel for Live POV video stream and Firebase RTDB command buttons.
- **VERIFY**: Buttons successfully write test command payloads to Firebase RTDB.

---

### Phase 3: VR App Features

**Task 3.1: Implement Enhanced Telemetry & Quests Data**
- **Agent**: `@game-developer`
- **Skills**: `game-development`
- **Priority**: P1
- **Dependencies**: Task 1.2
- **INPUT**: `VR_APP_IDEAS.md`
- **OUTPUT**: C# scripts to track eye/head movement and batch save session data to Firestore.
- **VERIFY**: Complete a dummy session and verify a JSON payload is generated and saved correctly.

**Task 3.2: RTDB Live Command Listener & WebRTC Setup**
- **Agent**: `@game-developer`
- **Skills**: `game-development`
- **Priority**: P2
- **Dependencies**: Task 3.1
- **INPUT**: `VR_APP_IDEAS.md`, `WEB_DASHBOARD_IDEAS.md`
- **OUTPUT**: Unity listener for RTDB commands (trigger_hint, set_volume, etc.) and WebRTC sender.
- **VERIFY**: Trigger command from Web Dashboard and observe effect in VR. Check WebRTC video reception on Web Dashboard.

---

### Phase 4: Integration & Polish

**Task 4.1: PIN Pairing System Implementation**
- **Agent**: `@backend-specialist` & `@frontend-specialist`
- **Skills**: `api-patterns`
- **Priority**: P2
- **Dependencies**: Phase 2 and 3 mostly complete
- **INPUT**: `WEB_DASHBOARD_IDEAS.md` (PIN-Pairing)
- **OUTPUT**: VR app generates PIN on screen, Web UI accepts PIN to connect the session.
- **VERIFY**: End-to-end test of pairing a device and starting a live session sync.

---

## ✅ Phase X: Verification (Definition of Done)
- [ ] **Lint**: Run ESLint on `web-dashboard` (`npm run lint`).
- [ ] **Build**: Run Build on `web-dashboard` (`npm run build`) - Must succeed!
- [ ] **Unity Compilation**: Unity Editor Console must have 0 red errors.
- [ ] **Security (Python)**: `python .agent/skills/vulnerability-scanner/scripts/security_scan.py .`
- [ ] **UX Audit (Python)**: `python .agent/skills/frontend-design/scripts/ux_audit.py ./web-dashboard`
- [ ] **End-to-End Test**: Run a complete lesson flow from pairing -> lesson -> data recording.
- [ ] **No Template/Purple**: UI design has original branding, no generic templates, no purple hex colors.
- [ ] **Socratic Gate**: Agent ensured user confirmed UI designs and logic before executing in full.

*Date: 2026-03-24*
