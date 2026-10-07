---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - docs/LESSON_FLOW_ENGINE_V2_PLAN.md
  - _bmad-output/planning-artifacts/architecture.md
excludedDocuments:
  - _bmad-output/planning-artifacts/ux-design-specification.md
project_name: VR-Autism
workflowType: epics-and-stories
---

# VR-Autism Lesson Flow Engine V2 - Epic Breakdown

## Overview

This document decomposes Lesson Flow Engine V2 into implementable epics and stories. The Lesson Flow plan is the PRD-equivalent source. UX specification is excluded.

## Requirements Inventory

### Functional Requirements

FR1: Define versioned LessonGraph assets with embedded node and edge data, Phase 1 node types, and editor-time validation.
FR2: Execute a Phase 1 DAG from its entry node with priority-ordered status transitions and a single lesson completion result.
FR3: Provide an isolated Quest V2 subsystem using scene binding IDs and QuestSourceV2 components without modifying or using the legacy Actions quest stack.
FR4: Run Voice, Touch, and HoldTouch completion sources concurrently in a QuestNode; accept exactly one first completion and cancel all remaining sources.
FR5: Run blocking Dialogue nodes through LiveKit and continue only after correlated script completion or a timeout transition.
FR6: Route dashboard Skip, Verbal Hint, and Visual Hint commands only to the active V2 QuestNode and eligible active source.
FR7: Persist idempotent per-node telemetry and legacy-compatible QuestLogData; publish low-frequency graph state to RTDB.
FR8: Extend Unity LiveKit transport and Python Agent handling for activation-correlated quest activate, cancel, status, match, script, and script completion packets.
FR9: Preserve legacy ActionManager and QuestController scenes unchanged while V2 scenes use LessonGraphRunner.
FR10: Add Phase 2 support for Timeline, structured Parallel and Gate nodes, bounded loops, checkpoint resume, advanced conditions, and an editor graph surface.
FR11: Establish an authored teaching state after eligible Timeline success/Skip and support a validated no-Timeline lesson launch. Added by user scope amendment on 2026-10-03.
FR12: Launch production lessons through application-controlled runtime composition and retire the temporary Unity test installer while preserving session, telemetry and cleanup ownership. Added by user scope amendment on 2026-10-03.

### NonFunctional Requirements

NFR1: LiveKitService remains the only microphone owner.
NFR2: Every quest packet uses activation_id; stale or unknown packets cannot complete an active node.
NFR3: Quest source lifecycle is explicit, terminal transitions are idempotent, and scene unload or source loss cannot leave listeners active.
NFR4: Unity, Python Agent, RTDB, Firestore, and VRA-web contract changes are delivered together.
NFR5: V2 telemetry is idempotent and does not duplicate events during retry.
NFR6: Phase 1 rejects graph cycles; Phase 2 permits only bounded cycles.
NFR7: Runtime callbacks that alter Unity state run on the Unity main thread.
NFR8: V2 implementation includes edit-mode, play-mode, and Python contract tests.

### Additional Requirements

- New Unity code is under Assets/Project/Scripts/Gameplay/LessonGraphV2.
- LessonGraph stores binding IDs, never scene MonoBehaviour references.
- LiveKitVoiceQuestTransport implements IVoiceQuestTransport and owns packet adaptation.
- New agent transport code is LiveKitAgent/src/quest_transport.py with tests in LiveKitAgent/tests.
- Web contract consumers include VRA-web/src/hooks/useLiveKitDataChannel.ts and VRA-web/src/lib/firebase/rtdb.ts.
- GraphView is optional; use UI Toolkit if project Unity version does not support a stable GraphView path.
- Before each code-symbol edit, run GitNexus impact analysis; run detect_changes before commit.

### UX Design Requirements

None. UX specification is out of scope for Lesson Flow Engine V2.

### FR Coverage Map

FR1: Epic 1 - Versioned Phase 1 graph assets and validation.
FR2: Epic 1 - DAG runner and transitions.
FR3: Epic 1 - Isolated Quest V2 bindings and sources.
FR4: Epic 1 - First-win multi-modal QuestNode execution.
FR5: Epic 2 - Blocking dialogue transport.
FR6: Epic 2 - Active-node remote control.
FR7: Epic 2 - Session telemetry and live state.
FR8: Epic 1 - Quest activation-correlated Unity/Agent transport; Epic 2 - dialogue completion extension.
FR9: Epic 1 - Legacy isolation and regression proof.
FR10: Epic 3 - Advanced adaptive graph capabilities.
FR11: Epic 4 - Timeline handoff and validated presentation bypass.
FR12: Epic 4 - Production lesson startup and temporary installer retirement.

## Epic List

### Epic 1: Run graph-based multimodal therapy tasks
Therapist can run a V2 scene where graph transitions execute isolated Touch, HoldTouch, and Voice quest sources safely; legacy scenes remain unchanged.
**FRs covered:** FR1, FR2, FR3, FR4, FR8 (quest transport), FR9.

