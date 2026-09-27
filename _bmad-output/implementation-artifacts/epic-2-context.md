# Epic 2 Context: Guide and observe LiveKit therapy flow

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Enable therapists to guide a running Lesson Graph V2 session with blocking scripted dialogue, activation-scoped voice interaction, hints and flow controls, while observing reliable live progress and an idempotent audit trail. All behavior must remain owned by the V2 runner and typed LiveKit/Firebase boundaries so stale input, reconnects, retries, cancellation, or scene teardown cannot advance the wrong node or alter legacy lesson behavior.

## Stories

- Story 2.1: Run a blocking Dialogue node through LiveKit V2
- Story 2.2: Apply safe remote lesson commands through the V2 runner
- Story 2.3: Publish isolated, idempotent V2 lesson-flow telemetry

- Story 2.4 (backlog, separate follow-up): Reconcile legacy and Lesson Graph session ownership, feature parity, and controller retirement

## Requirements & Constraints

### Dialogue and voice flow

- Dialogue is blocking: graph execution continues only after a completion signal correlated to the active speech request. Actual audio playback completion must be confirmed by the LiveKit Agents capability; enqueue or synthesis return is not sufficient.
- A missing completion capability, timeout, cancellation, disconnect, or scene unload follows an explicit safe failure/timeout policy, releases transport work, and makes later callbacks ineffective.
- Dialogue/general speech stays separate from quest evaluation context. Use the immutable phrase snapshot resolved at session launch; do not read legacy `quick_phrases`, fall back to Inspector phrases, or mutate phrases during the session.
- NPC routing uses a stable `npc_binding_id` or equivalent data identity. Graphs and packets never carry Unity `AudioSource` references. A single LiveKit Agent serves the room, switching TTS Voice Profiles per `npc_binding_id` (logging and falling back to default if unknown) and allowing Unity's `LiveKitService` to dynamically route its single audio track to the active NPC's `AudioSource`. Both Dialogue and Voice Quest nodes support `npc_binding_id`.

### Remote control safety

- Dashboard commands identify the session and, when applicable, the target node, activation, and binding. They route dashboard/bridge -> `LessonGraphRunner` -> `QuestNodeExecutor` -> eligible active source; no command may address a source directly.
- Skip, pause, resume, verbal hints, and visual hints affect only matching active V2 work. Unsupported, duplicate, stale, early, cancelled, or wrong-target commands cause no state change and record a typed rejection reason.
- Pause/cancel semantics must clean up non-pausable work. Reactivation or resume creates a new `activation_id`; callbacks from an earlier activation cannot win.

### Observation and persistence

- Publish low-frequency current state only when node or lesson state changes, never as a frame-level stream.
- Persist each logical lifecycle event once across retry or reconnect. Include session, graph, node, activation, UTC timing, monotonic elapsed, status, and a stable idempotency identity.
- The V2 telemetry path is the sole writer of `SessionData.node_logs`. A V2 compatibility mapper emits exactly one legacy-compatible `QuestLogData` for each QuestNode without changing or duplicating the legacy telemetry path.
- Cross-stack contract changes ship together across Unity DTOs, the Python V2 agent/transport, RTDB/Firestore models, and VRA-web consumers. Coverage must include success, stale/duplicate delivery, cancellation, timeout, disconnect/reconnect, retry, scene unload, serialization, and dashboard compatibility.

## Technical Decisions

- Keep all real-time dialogue, quest, hint, skip, pause, resume, status, and completion events on the LiveKit DataPacket channel. Event names are uppercase, fields are snake_case, and V2 packets declare `contract_version: 2` with non-empty correlation IDs.
- `activation_id` is mandatory for activation-scoped packets. Unity and Python reject unknown or stale IDs. `agent_v2.py` and V2-specific modules own the new state machine; `agent.py` remains unchanged until a separate validated deployment cutover.
- `LiveKitVoiceQuestTransport` owns typed packet adaptation, publish/subscribe/cancel, stale filtering, and Unity main-thread dispatch. Sources do not know graph routing. `LiveKitService` remains the only microphone owner and owns remote-track lookup, deferred binding, rebind, and idempotent teardown.
- Audio routing uses a Single LiveKit Agent topology: the room contains exactly one agent publishing one audio track. The agent dynamically switches TTS Voice Profiles based on `npc_binding_id` (logging a warning and falling back to a default voice if unknown/unspecified). Unity `LiveKitService` dynamically routes/swaps this single stream's destination `AudioSource` to the active NPC registered via `NpcAudioRouteBindingV2`. Audio routing supports track-before-source, source-before-track, source replacement, unsubscribe, despawn, and scene unload without cross-talk or writes to destroyed objects.
- Runtime orchestration remains isolated under `Assets/Project/Scripts/Gameplay/LessonGraphV2/`: the runner owns graph state, executors own node work, Remote consumes runner commands, Integration owns LiveKit adaptation, and Telemetry consumes runner events. No V2 code imports or mutates the legacy Actions quest stack.
- RTDB projection is restricted to `live_sessions/{sessionId}/lesson_graph`, including current node ID/type, status, checkpoint ID, update time, and forward-compatible Phase 2 parallel fields. Firestore audit writes use idempotency keys rather than append-on-retry behavior.
- Preserve phrase-set and lesson revisions in observable launch/activation context where telemetry needs them. Effective phrases retain canonical order and are passed consistently to opening speech, verbal hints/reminders, and evaluation examples; general phrases are sent only as scripted speech.

