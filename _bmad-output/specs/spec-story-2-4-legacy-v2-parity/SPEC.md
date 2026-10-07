---
id: SPEC-story-2-4-legacy-v2-parity
companions:
  - delivery-phases.md
  - ../../planning-artifacts/epics.md
  - ../../implementation-artifacts/epic-2-context.md
sources: []
---

# Story 2.4: Legacy/V2 parity with verified lesson dependencies

## Why

Lesson Graph V2 must fully replace required ActionManager and old Quest behavior without breaking shared Quiz/Exploration consumers or historical records. The existing inventory exposes missing behavior and dependencies; remaining gaps can be closed without waiting for every Epic 3 story. Replacement readiness requires delivery and verification of the concrete dependencies used by the migrated lessons. Working voice, remote controls, and lesson-flow telemetry do not establish complete parity for profiles, hints, VR presentation, sensors, or lesson/session lifecycle.

## Capabilities

- **CAP-1**
  - **intent:** Maintainers can identify required behavior and ownership differences before closing migration gaps.
  - **success:** An evidence-backed parity matrix covers the complete inventory in Story 2.4 of the epics companion; every missing, partial, different, or unverified item has a required outcome, owner, task/dependency, and acceptance check. The pre-Epic-3 inventory exit conditions in delivery-phases.md are met.
- **CAP-2**
  - **intent:** The completed Lesson Graph system delivers required legacy behavior through coordinated Epic 3 dependencies and remaining Story 2.4 work.
  - **success:** Required advanced-flow dependencies are assigned to specific stories for the selected trial lessons; as those dependencies are delivered and verified, the matrix is reassessed and every required gap is implemented and verified or resolved by an explicit user-approved behavior decision. Independent gaps can be closed immediately. Unfinished required dependencies and unverified required behavior block replacement readiness; completing the whole of Epic 3 is not a prerequisite.
- **CAP-3**
  - **intent:** Each session has one authoritative owner for runtime work and observation across transitions and teardown.
  - **success:** Ownership is inventoried before cutover and verified at final acceptance for timing, LiveKit/media, commands, sensor targets, state/persistence, delayed events, and shared Quiz/Exploration dependencies, meeting the adopted ownership criteria.
- **CAP-4**
  - **intent:** Therapists retain correct live and historical reporting throughout the migration.
  - **success:** Agreed schema, timing, status, hint, and sensor associations render correctly without duplicate writes, lost history, or legacy/V2 overwrites. Access-rule, session/run correlation, reconnect/exit, and shared web-presentation criteria pass before cutover.
- **CAP-5**
  - **intent:** Maintainers can retire ActionManager and the obsolete gameplay Quest stack after V2 fully replaces required behavior.
  - **success:** All concrete dependencies required by the migrated lessons are delivered and verified; migrated lessons pass with legacy controllers disabled; cutover is accepted and references/consumers are migrated. Only then are obsolete controllers removed, with representative Lesson Graph, Quiz/Exploration, Firebase/web, and historical-reader regressions passing again. Deferred or unused Epic 3 stories do not block this gate.

## Constraints

- Execute the phases in delivery-phases.md: inventory; required lesson dependency delivery/verification; remaining gap closure, replacement acceptance and retirement. Independent gap work can proceed while dependencies are verified. Neither audit completion nor completion of any epic alone marks Story 2.4 done.
- Trial-build priority is Story 2.4 then Story 4.2. Stories 3.4, 3.5 and 4.1 are deferred, not automatic prerequisites. Existing graph/asset validation remains required without requiring editor expansion. Identify any concrete unmet dependency per matrix row rather than reinstating a blanket epic gate.
- Selection of trial lessons does not waive required parity or permit deleting a controller while any remaining scene, asset or consumer still depends on it.
- Preserve the complete Story 2.4 feature/behavior, ownership, schema/access-rule, dashboard/history, lifecycle, and regression criteria in the adopted companions. This sequencing refines those criteria rather than reducing them.
- Keep tracking key 2-4-reconcile-legacy-and-lesson-graph-session-ownership. A planning-only spec update does not change its sprint status; once work starts, it remains open until the final gate passes.
- Retirement covers ActionManager, the old gameplay Quest model/controller stack, and obsolete action-specific integrations. Quiz/Exploration retain their runtimes and TimeManager/FirebaseManager; neither migration nor manager deletion is required.
- Retained managers may detach obsolete Action/Quest callbacks or type references while preserving Quiz/Exploration duties. Historical QuestLogData, session contracts, and their readers remain supported.
- Preserve LiveKit-only real-time transport, sole microphone capture ownership in LiveKitService, single-agent NPC routing, and synchronized C#/Python/TypeScript contracts; do not restore deprecated transports.
- Required observable differences need an explicit user decision. Previously accepted delivery or scoped verification waivers do not prove full replacement parity.
- User-run Unity compilation, focused tests, scene checks, and real-room acceptance remain the project workflow. Final cutover requires recorded evidence and explicit user acceptance before removal.

## Non-goals

- Runtime implementation or controller deletion during this planning update.
- Reimplementing Epic 3 advanced-node capabilities inside Story 2.4 or reopening accepted Stories 2.2/2.3 merely to deliver this spec.
- Treating session-flow telemetry as completed child behavior-sensor integration.
- Migrating/removing Quiz or Exploration, deleting their shared managers, or deleting historical data contracts.

## Success signal

The parity matrix and dependency handoff identify the capabilities actually required by the selected lessons. After those dependencies are delivered and verified and remaining gaps are closed, representative migrated lessons run from launch through return to lobby with ActionManager and old Quest controllers disabled, preserving required behavior, sensor association, session data, and web presentation. Following accepted cutover, removal leaves the representative regression matrix and historical readers passing with Quiz/Exploration and their shared managers retained. No completion of unrelated Epic 3 stories is required.