**Story 1.5 cross-epic handoff:** The implemented Firebase/Firestore phrase schema V2, immutable session snapshot, and cross-stack voice contract are documented in [Voice Phrases V2 Cross-Epic Handoff](../../docs/VOICE_PHRASES_V2_CROSS_EPIC_HANDOFF.md). Epic 2 and Epic 3 consume this foundation; they do not recreate legacy quick_phrases storage or phrase resolution.

### Epic 2: Guide and observe LiveKit therapy flow
Therapist can deliver blocking scripted dialogue, voice quests, remote hints or skip, and view reliable live/session progress.
**FRs covered:** FR5, FR6, FR7, FR8.

**Audio routing scope amendment:** Story 2.1 consumes the [V2 NPC Audio Routing Addendum](../../docs/VOICE_AUDIO_ROUTING_V2_ADDENDUM.md). Runtime ownership stays in LiveKitService; VoiceQuestSourceV2 supplies only stable NPC audio context through the typed transport. Epic 3 may define that context for authored advanced nodes, but does not own runtime binding.

**Compatibility and replacement follow-up:** Story 2.4 compares legacy and V2 features and observable behavior, closes required gaps, and verifies session ownership, Firebase schema, and dashboard compatibility before retiring legacy controllers. Its inventory can begin after Stories 2.2/2.3; final replacement depends on all required Lesson Graph features and migrations, including later-epic work. It does not expand Stories 2.2 or 2.3.

### Epic 3: Author adaptive advanced lessons
Lesson author can create Timeline, Parallel, Gate, condition, bounded-loop, checkpoint-resume, and graph-editor flows.
**FRs covered:** FR10.

### Epic 4: Prepare and launch production lessons safely
Therapist can play or bypass presentations while reaching the declared teaching state, and launch lessons through the application without the temporary Unity test installer.
**FRs covered:** FR11, FR12.

**Scope amendment (2026-10-03):** Story 4.1 extends Story 3.2 without changing its acceptance criteria. Story 4.2 transfers startup responsibilities before removing `LessonGraphRunnerInstaller`; it does not replace Story 2.4's legacy Action/Quest cutover. The shared [proposed spec](../specs/spec-timeline-handoff-and-bypass/SPEC.md) and its companions define the boundaries; concrete production startup APIs still require a focused design and plan.

**Scope amendment (2026-10-07):** Prioritize Story 2.4, then Story 4.2 for a stable trial build. Stories 3.4, 3.5 and 4.1 are deferred. Story 4.2 now has an independent [production startup spec](../specs/spec-production-lesson-startup/SPEC.md); the earlier shared-spec coupling is superseded. Its implementation uses the actual code, scenes and accepted launch contracts present at that time, with no Timeline handoff/bypass prerequisite.

## Epic 1: Run graph-based multimodal therapy tasks

Therapist can run a V2 scene where graph transitions execute isolated Touch, HoldTouch, and Voice quest sources safely; legacy scenes remain unchanged.

### Story 1.1: Create validated LessonGraph V2 assets

As a lesson author,
I want to create Phase 1 LessonGraph assets with valid nodes, edges, and binding IDs,
So that V2 lessons are versioned and do not directly reference scene objects.

**Acceptance Criteria:**

**Given** a new LessonGraph asset
**When** the author adds Quest, Dialogue, Wait, and Checkpoint nodes and edges
**Then** the asset stores schemaVersion, unique node IDs, entryNodeId, position, and typed configuration.

**Given** a QuestNodeConfig
**When** the author configures completion sources
**Then** it stores only completionBindingIds, timeoutSeconds, and voicePrompt and contains no legacy Quest reference.

**Given** an invalid Phase 1 graph
**When** validation runs before StartLesson
**Then** it rejects missing entry nodes, duplicate node or binding IDs, missing edge targets, empty required configuration, and cycles.

**Given** a valid graph asset with no scene loaded
**When** it is serialized
**Then** it remains serializable because scene bindings are resolved later by LessonGraphBindings.
### Story 1.2: Run validated Phase 1 graph flow

As a therapist,
I want to start a V2 lesson whose graph orchestrates nodes sequentially,
So that therapy flow transitions according to node results.

**Acceptance Criteria:**

**Given** a valid graph and valid scene bindings
**When** LessonGraphRunner.StartLesson() is called
**Then** it starts lesson time, enters entryNodeId, and emits a node-entered event.

**Given** a node returns success, skipped, timeout, or failed
**When** the runner evaluates outgoing edges
**Then** it selects the matching StatusCondition with the lowest priority and uses AlwaysCondition only as fallback.

**Given** a terminal node with no matching edge
**When** the node completes
**Then** the runner emits exactly one lesson completion result and stops active execution.

**Given** an invalid graph or source before start
**When** the runner validates it
**Then** it fails invalid_graph without activating a source or changing legacy flow.

**Given** a WaitNode becomes active
**When** its configured duration elapses, it is cancelled, or its timeout policy is reached
**Then** it returns the corresponding deterministic result and releases its pending wait work.

**Given** a Phase 1 CheckpointNode becomes active
**When** it records its configured checkpoint ID through the V2 telemetry boundary
**Then** it completes success without persisting or resuming lesson state.

