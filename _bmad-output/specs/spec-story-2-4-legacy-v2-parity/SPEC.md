---
id: SPEC-story-2-4-legacy-v2-parity
companions:
  - delivery-phases.md
  - ../../planning-artifacts/epics.md
  - ../../implementation-artifacts/epic-2-context.md
sources: []
---

# Story 2.4: Legacy/V2 parity before and after Epic 3

## Why

Lesson Graph V2 must fully replace required ActionManager and old Quest behavior without breaking shared Quiz/Exploration consumers or historical records. Comparing the runtimes before Epic 3 exposes missing behavior and dependencies early; final gap closure and controller retirement follow Epic 3. Working voice, remote controls, and lesson-flow telemetry do not establish complete parity for profiles, hints, VR presentation, sensors, or lesson/session lifecycle.

## Capabilities

- **CAP-1**
  - **intent:** Maintainers can identify required behavior and ownership differences before implementing Epic 3.
  - **success:** An evidence-backed parity matrix covers the complete inventory in Story 2.4 of the epics companion; every missing, partial, different, or unverified item has a required outcome, owner, task/dependency, and acceptance check. The pre-Epic-3 inventory exit conditions in delivery-phases.md are met.
- **CAP-2**
  - **intent:** The completed Lesson Graph system delivers required legacy behavior through coordinated Epic 3 dependencies and remaining Story 2.4 work.
  - **success:** Required advanced-flow dependencies are assigned to specific Epic 3 stories; after Epic 3 completes, the matrix is reassessed and every required gap is implemented and verified or resolved by an explicit user-approved behavior decision. Unfinished dependencies and unverified required behavior block replacement readiness.
- **CAP-3**
  - **intent:** Each session has one authoritative owner for runtime work and observation across transitions and teardown.
  - **success:** Ownership is inventoried before Epic 3 and verified at final acceptance for timing, LiveKit/media, commands, sensor targets, state/persistence, delayed events, and shared Quiz/Exploration dependencies, meeting the adopted ownership criteria.
- **CAP-4**
  - **intent:** Therapists retain correct live and historical reporting throughout the migration.
  - **success:** Agreed schema, timing, status, hint, and sensor associations render correctly without duplicate writes, lost history, or legacy/V2 overwrites. Access-rule, session/run correlation, reconnect/exit, and shared web-presentation criteria pass before cutover.
- **CAP-5**
  - **intent:** Maintainers can retire ActionManager and the obsolete gameplay Quest stack after V2 fully replaces required behavior.
  - **success:** Epic 3 and required migration dependencies are complete; migrated lessons pass with legacy controllers disabled; cutover is accepted and references/consumers are migrated. Only then are obsolete controllers removed, with representative Lesson Graph, Quiz/Exploration, Firebase/web, and historical-reader regressions passing again.

## Constraints

- Execute the phases in delivery-phases.md: inventory before Epic 3; Epic 3 dependency delivery; remaining gap closure, replacement acceptance, and retirement after Epic 3. Audit completion and Epic 3 completion alone do not mark Story 2.4 done.
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

Before Epic 3, the parity matrix and dependency handoff are actionable. After Epic 3 and remaining gap closure, representative migrated lessons run from launch through return to lobby with ActionManager and old Quest controllers disabled, preserving required behavior, sensor association, session data, and web presentation. Following accepted cutover, removal leaves the representative regression matrix and historical readers passing with Quiz/Exploration and their shared managers retained.