## UX & Interaction Patterns

- The dashboard presents authoritative current lesson state rather than inferring progress from high-frequency events.
- Therapist actions are scoped to the visibly active node. Rejected or unsupported commands remain non-destructive and expose a reason suitable for operator feedback and audit.
- Blocking dialogue must not freeze the Unity main thread; progress waits asynchronously while VR rendering and input remain responsive.

## Story 2.4: Feature parity and replacement gate

- Compare legacy controllers and Lesson Graph V2 feature by feature and behavior by behavior. The minimum inventory and acceptance criteria are in [Story 2.4](../planning-artifacts/epics.md#story-24-reconcile-legacy-and-lesson-graph-session-ownership-feature-parity-and-controller-retirement); the dedicated contract is [SPEC](../specs/spec-story-2-4-legacy-v2-parity/SPEC.md).
- Cover complete lesson flow, scene actions, touch/hold/voice interaction, profile parameters, manual/automatic hints, VR bubble/progress/completion UI, behavior sensors, therapist controls, session persistence, and live/historical web presentation. Record each legacy owner, V2 counterpart, difference, implementation dependency, and acceptance evidence.
- Explicit gaps include legacy visual-hint blinking and assigned hint sound with profile-state restoration; V2 active-target propagation to behavior sensors and session/step association; bubble/progress UI; auto reminders; and end-of-lesson/return-to-lobby behavior. Inventory determines the full required set; these examples are not proof that every other feature is complete.
- Implement and verify required gaps rather than ending at an audit report. Deliberate observable differences need explicit user approval. Required features that depend on Epic 3 or later work remain open and block the final replacement gate.
- Retirement scope is `ActionManager` and the old Quest model/controller stack with its obsolete action-specific integrations. Quiz/Exploration retain their existing runtimes and continue using `TimeManager` and `FirebaseManager`; their migration or manager deletion is outside Story 2.4.
- Retained managers may shed obsolete Action/Quest callbacks or type references while preserving Quiz/Exploration duties. Gameplay Quest model retirement does not remove historical `QuestLogData` or session data contracts.
- Demonstrate migrated Action/Quest lessons with their legacy controllers disabled before removal. Preserve one owner for session/transport/control/sensor/persistence work and historical records/readers; verify existing Quiz/Exploration behavior with the shared managers retained.
- Remove obsolete controllers only after the parity/cutover gate is accepted, references and consumers are migrated, and representative Unity/real-room/Firebase/web checks pass. Rerun the regression matrix after removal. The current change updates planning documents only.

## Cross-Story Dependencies

- Consume Epic 1's validated DAG runner, isolated `QuestSourceV2` lifecycle, first-win executor, typed `IVoiceQuestTransport`, activation correlation, immutable session phrase snapshot, and stable scene binding IDs. Epic 2 extends these contracts; it does not recreate them.
- Story 2.1 establishes dialogue completion, single-agent voice profile switching, and dynamic NPC audio-routing behavior used by both dialogue and voice interactions. Story 2.2 relies on runner/executor activation ownership and routes verbal hints with `npc_binding_id`. Story 2.3 observes the resulting transitions and command rejections without becoming a control path.
- Legacy `ActionManager`, `QuestController`, legacy remote controls, `TelemetryStreamer.cs`, and legacy LiveKit speech/quest behavior must remain unchanged. Epic 3 may author NPC route identities and consume telemetry fields, but runtime audio ownership and Epic 2 contracts remain stable.
- Story 2.4 inventories ActionManager, Lesson Graph, and shared Quiz/Exploration dependencies after the V2 remote-control and telemetry work, closes Action/Quest feature/behavior gaps, and validates retirement of that legacy stack only. It retains ownership, schema/access-rule, and shared web-presentation requirements. Final Action/Quest replacement waits for all required Lesson Graph features and migration dependencies. Quiz/Exploration keep their existing runtimes, TimeManager, and FirebaseManager. This follow-up is not part of the current 2.2/2.3 implementation.