**Given** automated tests run
**When** Wait success/cancellation/timeout and Checkpoint marker idempotency are exercised
**Then** focused coverage passes.

**Given** a legacy ActionManager or QuestController scene
**When** regression tests run
**Then** its behavior remains unchanged.

### Story 1.3: Bind isolated V2 quest sources to a scene

As a scene author,
I want to map graph binding IDs to QuestSourceV2 components,
So that V2 sources operate independently from legacy Quest.

**Acceptance Criteria:**

**Given** a scene with LessonGraphBindings
**When** Awake runs
**Then** the registry maps unique binding IDs to enabled QuestSourceV2 instances and reports null or duplicate IDs as validation errors.

**Given** a QuestNode requesting a binding ID
**When** the registry resolves it
**Then** it returns the enabled source or an explicit missing-binding failure.

**Given** an activated source
**When** its lifecycle changes
**Then** it only follows Inactive to Activating to Active to Completing to Completed, Cancelled, or Failed and a terminal state rejects stale completion.

**Given** a source disabled or destroyed while active
**When** the runner is still running
**Then** its `OnDisable`/scene-unload observer reports Failed(binding_unavailable) before listener cleanup and the runner cleans up the node.

**Given** V2 source code
**When** it builds
**Then** it does not import or use Gameplay.Actions classes or legacy singletons.
### Story 1.4: Complete a Touch or HoldTouch QuestNode once

As a therapist,
I want Touch and HoldTouch sources in one QuestNode to complete by first-win,
So that a child needs only one successful interaction to advance therapy safely.

**Acceptance Criteria:**

**Given** a QuestNode with valid Touch and HoldTouch bindings
**When** the executor activates the node
**Then** it activates all sources with one shared activation ID.

**Given** two sources report completion together
**When** the first completion arrives
**Then** the executor accepts exactly one QuestCompletionResult, cancels and cleans up remaining sources, and emits one NodeResult.


**Given** a source reports completion after cancellation or terminal state
**When** the event reaches the runner
**Then** it is ignored and does not transition or write duplicate telemetry.

**Given** an active Touch source
**When** the character enters its target collider
**Then** it emits success with completion_channel touch.

**Given** an active HoldTouch source
**When** the character remains in its collider for the configured duration
**Then** it emits success with completion_channel hold_touch, and exit before duration resets progress.
### Story 1.5: Provide a typed, activation-correlated LiveKit V2 quest transport

As the Lesson Graph V2 system,
I want a typed LiveKit transport boundary for voice quest activation, cancellation, and signals,
So that VoiceQuestSourceV2 communicates safely with the LiveKit Agent without owning room routing or microphone capture.

**Architecture Decision (2026-09-04):** Implement Story 1.5 on a dedicated
`LiveKitAgent/src/agent_v2.py` entrypoint, initially copied from the current
`agent.py` baseline. Keep `agent.py` and its legacy flow unchanged. All V2
voice packet handling, activation correlation, cancellation, and reconnect
behavior must be developed in `agent_v2.py` and its V2 modules. Switch
deployment to the V2 entrypoint only after V2 validation is complete.

**Acceptance Criteria:**

**Given** the V2 graph owns a VoiceQuestSourceV2 activation
**When** the source requests voice matching
**Then** it depends only on `IVoiceQuestTransport` operations for activate, cancel, and received signals
**And** V2 packets use uppercase event names, snake_case fields, and an explicit `contract_version`.

**Given** any V2 voice quest packet crosses Unity and the agent
**When** it represents activation, cancellation, match, or status
**Then** its payload contains `activation_id`
**And** Unity and Python reject unknown or stale activation IDs without advancing graph state.

**Given** LiveKit callbacks arrive on a transport callback thread
**When** a valid V2 signal is delivered to a Unity source
**Then** the adapter marshals source-state work to the Unity main thread
**And** reconnect or duplicate delivery cannot complete the source twice.

**Given** the Python LiveKit Agent receives a V2 activate or cancel packet
**When** it handles the request
**Then** it tracks only the currently active V2 activation and emits correlated match/status packets
**And** a cancelled activation cannot later emit a successful completion.
**And** its integration coverage proves Voice can first-win against concurrent Touch and HoldTouch sources.

**Given** this feature is added to the brownfield project
**When** the V2 transport is implemented
**Then** legacy LiveKit quest flow remains unchanged
**And** no component other than `LiveKitService` starts microphone capture.

**Given** automated tests run
**When** packet serialization/parsing, unknown or stale activation, cancellation, duplicate delivery, and reconnect-safe handling are exercised
**Then** each case has focused Unity or Python coverage and passes.

### Story 2.1: Run a blocking Dialogue node through LiveKit V2

As a therapist,
I want a Dialogue node to wait for agent speech playback completion,
So that lesson flow advances only after the intended spoken guidance has finished.

**Acceptance Criteria:**

The single-agent room topology, voice profile switching, dynamic AudioSource routing, and scene-unload safety criteria are defined in [V2 NPC Audio Routing Addendum](../../docs/VOICE_AUDIO_ROUTING_V2_ADDENDUM.md) and are part of this story's completion evidence.

