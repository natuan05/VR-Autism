---
id: SPEC-story-2-4-legacy-v2-parity
companions:
  - ../../planning-artifacts/epics.md
  - ../../implementation-artifacts/epic-2-context.md
sources: []
---

# Story 2.4: Legacy/V2 feature parity and controller retirement

## Why

Lesson Graph V2 must preserve required therapy features and observable behavior before replacing the legacy action controller stack. A working runner and new session telemetry alone do not establish parity for profile settings, hints, VR presentation, behavior sensors, scene effects, and complete lesson/session flow. Story 2.4 closes these gaps while preserving shared consumers and historical records.

## Capabilities

- **CAP-1**
  - **intent:** Maintainers can compare every required legacy feature and observable behavior with its V2 counterpart.
  - **success:** The Story 2.4 parity matrix in the adopted epics companion covers the minimum inventory and records owners, evidence, differences, required outcomes, tasks/dependencies, and acceptance checks for every item.
- **CAP-2**
  - **intent:** The completed Lesson Graph system delivers required legacy behavior through implemented and verified equivalents.
  - **success:** Every required gap is closed with implementation and acceptance evidence, or an explicit user-approved behavior decision; unfinished later-epic dependencies remain open and prevent replacement readiness.
- **CAP-3**
  - **intent:** Each lesson session has one authoritative owner for runtime work and observation across transitions and teardown.
  - **success:** The adopted ownership criteria pass for timing, LiveKit/media, commands, sensor targets, state/persistence, stale events, and shared Quiz/Exploration dependencies.
- **CAP-4**
  - **intent:** Therapists retain correct live and historical reporting throughout the migration.
  - **success:** Agreed schema, timing, status, hint, and sensor associations render correctly without duplicate writes, lost history, or V2/legacy overwrites; access-rule and reconnect/exit checks pass.
- **CAP-5**
  - **intent:** Maintainers can retire obsolete legacy lesson controllers after V2 fully replaces the required lesson behavior.
  - **success:** Migrated lessons pass the parity matrix with legacy controllers disabled; after accepted cutover and consumer/reference migration, obsolete controllers are removed and the representative regression matrix passes again.

## Constraints

- Preserve the existing Story 2.4 ownership, schema, access-rule, dashboard, lifecycle, and shared-manager requirements in the adopted companions. Keep the sprint tracking key and backlog status until implementation begins.
- Inventory can start after Stories 2.2/2.3; final replacement depends on all required Lesson Graph work, including later epics. An audit or gap list alone does not satisfy CAP-2 or CAP-5.
- Compare observable behavior without restoring deprecated transports or violating single-microphone/single-agent ownership and synchronized cross-stack contracts.
- Retirement covers ActionManager and the old Quest model/controller stack with obsolete action-specific integrations. Quiz/Exploration retain their existing runtimes and continue using TimeManager/FirebaseManager; the parity/cutover gate applies to migrated Action/Quest lessons and requires regressions for the unchanged shared consumers.
- Preserve historical session readers and shared managers. No obsolete Action/Quest controller removal precedes accepted parity/cutover evidence.
- Retained managers may detach obsolete Action/Quest callbacks and type references while preserving their Quiz/Exploration duties. Retiring gameplay Quest models does not delete historical QuestLogData or session contracts.
- Unity compilation, focused tests, manual scene checks, and real-room acceptance are performed by the user under the project workflow.

## Non-goals

- Runtime implementation or controller deletion during this planning update.
- Expanding the current Stories 2.2/2.3 implementation or treating session-flow telemetry as completed behavior-sensor integration.
- Migrating or removing Quiz/Exploration runtimes, or replacing/deleting the TimeManager/FirebaseManager they continue to use.

## Success signal

Representative migrated lessons run from launch through return to lobby with the legacy lesson controllers disabled, preserving all required therapy behavior, sensor association, session data, and web presentation. After accepted cutover, obsolete controllers and references can be removed while the representative regression matrix and historical readers continue to pass.