**Given** a runner enters a Dialogue node
**When** it activates the node
**Then** it requests V2 `SPEAK_SCRIPT` through the typed transport with an `activation_id`, script/text, and required `npc_binding_id`
**And** `LiveKitService` dynamically routes the single agent audio stream to the scene `AudioSource` registered for that `npc_binding_id`
**And** it does not block the Unity main thread while waiting.

**Given** the single LiveKit Agent accepts a V2 speech request
**When** the agent prepares speech
**Then** it selects the TTS voice profile for the specified `npc_binding_id` (or logs a warning and falls back to default if unknown)
**And** when audio playback has actually finished, it emits `SPEAK_SCRIPT_DONE` correlated to the same `activation_id` only after the playback-completion capability is confirmed
**And** unsupported playback-completion capability follows the configured safe timeout/failure fallback.
**And** the runner transitions only after the matching completion signal arrives.

**Given** a `SPEAK_SCRIPT_DONE` packet is stale, duplicated, unknown, or belongs to a previously cancelled node
**When** Unity receives it
**Then** it is ignored and cannot advance the graph.

**Given** dialogue times out, is cancelled, the scene unloads, or LiveKit disconnects
**When** the node cannot finish normally
**Then** it reaches the configured fail/cancel policy and releases its transport work
**And** a later packet for that activation has no effect.

**Given** V2 dialogue support is added
**When** existing scenes and legacy lessons run
**Then** the legacy `SPEAK_SCRIPT` flow remains unchanged.

**Given** automated tests run
**When** matching completion, stale completion, voice profile selection, dynamic routing, timeout, cancellation, reconnect, and scene-unload paths are exercised
**Then** every path has focused coverage and passes.

### Story 2.2: Apply safe remote lesson commands through the V2 runner

As a therapist using the dashboard,
I want hint, skip, pause, and resume commands to reach the active V2 lesson safely,
So that remote guidance cannot act on a stale node or bypass graph ownership.

**Acceptance Criteria:**

**Given** the dashboard issues a V2 lesson command
**When** the command is sent through the agreed bridge
**Then** its contract identifies the session and, where applicable, target node and activation
**And** it routes dashboard/bridge -> LessonGraphRunner -> QuestNodeExecutor -> active source, never directly to a source.

**Given** an active VoiceQuestSourceV2 can receive a hint
**When** a valid remote hint arrives for its current activation
**Then** the executor forwards it with the active quest's `npc_binding_id` through the typed voice transport
**And** the agent speaks the hint using the voice profile of that assigned NPC
**And** it affects no inactive or non-voice source.

**Given** a valid Visual Hint targets an active V2 source
**When** its session, node, activation, and binding identity match
**Then** the executor invokes only that source's V2 visual-hint capability
**And** an unsupported source rejects it with a typed reason and no state change.

**Given** a Visual Hint is duplicate, stale, cancelled, or targets the wrong source
**When** it reaches the runner
**Then** it is ignored and telemetry records the rejection reason.

**Given** a therapist sends skip, pause, or resume
**When** the runner evaluates it
**Then** pause cancels non-pausable active work, and resume reactivates the node with a new activation ID according to the configured graph policy
**And** it records the resulting graph state
**And** all active executor/source work follows the resulting cancellation or pause semantics.

**Given** a command is duplicated, stale for its session/node/activation, invalid for the current state, or arrives before activation
**When** it is received
**Then** it is ignored without changing quest or graph state
**And** lesson telemetry records the rejection reason.

**Given** a node transitions or is cancelled
**When** an earlier command arrives later
**Then** the old command has no effect.

**Given** V2 remote control is implemented
**When** legacy lessons and remote controls run
**Then** their behavior remains unchanged.

**Given** automated tests run
**When** valid, invalid, duplicate, stale, Visual Hint, and source-cleanup command paths are exercised
**Then** focused coverage passes.

### Story 2.3: Publish isolated, idempotent V2 lesson-flow telemetry

As the session-observation system,
I want V2 lesson state and lifecycle events published reliably,
So that the dashboard and audit trail accurately show lesson progress without coupling to legacy telemetry.

**Acceptance Criteria:**

**Given** a V2 graph changes runtime state
**When** it activates, completes, transitions, times out, cancels, fails, or rejects a remote command
**Then** the V2 telemetry adapter emits a correlated lesson-flow event with session, graph/node, and activation context
**And** it remains independent from `TelemetryStreamer.cs`.

**Given** the dashboard needs current low-frequency lesson state
**When** telemetry publishes a state projection
**Then** it writes only to `live_sessions/{sessionId}/lesson_graph` on node/state changes with current node ID/type, status, checkpoint ID, and Phase 2 parallel fields where applicable
**And** no high-frequency stream is introduced.

**Given** an auditable lesson lifecycle event occurs
**When** it is persisted
**Then** the V2 telemetry adapter is the only writer of `NodeLogData` in `SessionData.node_logs`, including node ID/type/index, UTC entered/exited time, monotonic elapsed, and status
**And** a V2-owned compatibility mapper writes one required `QuestLogData` for a QuestNode without changing the legacy telemetry path.

**Given** automated telemetry model tests run
**When** NodeLogData and QuestLogData serialization, RTDB path/field mapping, retry, and dashboard compatibility are exercised
**Then** each mapping is idempotent and passes.

**Given** telemetry publish retries, reconnects, or receives duplicate callbacks
**When** the same logical event is processed again
**Then** it does not create duplicate audit events or cause an incorrect state transition.

**Given** V2 telemetry schema is consumed by the web dashboard
**When** the dashboard reads current state and events
**Then** it uses the agreed V2 contract
**And** existing legacy schema and `TelemetryStreamer.cs` behavior remain unchanged.

### Story 2.4: Reconcile legacy and Lesson Graph session ownership, feature parity, and controller retirement

As a platform maintainer,
I want to compare legacy controllers and Lesson Graph V2 across features, observable behavior, and session lifecycles, then close required gaps,
So that the completed Lesson Graph system can fully replace the legacy lesson controller stack and obsolete controllers can be removed without losing therapy behavior or breaking shared consumers and historical records.

**Scope and checkpoints:** Replace and retire `ActionManager` and the old Quest model/controller stack, including its obsolete action-specific UI, hint, and remote integrations. Quiz and Exploration retain their existing runtimes and continue using `TimeManager` and `FirebaseManager`; migrating or deleting those runtimes/managers is outside this story. First inventory the existing behavior and dependencies; then implement and verify required gaps; finally perform the validated Action/Quest cutover and removal. Record dependencies on unfinished Lesson Graph work, including Epic 3. Completing an audit or publishing a gap list alone does not satisfy the replacement gate. Keep the existing sprint tracking key `2-4-reconcile-legacy-and-lesson-graph-session-ownership`.

**Dependency clarification (2026-10-07):** Story 2.4 may continue now. Only concrete dependencies actually required by the selected trial lessons must be delivered and verified; completing the whole of Epic 3 is not a prerequisite. Evaluate 3.1/3.2 for the advanced nodes/Timeline used and 3.3 only for required structured flow. Stories 3.4, 3.5 and 4.1 are deferred; existing asset validation remains required without editor expansion. Story 4.2 follows 2.4. All parity, reference migration, accepted cutover and post-removal regression gates remain; no controller may be deleted while any remaining consumer depends on it.

**Minimum parity inventory:**

| Area | Observable behavior to compare |
| --- | --- |
| Complete lesson flow | Introduction, scripted dialogue, demonstration/timeline, practice sequence, scene actions, congratulations, ended signal, and return to lobby |
| Quest interaction | Touch, hold duration/progress/reset, voice evaluation, completion once, and cleanup on success, skip, failure, pause/resume, and scene exit |
| Profile parameters | Visual guidance, bubble hints, auto hints, reminder cycle, speech silence timeout, gaze configuration, and supported volume controls |
| Manual and automatic hints | Correct active target/NPC, phrase selection, reminder eligibility/timing, visual blinking, assigned hint sound, profile-state restoration, and hint counts |
| VR presentation | Question bubble placement/visibility, hold progress display, completion feedback, and interactable meshes remaining available |
| Behavior telemetry | Active V2 target supplied to the sensor collector; gaze/head/hand/proximity observations associated with the correct session and lesson step; target cleanup/rebinding across transitions |
| Therapist controls | Skip, verbal/visual hints, volume where supported, authoritative feedback, and additional V2 pause/resume behavior without duplicate or stale effects |
| Session data and web | Timing, node/quest results, accepted hints, lifecycle/audit events, live behavior/progress views, historical reports, schema/access rules, and reconnect/exit handling |

For each item, record the legacy owner and evidence, V2 counterpart, current difference, required outcome, implementation task/dependency, and test or manual acceptance evidence. Required legacy behavior must be preserved; any deliberate observable difference requires an explicit user-approved decision rather than silently treating it as parity.

**Acceptance Criteria:**

**Given** the legacy action controller stack and the target Lesson Graph lesson scenes
**When** their features and observable behaviors are compared
**Then** the parity matrix covers at least the inventory above, including `ActionManager`, `QuestController`, their UI/hint/scene integrations, and relevant shared consumers
**And** every missing, partial, different, or unverified behavior has evidence and a concrete implementation or verification task.

**Given** a required parity gap or dependency on later Lesson Graph work
**When** replacement readiness is assessed
**Then** its implementation task has an owner, dependency, expected behavior, and acceptance check
**And** the gap remains open until implemented and verified; audit completion and code-only evidence do not imply full replacement readiness.

**Given** visual hints and profile-based guidance in a legacy lesson
**When** their V2 equivalents are implemented and tested
**Then** the defined behavior includes legacy visual blinking, assigned hint audio, and restoration to the profile baseline after the hint
**And** only the intended visual effect changes; the interactable mesh, quest completion, and other active bindings remain unaffected.

**Given** behavior sensors operating during a V2 lesson
**When** the active quest changes, pauses/resumes, completes, or exits the scene
**Then** the collector receives the correct active target and clears or rebinds it appropriately
**And** observations retain the intended session/step association and agreed pause semantics without stale targets or duplicate collectors.

**Given** ActionManager, Lesson Graph, Quiz, and Exploration scenes
**When** their session lifecycle and dependencies are inventoried
**Then** the audit records which components own timing, LiveKit handshake, RTDB state, Firestore session writes, quest/node logs, and session completion for each scene
**And** it identifies shared uses of `TimeManager` and `FirebaseManager` that must remain available to Quiz and Exploration.

**Given** a scene launches either runtime
**When** its Unity components, LiveKit handlers, and remote-command listeners are active
**Then** only the selected runtime may advance lesson state or accept its control commands
**And** microphone capture, NPC audio routing, room join/leave, and event subscriptions have one owner and are released on scene teardown.

**Given** a session uses the legacy runtime or Lesson Graph V2
**When** it starts, logs progress, and completes
**Then** only its designated persistence path writes each owned Firestore field and RTDB subtree
**And** a whole-document legacy write cannot erase V2 fields in the same `sessions/{sessionId}` document.

**Given** reconnect, scene exit, or a new run reuses an active live session
**When** a delayed packet, callback, RTDB update, or completion arrives
**Then** session/run correlation prevents it from changing the current lesson or overwriting the newer state
**And** handshake and ended signals remain consistent for the selected runtime.

**Given** the dashboard reads historical or live sessions from either runtime
**When** a legacy record lacks V2 fields or a V2 record contains node events
**Then** it renders the correct content through an explicit runtime/schema discriminator and a shared presentation model
**And** runtime-specific branching stays at the data-adapter boundary rather than spreading across view components.

**Given** historical and current records use different timing, status, hint, or optional-field conventions
**When** they are normalized for reporting and display
**Then** the adapter documents each mapping and retains unknown or absent fields safely
**And** Firestore/RTDB access rules and queries permit only the intended readers and writers for each schema.

**Given** all required Lesson Graph features and parity tasks are complete, including dependencies delivered by later epics
**When** controller retirement is evaluated
**Then** representative migrated lessons pass end-to-end with the legacy lesson controllers disabled, covering the parity matrix, real-room voice/remote behavior, Firebase readback, and dashboard views
**And** the reviewed migration record identifies retained V2 components, migrated scenes/assets/event bindings/consumers, and continued readers for historical session records.

**Given** the parity and migration gates have passed and the cutover has been accepted
**When** obsolete legacy lesson controllers are removed
**Then** no remaining scene, prefab, asset, script, or remote/web consumer depends on those controllers or their obsolete hooks
**And** `TimeManager`, `FirebaseManager`, and shared components remain available to the existing Quiz/Exploration runtimes; Action/Quest retirement does not require migrating those runtimes or deleting their managers
**And** retained managers shed obsolete Action/Quest callbacks or type references without losing Quiz/Exploration behavior; legacy gameplay Quest models are distinct from historical `QuestLogData` and session data contracts, which remain readable
**And** the representative Lesson Graph, Quiz, Exploration, and dashboard regression matrix passes again after removal; this documentation update itself deletes no runtime code.

### Story 3.1: Author validated advanced Phase 2 graph nodes

As a lesson author,
I want to author Timeline, Parallel, Gate, Loop, and Checkpoint nodes in V2 graphs,
So that advanced therapy lessons can express structured adaptive flow safely.

**Acceptance Criteria:**

**Given** an author creates a Phase 2 LessonGraph asset
**When** configuring Timeline, Parallel, Gate, Loop, or Checkpoint nodes
**Then** each node uses an explicit typed configuration model rather than a dictionary or untyped blob
**And** the editor exposes the required configuration fields.

**Given** an advanced graph is saved or validated before execution
**When** IDs, edges, bindings, node configuration, and schema compatibility are checked
**Then** missing/duplicate IDs, invalid edges/bindings, and incomplete required configuration are reported as validation errors.

**Given** a Timeline node is configured
**When** it is authored
**Then** it contains a `PlayableAsset`, expected signal name, and timeout policy.

**Given** a VariableCondition or CompositeCondition is configured
**When** validation runs
**Then** variable values are limited to bool, int, float, or string and composite AND/OR conditions are non-empty.

**Given** a Parallel node is configured
**When** its branches are authored
**Then** its join policy is limited to `AllSuccess` or `FirstCompleted`
**And** a Gate validates named completed-branch inputs and rejects missing, duplicate, or deadlocking inputs.

**Given** a Loop node is configured
**When** validation runs
**Then** it requires both a maximum-iteration bound and an exit rule
**And** unbounded cycles are rejected.

**Given** serialized graph types are renamed or migrated
**When** an editor migration is attempted
**Then** `[MovedFrom]` preserves stable type names, the asset is duplicated/backed up and validated before save, and unsupported versions are never silently rewritten.

**Given** a Checkpoint node is configured
**When** the graph is persisted or resumed later
**Then** it has a stable checkpoint ID and resume-compatibility metadata
**And** checkpoints are valid only at quiescent node boundaries (after branch join), not while a Parallel branch is in flight.

**Given** a Phase 1 graph is loaded after the Phase 2 schema is introduced
**When** it contains only Phase 1 nodes
**Then** it remains valid and runs with its existing behavior.

**Given** automated tests run
**When** incomplete configuration, invalid edges/bindings, condition typing, empty composites, invalid policies, migration backup, and schema compatibility are exercised
**Then** focused validation coverage passes.

### Story 3.2: Execute a Timeline node from Unity signals

As the lesson runner,
I want a Timeline node to play its configured Unity timeline and complete from its configured signal,
So that authored cinematic or guided sequences transition deterministically.

**Acceptance Criteria:**

**Given** a Timeline node becomes active
**When** its `PlayableAsset` starts through the Unity Timeline path
**Then** it completes success only when the configured Timeline Signal arrives.

**Given** a Timeline Signal is unknown, duplicated, stale, or arrives after cancellation
**When** the runner receives it
**Then** it is ignored and cannot transition the graph twice.

**Given** Timeline playback times out, is cancelled, or the scene unloads
**When** cleanup runs
**Then** playback and signal listeners are released and the configured result is returned.

**Given** automated tests run
**When** correct/wrong/duplicate signals, timeout, cancellation, and scene unload are exercised
**Then** focused coverage passes.

### Story 3.3: Execute structured Parallel, Gate, and bounded Loop flow

As the lesson runner,
I want deterministic structured concurrency, branch joins, and bounded loops,
So that advanced lessons remain safe under concurrency, failure, cancellation, and retries.

**Acceptance Criteria:**

**Given** a Parallel node becomes active
**When** it starts configured branches
**Then** the parent owns child execution and each branch has a cancellation scope
**And** children never evaluate graph edges independently.

**Given** a Parallel node reaches a configured join result
**When** `AllSuccess` or `FirstCompleted` applies
**Then** the parent emits exactly one result and cancels/cleans up remaining children.

**Given** a Gate node becomes active
**When** validated named branch results are available
**Then** it performs AND/OR merge semantics without running quest or dialogue work itself
**And** invalid/missing/duplicate inputs cannot deadlock or ambiguously advance the graph.

**Given** a Loop node becomes active
**When** it iterates
**Then** the runner counts node and edge visits, enforces its positive configured maximum, and fails through its configured route when the limit is exhausted.

**Given** branch callbacks are duplicated, stale, or arrive after cancellation
**When** the parent processes them
**Then** completion remains idempotent and no stale callback can win a join.

**Given** automated tests run
**When** supported join policies, child failure/cancellation, out-of-order results, gate validation, loop exit/limit, and stale callbacks are exercised
**Then** focused runtime coverage passes.
### Story 3.4: Checkpoint and resume an advanced lesson safely

As the therapist and session system,
I want to resume an interrupted V2 lesson from a valid checkpoint,
So that a learner can continue safely without replaying stale work or using an incompatible graph.

**Acceptance Criteria:**

**Given** an advanced graph reaches a Checkpoint node
**When** its state is persisted
**Then** the snapshot includes session identity, graph identity/version, checkpoint ID, and only the state required to resume
**And** persistence is idempotent.

**Given** a resume is requested
**When** the runner loads a checkpoint snapshot
**Then** it validates graph/schema compatibility and quiescent-checkpoint eligibility before restoring any execution state
**And** it recreates active execution with new activation IDs rather than replaying old callbacks or voice completions.

**Given** the asset changed incompatibly, the snapshot is corrupt or stale, or a required binding is missing
**When** resume validation fails
**Then** the runner performs the configured safe fallback
**And** it does not partially activate sources or advance the graph.

**Given** cancellation, disconnect, or scene unload occurs during resume
**When** cleanup runs
**Then** restored executors, sources, transport requests, and pending callbacks are released
**And** no old activation can alter subsequent graph state.

**Given** this V2 resume capability is implemented
**When** legacy lessons run
**Then** no checkpoint or resume behavior is added to them.

**Given** automated tests run
**When** successful resume, schema mismatch, corrupt/stale snapshot, missing binding, duplicate resume, cancellation, disconnect, and scene-unload paths are exercised
**Then** focused coverage passes.
### Story 3.5: Author and validate graph flow through an editor surface

As a lesson author,
I want an editor surface for V2 graph nodes, edges, and validation results,
So that I can build safe advanced lesson flow without manually editing serialized data.

**Acceptance Criteria:**

**Given** a LessonGraph V2 asset is opened in the Unity editor
**When** the author uses the editor surface
**Then** they can add/remove supported nodes, configure typed node fields, connect allowed edges, select the entry node, and view validation results
**And** the implementation uses UI Toolkit unless the project Unity version verifies a stable GraphView path.

**Given** the author attempts an invalid graph edit
**When** validation runs during editing and before save/run
**Then** the surface identifies the relevant node, edge, or field and prevents invalid runtime execution.

**Given** a Phase 1 or Phase 2 graph is loaded
**When** the editor renders it
**Then** supported schema versions remain editable or are shown read-only with a clear upgrade path
**And** unsupported versions are not silently rewritten.

**Given** editor interactions change graph data
**When** undo/redo, save, reload, or domain reload occurs
**Then** graph state remains deterministic and serialized changes preserve stable IDs and typed configuration.

**Given** automated tests run
**When** graph editing, validation presentation, undo/redo, schema compatibility, and serialization paths are exercised
**Then** focused editor coverage passes.

## Epic 4: Prepare and launch production lessons safely

Added on 2026-10-03. Both stories begin in backlog; this addition does not approve implementation or promote existing stories.

### Story 4.1: Prepare Timeline handoff and support launch bypass

As a therapist,
I want a skipped presentation or a lesson launched without presentations to leave the scene ready for the next quest,
So that the learner can begin practice without watching every Timeline or pressing Skip repeatedly.

**Documents:** [Research](../implementation-artifacts/research-timeline-handoff-and-bypass-2026-10-02.md), [Spec](../specs/spec-timeline-handoff-and-bypass/SPEC.md), [Runtime contract](../specs/spec-timeline-handoff-and-bypass/runtime-contract.md), [Verification](../specs/spec-timeline-handoff-and-bypass/verification.md).

**Dependencies and limits:** Reuse Story 3.2 playback/signal semantics and existing remote/state contracts. Initial profiles apply only to standalone Timeline nodes; structured child handoff/bypass remains excluded. Confirm exact scene targets, neutral animation and prop logic in Unity. Development may use the current test scaffold; final production launch also requires Story 4.2.

**Acceptance Criteria:**

**Given** an eligible Timeline with a validated scene handoff profile and valid continuations
**When** it completes from its configured signal or receives an accepted Skip
**Then** playback/listeners are cleaned before runner-owned finalization applies the profile once
**And** normal playback returns `Success`, Skip returns `Skipped`, and both reach the declared teaching state and intended successor.

**Given** the teacher selects Bypass for a new session
**When** launch preflight validates every configured Timeline profile and route
**Then** each activated Timeline applies its profile without playback and returns `Skipped/timeline_bypass`
**And** unrelated nodes execute normally and the immutable mode cannot leak to a later session.

**Given** a Timeline has no authored profile
**When** Play, Skip or Bypass is requested
**Then** normal Play remains available, Skip is rejected before cancelling playback, and Bypass fails preflight before node execution.

**Given** cancellation/pause is accepted before completion reservation, or timeout occurs
**When** the outcome is settled
**Then** no normal handoff is applied
**And** post-reservation abort/unload or finalization failure prevents the intended next-node entry without promising rollback of partial writes.

**Given** configured LearnToAsk-V2 and Bathroom-V2 boundaries
**When** focused tests and manual normal/early-middle-late Skip/Bypass checks run
**Then** the declared state persists across Animator/physics updates and the next quest's real interaction works
**And** authoritative eligibility, routing, duplicate/stale commands and cross-stack startup/state contracts have evidence before completion.

### Story 4.2: Replace the test installer with production lesson startup

As a therapist,
I want lessons to start through the application's accepted session launch path,
So that production execution does not depend on a component created for quick Unity testing.

**Documents:** [Production startup spec](../specs/spec-production-lesson-startup/SPEC.md), [Startup boundary and retirement](../specs/spec-production-lesson-startup/startup-boundary.md), [Verification](../specs/spec-production-lesson-startup/verification.md).

**Dependencies and limits:** Schedule after required Story 2.4 parity/cutover work. At implementation time, inspect the actual code, scenes, session launch contracts and Story 2.4 outcomes before selecting concrete owner APIs and migration steps. Preserve existing session/phrase, LiveKit, telemetry, Timeline playback and readiness safeguards. Story 4.2 does not depend on Story 4.1; handoff profiles, Play/Bypass options, Demo scene routing, checkpoint recovery and editor expansion are excluded. Legacy controller cutover remains Story 2.4; preserve retained legacy persistence behavior. Reuse the current lesson selection/Start Lesson entry without a competing loader or changing pairing responsibilities.

**Acceptance Criteria:**

**Given** an accepted dashboard launch with session/lesson/launch identity
**When** required metadata is ready and the scene definition is available
**Then** one runtime owner composes the dependencies required by the inspected baseline, establishes persistence ownership and validates graph/bindings/readiness before authorizing exactly one runner activation.

**Given** a stale/duplicate launch, missing required dependency or failed startup validation
**When** startup handles it
**Then** it cannot start an unauthorized or duplicate run/writer
**And** partial composition is cleaned without silently relaxing readiness gates.

**Given** a running lesson reconnects or its scene unloads
**When** lifecycle handling runs
**Then** reconnect reattaches transport without restarting the graph
**And** unload cancels the run, releases scene references/listeners and preserves pending telemetry drain ownership.

**Given** the replacement startup path is verified
**When** the temporary installer is retired
**Then** production scenes, runner authorization, TimeManager persistence exclusion and tests no longer depend on its concrete type
**And** `LessonGraphRunnerInstaller` is removed without missing-script references or duplicate legacy/V2 persistence.

**Given** a normal production lesson launch without the installer
**When** focused startup tests and dashboard/manual lifecycle checks run
**Then** normal launch, duplicate/stale launch, readiness failure, reconnect, unload and persistence preservation have evidence without requiring any Story 4.1 configuration or tests
**And** any replacement quick-test entry uses the shared runtime composition rather than its own launch policy.
