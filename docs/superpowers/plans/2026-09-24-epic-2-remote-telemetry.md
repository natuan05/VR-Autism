# Epic 2 Remote Commands and Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Dispatch one implementer at a time, then one independent reviewer covering specification and code quality. Re-review material fixes only. Steps use checkbox syntax for tracking.

**Goal:** Finish stories 2.2 and 2.3 with activation-safe therapist commands and isolated, idempotent V2 observations across Unity, Python, and the dashboard.

**Architecture:** Unity's runner remains the authority for graph state. A separate LiveKit remote bridge routes typed commands into that runner; the executor alone reaches active source capabilities. A V2 telemetry adapter observes immutable runner events and writes low-frequency RTDB state plus idempotent Firestore audit/session projections; it never drives transitions.

**Tech Stack:** Unity C#/NUnit EditMode, LiveKit DataPackets, Python/pytest, Firebase RTDB/Firestore, Next.js/TypeScript/Vitest.

**Spec:** `D:/Lab/VR-Autism/_bmad-output/specs/spec-epic-2-remote-telemetry/SPEC.md`. Read every file in its `companions:` list, especially `contracts.md`. The SPEC is canonical; this plan chooses implementation steps. The spec folder and sprint tracker are ignored artifacts in the root checkout, while code work happens in isolated worktrees.

## Global Constraints

- Work in `D:/Lab/VR-Autism/.worktrees/epic-2-continuation`, branch `codex/epic-2-continuation`, initial HEAD `ca1ee02c`. Do not touch scenes or root checkout changes.
- Unity code/tests live in `Assets/Project/Scripts/`; Python changes live in `LiveKitAgent/src/` and `LiveKitAgent/tests/`. No vendor, package, generated cache, or Unity settings edits.
- Do not launch Unity, batch mode, or Test Runner. Author focused EditMode tests and hand their exact filters to the user. State explicitly that they were not executed.
- Before modifying each existing function/class/method, run GitNexus upstream impact and report callers, processes, and risk; warn before HIGH/CRITICAL edits. Query/context before navigating unfamiliar subsystems. The initial group query returned no hits; inspect source when the index lacks symbols, and report that limitation rather than inventing impact evidence.
- Inspect scoped diffs, run `git diff --check`, and GitNexus `detect_changes` for each deliverable before committing or finalizing. Refresh the index after adding substantial files; verify the indexed repository/branch is the worktree rather than trusting the root index.
- Use Conventional Commits and stage exact files. Commit only when session authorization includes commits; the review/test gates apply even without a commit.
- All real-time commands, hints and results use reliable LiveKit DataPackets. Event names uppercase, fields snake_case, `contract_version: 2`, nonempty correlation IDs. RTDB is observation only for V2 remote controls.
- Preserve `agent.py`, `TelemetryStreamer.cs`, legacy Actions/QuestController/ActionManager, legacy RTDB command handlers, and legacy voice hooks. `LiveKitService.cs` remains sole microphone owner. A single agent publishes a single audio track.
- General dialogue never mutates evaluation context. Verbal hints use the immutable activation phrase snapshot and assigned NPC identity; never use legacy quick phrases or Inspector fallback phrases.
- RTDB writes are restricted to `live_sessions/{sessionId}/lesson_graph`. No frame loop or periodic stream. Firestore uses stable event identities and merge/upsert semantics, never append on retry.
- New Unity files/folders need committed `.meta` files with unique GUIDs using established repository conventions. Read `Tests/Editor/AGENTS.md`: async tests use bounded `[UnityTest] IEnumerator` helpers and complete every fixture TaskCompletionSource.
- Use one fresh Luna implementer subagent per task at high or xhigh reasoning effort, then one independent review. The manager owns this SPEC and plan. Do not start the next task until the current task passes its review gate.
- Web changes are in `D:/Lab/VRA-web/src/`, outside this workspace sandbox. Read that repository's AGENTS.md and status before editing, request tool escalation for authorized writes if required, and isolate from existing user changes. Current web branch is `codex/voice-phrase-v2`, with uncommitted user edits in `src/hooks/useLiveKitDataChannel.ts`: preserve them, do not stash, overwrite, or commit them. Prefer a separate web worktree/branch `codex/epic-2-remote-telemetry` from the current committed HEAD, at `D:/Lab/VRA-web/.worktrees/epic-2-remote-telemetry`; all WEB paths below then resolve inside that worktree. Verify it includes required committed phrase-schema code; the new V2 hooks intentionally do not depend on the user's dirty legacy hook. Record separate web commit IDs at handoff and do not merge into the user's branch automatically. Never silently omit web work and call the story complete. If auto-review denies escalation, report the precise blocked operation.

---

## File responsibility map

Prefixes: `V2 = Assets/Project/Scripts/Gameplay/LessonGraphV2`, `WEB = D:/Lab/VRA-web/src` in the isolated web worktree, and `PY = LiveKitAgent`. Every path in this plan uses those exact roots.

| Unit | Owns | Tasks |
| --- | --- | --- |
| `V2/Remote/LessonRemoteContractsV2.cs`, `LessonCommandCodecV2.cs`; `WEB/types/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.ts` | One frozen command, result, and state wire contract; strict command parsing and tolerant state parsing | 1, 4, 7 |
| `V2/Runtime/LessonSessionContextV2.cs`, `LessonGraphRunner.cs`, `LessonGraphRuntimeContracts.cs` | Session identity, activation cancellation, runner decisions and lifecycle events | 1, 2, 5 |
| `V2/Questing/`, `PY/src/voice_*_v2.py`, `PY/src/agent_v2.py` | Active source capability and correlated verbal-hint speech | 3 |
| `V2/Remote/LiveKitLessonRemoteBridgeV2.cs`, `WEB/hooks/useLessonGraphRemoteV2.ts`, `WEB/app/dashboard/expert/session/[id]/page.tsx` | LiveKit packet bridge and therapist controls | 4 |
| `V2/Telemetry/`, `Cloud/Models/NodeLogData.cs`, `SessionData.cs` | Pure lifecycle projection, sole session-scoped retry writer, Firebase sinks | 5, 6 |
| `WEB/hooks/useLessonGraphTelemetryV2.ts`, `WEB/lib/lesson-graph-telemetry-v2.ts`, `PY/src/lesson_telemetry_contract_v2.py` | Cross-stack observation parsing and audit display; no second writer | 7 |

**Resume state:** Task 1 started before the SPEC correction. Three uncommitted web files (`WEB/types/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.test.ts`) already passed 18 focused Vitest cases after an observed missing-module red. Review those files against the canonical SPEC, keep valid work, and complete Task 1. No Unity Task 1 code or Task 1 commit exists yet.

## Baseline and decisions

Paths below use `V2 = Assets/Project/Scripts/Gameplay/LessonGraphV2`, `WEB = D:/Lab/VRA-web/src`, `PY = LiveKitAgent`. Expand these exact prefixes; they are not alternative locations.

Observed baseline:

- `Runtime/LessonGraphRunner.cs` exposes `Configure`, `StartLessonAsync`, `RequestSkip`, `RequestTimeout`, `AbortLesson`; it has no session ID, pause/resume state, remote-command API, or node-cancelled event. `RunAsync` currently uses the lesson token directly for executor cancellation.
- `QuestNodeExecutor.ExecuteAsync` owns source lists only as locals and settles first-win. `QuestSourceV2` has lifecycle, but no visual-hint capability. `VoiceQuestSourceV2` uses a concrete voice transport; `IVoiceQuestTransport` only supports activation/cancellation.
- `NodeResult.ElapsedSeconds` currently represents the shared clock reading, not node duration. Do not serialize that as per-node elapsed without subtracting entry time.
- `VoicePhraseSessionSnapshotV2` carries launch token, lesson ID, lesson voice revision and child phrase revision. `SessionContext.Instance.SessionId` carries the external session ID; runner RunId is a different identity.
- `Cloud/Models/SessionData.cs` only has `quest_logs`; no NodeLogData exists. Existing FirebaseManager accumulates legacy logs and must not become the V2 writer.
- `Bathroom-V2.unity` has an enabled `TimeManager` alongside `LessonGraphRunnerInstaller`. `TimeManager.Start()` calls `FirebaseManager.BeginSession()` and starts the existing LiveKit/session handshake; `SaveLessonTimeData()` can call `FirebaseManager.SaveSession()` for the same session. A whole-document legacy save would overwrite V2-owned session projections. Keep the handshake, but gate legacy Firestore accumulation/save for an active V2 lesson.
- Web session page currently uses `useLiveKitDataChannel` for legacy voice and RTDB `pushRemoteCommand` for visual hint/skip. A separate V2 path is required. `ControlSidebar.tsx` is not necessarily the page's actual control surface; change the rendered controls in `[id]/page.tsx`, not an unused component.
- Python's strict `parse_unity_packet` accepts SET_ACTIVE_QUEST, CANCEL_ACTIVE_QUEST and SPEAK_SCRIPT, but no correlated VERBAL_HINT. Existing unversioned hint handler chooses cached active phrases and is insufficient for V2 correlation.

Resolve unspecified policy using these conservative implementation choices, recording them in the contract tests:

1. Pause policy is `CancelAndReactivate` for current Phase 1 executors. Pause cancels the activation, waits for cleanup, remains on the same node without selecting an edge, and resume creates a fresh activation with reset node timeout. Lesson task remains pending. No in-place pausing or Phase 2 resume restoration.
2. All five remote command kinds target a concrete session/run/node/activation. Resume targets the retained paused activation; after accepting it, old activation commands are stale. Hints additionally require `binding_id`. Derive `npc_binding_id` from the active source, never trust a dashboard-supplied route.
3. Command IDs are unique within the lesson run and retained until run termination. A repeated ID never applies twice and returns `DUPLICATE`; retain the original acceptance in audit. No small LRU cache that permits old IDs to apply again.
4. A visual hint toggles a separately assigned V2 hint indicator GameObject on the source for the rest of the activation and restores its prior active state on cleanup. No mutation of interactable state/materials, no legacy hint controller. Missing indicator means `UNSUPPORTED_CAPABILITY`.
5. Persist node logs per activation, but one compatibility QuestLogData per logical `(run_id,node_id)` in Phase 1; pause attempts aggregate elapsed/hint counts and do not create extra quest logs. Phase 2 repeated-node semantics are explicitly outside this story.
6. In-process reconnect/retry is covered. Process-kill durable offline delivery is not specified; do not claim it. Keep pending writes alive across scene teardown using a V2 session-scoped writer, and expose failed/pending flush at session end.

## Frozen wire and storage contract

Remote topic: `lesson-graph-v2.remote`. Command envelope:

```json
{"contract_version":2,"event":"LESSON_COMMAND","command_id":"cmd-1","session_id":"session-1","run_id":"run-1","node_id":"quest-1","activation_id":"activation-1","command":"VISUAL_HINT","binding_id":"soap-touch"}
```

`command` is `SKIP | PAUSE | RESUME | VERBAL_HINT | VISUAL_HINT`. `binding_id` is empty for non-hints. Reject absent/blank correlation fields, unknown command or invalid type. Result envelope uses `event: LESSON_COMMAND_RESULT`, repeats correlation and command, adds `accepted: bool`, `reason: string`, and authoritative `state` snapshot. Reasons: `NONE`, `MALFORMED`, `WRONG_SESSION`, `WRONG_RUN`, `WRONG_NODE`, `STALE_ACTIVATION`, `DUPLICATE`, `NOT_ACTIVE`, `INVALID_STATE`, `WRONG_BINDING`, `UNSUPPORTED_CAPABILITY`, `TRANSPORT_UNAVAILABLE`, `CANCELLED`. Snapshot replies/reconnect use `event: LESSON_STATE` with the same state DTO. Dashboard result timeout is 5 seconds: show unconfirmed, do not infer state or automatically replay a command.

Voice topic remains `lesson-graph-v2.voice`. New Unity-to-agent packet:

```json
{"contract_version":2,"event":"VERBAL_HINT","command_id":"cmd-2","activation_id":"activation-1","npc_binding_id":"teacher-npc"}
```

Agent verifies current activation and NPC, de-duplicates command IDs for that activation, selects from its immutable active phrase snapshot, and checks activation again before speech. No dashboard packet on the remote topic is consumed by the agent.

`LessonStateV2` fields (also exact RTDB keys): `contract_version`, `session_id`, `run_id`, `graph_id`, `lesson_id`, `launch_token`, `lesson_voice_revision`, `child_phrase_revision`, `node_id`, `node_type`, `node_index`, `activation_id`, `status`, `checkpoint_id`, `updated_at_utc`, `state_revision`, `active_node_ids`, `parallel_group_id`, `bindings`. `bindings` entries contain `binding_id`, `npc_binding_id`, `can_verbal_hint`, `can_visual_hint`. Status strings: `running`, `pausing`, `paused`, `completed`, `failed`, `cancelled`. Phase 1 active_node_ids contains the current active node or empty; parallel_group_id and checkpoint_id are empty until applicable. Retain node/activation identity while paused and terminal for diagnostics.

Audit event fields: `event_id`, `event_type`, session/run/graph/node/type/index/activation identities, `occurred_at_utc` (UTC ISO 8601), `elapsed_seconds` (monotonic run elapsed), `status`, `command_id`, `reason`, launch/revision context. NodeLogData additionally contains `entered_at_utc`, `exited_at_utc`, `duration_seconds`, `completion_channel`. Stable IDs are generated once at observation: lifecycle identity `(session,run,activation,event_type)`, command identity `(session,run,command_id,disposition)`; encode/hash tuple into Firestore-safe document ID. Duplicate replay records at most one `DUPLICATE` audit event for that command, never per receipt. Node log identity `(session,run,activation)`; compatibility identity `(session,run,node)`.

Firestore layout: `sessions/{sessionId}/lesson_events/{eventId}` for immutable lifecycle events, and merged `sessions/{sessionId}` projection containing ordered `node_logs` plus V2 compatibility `quest_logs`. Use transactionally persisted keyed V2 maps (`v2_node_logs_by_id`, `v2_quest_logs_by_id`) to derive the arrays and survive adapter re-creation; do not replace unrelated session fields. A V2 session must not concurrently use legacy quest-log accumulation. Assert this ownership at composition and document required scene setup.

## Task 1 — Define remote DTOs and runner state contract (2.2)

**Depends on:** baseline only. **Deliverable:** compiling typed DTOs, strict parser, immutable state/correlation context and serialization fixtures; no active routing yet.

**Files:** Create `V2/Remote/LessonRemoteContractsV2.cs`, `V2/Remote/LessonCommandCodecV2.cs`, `V2/Runtime/LessonSessionContextV2.cs`, `V2/Tests/Editor/LessonRemoteContractV2Tests.cs`; modify `V2/Runtime/LessonGraphRuntimeContracts.cs`; create `WEB/types/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.test.ts`.

**Interfaces produced:** `LessonCommandV2` and `LessonCommandResultV2` serializable field DTOs matching above; `LessonStateV2`; `LessonCommandCodecV2.TryParse(byte[] payload, out LessonCommandV2 command, out string reason): bool`; `LessonSessionContextV2` immutable constructor `(string sessionId, string lessonId, string launchToken, int lessonVoiceRevision, int childPhraseRevision)`; `LessonRuntimeStateV2` enum matching statuses. TS `parseLessonStateV2(value: unknown): LessonStateV2 | null`, `createLessonCommandV2(state: LessonStateV2, command: LessonCommandKindV2, commandId: string, bindingId?: string): LessonCommandV2`.

**Test-first steps (each check is one action):**

- [ ] Compare the three uncommitted WEB files with `SPEC.md` and `contracts.md`; keep only matching fields and validation.
- [ ] Re-run `D:/Lab/VRA-web/node_modules/.bin/vitest.cmd run src/lib/lesson-graph-v2.test.ts` from the web worktree; preserve the recorded missing-module red and verify current green.
- [ ] Write `LessonRemoteContractV2Tests` table cases for the five commands and missing/string/old `contract_version`; the new Unity test cannot compile until the DTO exists.
- [ ] Add test cases for each blank correlation ID, missing hint `binding_id`, unknown command, invalid UTF-8/JSON, and wrong property types.
- [ ] Implement C# DTO fields and `LessonSessionContextV2`, then add `LessonRuntimeStateV2` without changing existing status values.
- [ ] Implement `LessonCommandCodecV2.TryParse`; reject illegal Firebase path-key characters in IDs used as path keys.
- [ ] Compare one literal valid and one invalid command/state fixture byte-for-byte across C# and TS tests.
- [ ] Run focused web Vitest and scoped ESLint; inspect exact diffs and `git diff --check` in both worktrees.
- [ ] Run GitNexus `detect_changes` for both repos, review the Task 1 diff independently, then commit only Task 1 files with Conventional Commits.

- [ ] Read web instructions/status and acquire authorized write access. Run impact on runtime contracts before modifying them.
- [ ] Write table tests using the literal envelope above: valid all five commands; contract_version missing/string/1; empty each ID; unknown kind; hint missing binding; malformed UTF-8/JSON; wrong property types; parse state with additive future fields. Use strict field validation for commands, tolerant extra fields for state.

```ts
expect(createLessonCommandV2(state, "VISUAL_HINT", "cmd-1", "soap-touch"))
  .toMatchObject({contract_version: 2, event: "LESSON_COMMAND", activation_id: state.activation_id, binding_id: "soap-touch"});
expect(parseLessonStateV2({...state, contract_version: 1})).toBeNull();
```

- [ ] Run `npm test -- src/lib/lesson-graph-v2.test.ts` in web; observe missing implementation failure. Author Unity filter `LessonRemoteContractV2Tests` without running Unity.
- [ ] Implement exact DTOs and codec; reject unsafe Firebase path identities containing `/`, `.`, `#`, `$`, `[` or `]` where used as RTDB path keys. Do not conflate session_id/run_id.
- [ ] Re-run focused web tests; independent review compares literal C#/TS fixtures and all identity rejection branches. Scoped diff/check/detect_changes gate; suggested commit `feat(remote): define V2 lesson command contract`.

## Task 2 — Make runner pause/resume and command decisions authoritative (2.2)

**Depends on:** Task 1. **Deliverable:** runner applies skip/pause/resume once, exposes state, and rejects stale/early commands without state changes.

**Files:** Modify `V2/Runtime/LessonGraphRunner.cs`, `V2/Runtime/LessonGraphRuntimeContracts.cs`; create `V2/Tests/Editor/LessonGraphRemoteCommandTests.cs`; extend `V2/Tests/Editor/LessonGraphRunnerTests.cs`.

**Interfaces:** Add `ConfigureSession(LessonSessionContextV2 context)`, `Task<LessonCommandResultV2> ApplyCommandAsync(LessonCommandV2 command)`, `LessonStateV2 CurrentState`, `event Action<LessonStateV2> StateChanged`, `event Action<LessonCommandResultV2> CommandEvaluated`, `event Action<NodeCancelledEventV2> NodeCancelled`. `NodeCancelledEventV2` carries run/node/activation/reason/monotonic elapsed. Keep existing public local APIs for compatibility; they enter the same transition primitives. Add an activation CTS linked to lesson CTS; pause never cancels lesson CTS. One monotonic `state_revision` per authoritative state change.

**Test-first steps:**

- [ ] Read current runner and executors; run upstream impact for each existing runner symbol to edit and report blast radius.
- [ ] Write one bounded `[UnityTest]` proving pause cancels active executor work while the lesson task remains pending.
- [ ] Add bounded tests for resume on the same node with a fresh activation, late old completion, abort while paused, and duplicate skip.
- [ ] Add tests for wrong session/run/node/activation, early hints, invalid state, and stable typed rejection without revision change.
- [ ] Add runner state/context fields, activation cancellation source, pause continuation signal, and new lifecycle events; keep existing local APIs.
- [ ] Route skip/pause/resume through one serialized runner decision path and record command ID before side effects.
- [ ] Verify cancellation waits for executor cleanup and prevents stale continuation from selecting an edge.
- [ ] Inspect changed runtime contracts and focused Unity test filters; do not launch Unity.
- [ ] Run `git diff --check`, GitNexus `detect_changes`, independent review, and commit only Task 2 files.

- [ ] Impact `LessonGraphRunner`, `RunAsync`, `BeginActivation`, `EndRun`, and existing request APIs. Read executors' cancellation behavior.
- [ ] Add bounded coroutine tests with fake blocking executor: pause cancels its token once; no NodeCompleted/edge on pause; lesson task remains pending; resume invokes same node with different activation; late first activation result cannot complete/abort second; abort while paused terminates; skip after pause rejected; duplicate skip produces one edge.

```csharp
var before = runner.CurrentState.activation_id;
var pauseTask = runner.ApplyCommandAsync(pauseCommand);
yield return CompleteWithinFrames(pauseTask);
Assert.That(runner.CurrentState.status, Is.EqualTo("paused"));
Assert.That(lessonTask.IsCompleted, Is.False);
yield return CompleteWithinFrames(runner.ApplyCommandAsync(resumeCommand));
Assert.That(runner.CurrentState.activation_id, Is.Not.EqualTo(before));
Assert.That(runner.CurrentState.node_id, Is.EqualTo("quest-1"));
```

- [ ] Implement validation under runner ownership in order: decode, session/run, duplicate identity, node/activation, state, capability. Treat executor registration as activating until source work is ready; reject early hints with NOT_ACTIVE. Record first decision before invoking side effects to prevent reentrant repeats.
- [ ] Serialize transitions on Unity main thread; do not hold a lock across await. Maintain a paused continuation signal released by resume or lesson cancellation. Cancellation wins over any abandoned executor result; observe abandoned task faults without allowing their continuations to mutate new state.
- [ ] Keep the current `INodeExecutor` reference in runner activation scope. Until Task 3 installs hint capabilities, return UNSUPPORTED_CAPABILITY for hints. Task 3 adds `IQuestHintExecutorV2.TryApplyHintAsync(LessonCommandV2 command): Task<LessonCommandResultV2>` implemented by QuestNodeExecutor; runner dispatches only through that interface after validating activation, never by querying scene sources.
- [ ] Gate pause completion on executor cleanup. For noncooperative executors reject resume/keep pausing until cleanup rather than activate competing sources. Current built-in executors must clean promptly via cancellation.
- [ ] Emit cancellation/transition/terminal events exactly once including OnDisable/OnDestroy. Preserve normal first-win, timeout/status edge selection and existing runner tests.
- [ ] Independent review gate focuses on race interleavings and no unbounded test waits. Handoff filters `LessonGraphRemoteCommandTests`, `LessonGraphRunnerTests`, `DialogueNodeExecutorTests`, `QuestNodeExecutorTests`. Diff/check/detect_changes; suggested commit `feat(runtime): add correlated pause and resume`.

## Task 3 — Route eligible hints through executor and typed voice transport (2.2)

**Depends on:** Task 2. **Deliverable:** one matching active source receives each hint; cancelled/unsupported/wrong-binding sources do nothing.

**Files:** Modify `V2/Runtime/Executors/QuestNodeExecutor.cs`, `V2/Questing/Sources/QuestSourceV2.cs`, `V2/Questing/Sources/VoiceQuestSourceV2.cs`, `V2/Questing/Voice/VoiceQuestTransportContracts.cs`, `V2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs`; create `V2/Questing/Contracts/QuestHintContractsV2.cs`, `V2/Tests/Editor/QuestHintRoutingV2Tests.cs`; extend existing `VoiceQuestTransportV2Tests.cs`, `VoiceQuestSourceV2Tests.cs`. Modify `PY/src/voice_contract_v2.py`, `PY/src/agent_v2.py`, `PY/src/voice_quest_runtime_v2.py`; create `PY/tests/test_verbal_hint_v2.py`.

**Interfaces:** `IQuestVisualHintV2.TryShowVisualHint(string activationId): bool`; `IQuestVerbalHintV2.SendVerbalHintAsync(string activationId, string commandId, CancellationToken token): Task<bool>`; `QuestNodeExecutor.TryApplyHintAsync(LessonCommandV2 command): Task<LessonCommandResultV2>`; add typed `VoiceQuestVerbalHint` with activation_id/command_id/npc_binding_id and `IVoiceQuestTransport.SendVerbalHintAsync(VoiceQuestVerbalHint request, CancellationToken token): Task<bool>`. Add Python frozen `VerbalHintV2` and parser case; `_handle_verbal_hint_v2(session, runtime, packet)` validates before using existing voice-profile/cached-speech machinery.

**Test-first steps:**

- [ ] Run upstream impact for each existing executor/source/transport/agent symbol to edit; inspect active-source cleanup boundaries.
- [ ] Add Unity fake-source tests proving only the matching binding receives visual/verbal hints, with unsupported and stale rejection.
- [ ] Add transport tests for disconnected false return and no transient reconnect replay; new tests remain unrun until user runs Unity.
- [ ] Write `PY/tests/test_verbal_hint_v2.py` for matching NPC/activation, duplicate command, wrong route, synthesis/playback cancellation, and phrase order.
- [ ] Run `python -m pytest tests/test_verbal_hint_v2.py -q` and record expected missing-feature red.
- [ ] Add source capability interfaces, indicator state restore, and executor activation-scoped hint dispatch.
- [ ] Add typed Unity voice packet and Python parser/handler using the assigned NPC and immutable phrase snapshot.
- [ ] Re-run focused Python red test to green, then the listed V2 regression tests; run Ruff on touched Python files.
- [ ] Inspect exact Unity/Python diffs, `git diff --check`, GitNexus `detect_changes`, independent review, and commit only Task 3 files.

- [ ] Impact affected symbols. Add test fixture sources that count visual calls and fake transport that captures hint payloads. Use two simultaneously eligible bindings and prove only requested binding changes.

```csharp
Assert.That(otherSource.HintCalls, Is.Zero);
Assert.That(transport.LastHint.npc_binding_id, Is.EqualTo(voiceSource.NpcBindingId));
Assert.That(transport.LastHint.activation_id, Is.EqualTo(activeId));
Assert.That(staleResult.reason, Is.EqualTo("STALE_ACTIVATION"));
```

- [ ] Write Python async tests for active NPC profile selection; unknown profile fallback remains existing behavior; wrong NPC/activation rejects; repeated command does not speak twice; cancellation during synthesis prevents speech; cancellation during playback stops owned work; phrases remain in canonical snapshot order.
- [ ] Run `python -m pytest tests/test_verbal_hint_v2.py -q` from PY, observe expected failure, then implement. Store active executor scope until finally cleanup, never re-resolve an arbitrary inactive source for a command. Clear scope before source cancellation can reenter.
- [ ] Implement indicator visibility capture/restore in base V2 source; missing reference rejects; destroyed hint GameObject is safely ignored during cleanup. Do not add or alter scene references in this task.
- [ ] Voice transport must return false when disconnected, never queue a transient hint for reconnect; activation/cancellation desired-state reconciliation remains unchanged. Ensure source uses injectable IVoiceQuestTransport for tests. Update all existing fake transport implementers for the added method.
- [ ] Agent ignores remote topic. Correlated hint de-duplication is separate from quest evaluation and blocking dialogue sequence state; it must not reset either. Protect cached audio by NPC profile identity to avoid replaying another NPC's voice.
- [ ] Run focused Python tests including `test_voice_contract_v2.py`, `test_agent_v2.py`, `test_voice_v2_reconciliation.py`, `test_speak_script_v2.py`. Reviewer checks scope ownership and stale callbacks. Unity filters: `QuestHintRoutingV2Tests`, `VoiceQuestTransportV2Tests`, `VoiceQuestSourceV2Tests`. Diff/check/detect_changes; suggested commit `feat(remote): route V2 source hints safely`.

## Task 4 — Connect LiveKit bridge and dashboard controls (2.2)

**Depends on:** Task 3. **Deliverable:** real packet bridge plus dashboard UI using authoritative state and typed rejection feedback, without waiting for telemetry story.

**Files:** Create `V2/Remote/LiveKitLessonRemoteBridgeV2.cs`, `V2/Tests/Editor/LiveKitLessonRemoteBridgeV2Tests.cs`; modify `V2/Runtime/LessonGraphRunnerInstaller.cs`, `V2/Phrases/VoicePhraseSnapshotStoreV2.cs`; create `WEB/hooks/useLessonGraphRemoteV2.ts`, `WEB/lib/lesson-graph-remote-v2.ts`, `WEB/lib/lesson-graph-remote-v2.test.ts`; modify `WEB/app/dashboard/expert/session/[id]/page.tsx`.

**Interfaces:** bridge `Configure(ILiveKitDataPacketClientV2 client, LessonGraphRunner runner)`, subscribe DataReceivedV2/ReconnectedV2, main-thread queue, Dispose/OnDestroy unsubscribe. Expose snapshot metadata accessor on phrase store returning immutable session metadata. Web controller `send(command: LessonCommandKindV2, bindingId?: string): Promise<LessonCommandResultV2>`; hook returns `{state, connected, pending, rejection, send}`. Controller uses injected reliable publisher, clock and ID factory for tests.

**Test-first steps:**

- [ ] Run upstream impact for installer, snapshot store, and rendered page callbacks before changing them.
- [ ] Add bounded Unity bridge tests for wrong topic/version, queued stale packet, reconnect, repeated Configure, and teardown; leave them for the user to run.
- [ ] Write web controller tests for reliable topic, authoritative state, typed rejection, timeout, disconnect, and unmount.
- [ ] Run `D:/Lab/VRA-web/node_modules/.bin/vitest.cmd run src/lib/lesson-graph-remote-v2.test.ts`; record missing-feature red.
- [ ] Implement bridge subscriptions, main-thread dispatch, snapshot publication, and read-only state request handling.
- [ ] Implement web controller/hook, then render V2 controls on the actual session page using validated state and binding selection.
- [ ] Re-run focused web tests to green and scoped ESLint; verify explicitly legacy sessions still use their prior controls.
- [ ] Inspect exact Unity/web diffs, `git diff --check`, GitNexus `detect_changes`, independent review, and commit only Task 4 files.

- [ ] Impact installer/phrase-store APIs and page callbacks. Snapshot SessionContext + phrase revisions once during composition; refuse remote activation on empty session identity rather than invent external IDs. Existing local test configuration remains usable.
- [ ] Tests deliver wrong topic/version; enqueue then change node before dispatch; duplicate after reconnect; pending reply after component unmount; scene teardown with queued callback; multiple Configure calls. Assert bridge references runner only, never bindings/sources.
- [ ] Publish LESSON_STATE on state change/reconnect; dashboard can also request current state with `LESSON_STATE_REQUEST`, version/session_id only. Codec handles this read-only request separately from mutating commands. Replies expose fresh state, never replay commands.
- [ ] Add controller tests with mocked publisher/result input. Assert reliable=true and remote topic, binding targets copied from observed state, timeout leaves state unchanged, duplicate result settles once, wrong session/run result ignored, disconnect disables sending. `npm test -- src/lib/lesson-graph-remote-v2.test.ts` must fail before implementation then pass.

```ts
expect(published.options).toMatchObject({reliable: true, topic: "lesson-graph-v2.remote"});
expect(controller.state.status).toBe("running"); // unchanged before runner acknowledgment
controller.receive({...result, accepted: false, reason: "STALE_ACTIVATION"});
expect(controller.rejection).toBe("STALE_ACTIVATION");
```

- [ ] In rendered page, choose V2 controls only after validated V2 state for this session. Display node/status, pause/resume from state, skip, and explicit binding selector if multiple hint targets. Disable unsupported/pending/disconnected controls. Show rejected/unconfirmed feedback without optimistic node advancement. While V2 session identification is loading, do not fall back to legacy writes for an already identified V2 session.
- [ ] Keep legacy paths for explicitly legacy sessions. V2 free-text NPC speech is not added by this story: hide the legacy speech control in V2 mode because it bypasses runner ownership. Document this UI distinction.
- [ ] Review Unity + TS fixtures together. Run focused Vitest and scoped ESLint; user Unity filter `LiveKitLessonRemoteBridgeV2Tests`. Diff/check/detect_changes in both repos. Suggested commit `feat(remote): connect dashboard V2 lesson controls`.

## Task 5 — Project runner events into deterministic telemetry models (2.3)

**Depends on:** Task 4. **Deliverable:** pure telemetry reducer, event IDs, node logs and legacy quest compatibility mapping tested without Firebase.

**Files:** Create `Assets/Project/Scripts/Cloud/Models/NodeLogData.cs`; modify `Assets/Project/Scripts/Cloud/Models/SessionData.cs`; create `V2/Telemetry/LessonTelemetryContractsV2.cs`, `V2/Telemetry/LessonTelemetryReducerV2.cs`, `V2/Telemetry/QuestLogCompatibilityMapperV2.cs`, `V2/Tests/Editor/LessonTelemetryReducerV2Tests.cs`, `V2/Tests/Editor/LessonTelemetrySerializationV2Tests.cs`; modify runtime event DTOs only where needed to capture immutable transition context.

**Interfaces:** `LessonTelemetryReducerV2.Observe(LessonLifecycleEventV2 item): TelemetryWriteBatchV2`; `QuestLogCompatibilityMapperV2.Map(NodeLogData terminal, QuestHintSummaryV2 hints): QuestLogData`; `IUtcClockV2.UtcNow: DateTimeOffset`. `TelemetryWriteBatchV2` contains zero/one state projection, keyed audit events, keyed node logs and keyed quest logs. `SessionData.node_logs: List<NodeLogData>` with FirestoreProperty. Node and lifecycle DTO fields follow frozen schema.

**Test-first steps:**

- [ ] Run upstream impact for `SessionData` and any existing runtime event DTO to edit.
- [ ] Add serialization tests for exact Firestore field names and additive legacy session deserialization.
- [ ] Add reducer fixture at monotonic 10 to 14 seconds and UTC t0 to t0+9; assert duration 4 seconds and original UTC timestamps.
- [ ] Add replay, pause/resume, cancellation, accepted/rejected hint, and compatible quest-log cardinality tests.
- [ ] Implement immutable lifecycle DTOs, stable event IDs, NodeLogData, and SessionData mapping.
- [ ] Implement pure reducer and V2-owned QuestLog compatibility mapper without Firebase calls.
- [ ] Inspect cancellation/timeout/skip mappings and exact diff; Unity tests remain unrun.
- [ ] Run `git diff --check`, GitNexus `detect_changes`, independent review, and commit only Task 5 files.

- [ ] Impact SessionData/event DTOs. Write serialization tests with explicit field names; JsonUtility does not serialize C# properties, so use explicit dictionaries/Firebase property conversion for Cloud models rather than assuming JsonUtility works.
- [ ] Reducer fixture enters at monotonic 10/UTC t0, exits at monotonic 14/UTC t0+9; assert duration=4, timestamps preserve UTC values. Repeat events/reconnect replay and assert same IDs/cardinality. Cancellation closes active log once even without NodeCompleted.

```csharp
Assert.That(log.duration_seconds, Is.EqualTo(4d));
Assert.That(session.node_logs.Count, Is.EqualTo(2)); // paused attempt + resumed terminal attempt
Assert.That(session.quest_logs.Count, Is.EqualTo(1));
Assert.That(session.quest_logs[0].hints_visual, Is.EqualTo(1));
```

- [ ] Count accepted unique hints only; reject/duplicate adds audit but no hint count. Track last accepted visual hint monotonic time for response_time_from_hint; default -1 when absent. Map success with accepted hints to assisted, success without hints to success, skip to skipped, timeout/failed/cancelled to failed rather than falsely reporting success. Verify web supports failed string additively.
- [ ] Use graph node list index as node_index and display name fallback to node ID. Finalize compatibility once on terminal quest exit, or lesson termination if paused/aborted; pause alone does not finalize. Preserve launch token and phrase revisions from captured context, not mutable globals.
- [ ] State projection only changes for node/lesson transitions (including pause/checkpoint); repeated events and command rejection alone do not write current state. No Update timer.
- [ ] Reviewer checks all lifecycle paths including terminal failure/timeout/cancel and exactly-one mapper ownership. User filters `LessonTelemetryReducerV2Tests`, `LessonTelemetrySerializationV2Tests`. Diff/check/detect_changes; suggested commit `feat(telemetry): model V2 lifecycle and node logs`.

## Task 6 — Persist V2 telemetry with retry and scene-safe lifetime (2.3)

**Depends on:** Task 5. **Deliverable:** session-scoped telemetry adapter, bounded retry writer, concrete RTDB/Firestore sinks, installation without legacy mutation.

**Files:** Create `V2/Telemetry/LessonTelemetryAdapterV2.cs`, `V2/Telemetry/LessonTelemetryWriterV2.cs`, `V2/Telemetry/FirebaseLessonTelemetrySinkV2.cs`, `V2/Tests/Editor/LessonTelemetryWriterV2Tests.cs`, `V2/Tests/Editor/LessonTelemetryAdapterV2Tests.cs`; modify `V2/Runtime/LessonGraphRunnerInstaller.cs` and `Assets/Project/Scripts/Core/Manager/TimeManager.cs` with a V2-only persistence guard. Preserve legacy behavior when no active V2 installer exists.

**Interfaces:** `ILessonTelemetrySinkV2.WriteStateAsync(LessonStateV2 state, CancellationToken token)`, `UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token)`; `LessonTelemetryWriterV2.Enqueue(TelemetryWriteBatchV2 batch)`, `FlushAsync(CancellationToken token): Task`, `PendingCount: int`; adapter `Attach(LessonGraphRunner runner, LessonSessionContextV2 context)`, `Detach()`; writer owns pending immutable batches after adapter scene teardown. One owner per external session/run.

**Test-first steps:**

- [ ] Run upstream impact on installer and Firebase model symbols to edit; inspect existing session ownership.
- [ ] Run upstream impact on `TimeManager.Start`, `LogQuestComplete`, and `SaveLessonTimeData` before the V2 guard. Assert that the V2 scene keeps the existing handshake/LiveKit behavior while only the FirebaseManager BeginSession/AccumulateQuestLog/SaveSession path is skipped for V2. Legacy scenes keep that path.
- [ ] Add fake-sink tests for first-write failure, apply-then-throw, replay, stale revision, unload, and cancelled retry delay.
- [ ] Add path assertions for only `live_sessions/{sessionId}/lesson_graph` and preserved unrelated Firestore fields.
- [ ] Implement ordered retry queue with injected delay, coalesced newest state, and retained audit batches.
- [ ] Implement RTDB revision/ownership transaction and Firestore stable-ID upsert/merge transaction.
- [ ] Attach telemetry before runner start; detach after terminal capture, retaining session writer through scene teardown.
- [ ] Seed V2 session metadata from the immutable session context and `SessionContext` into the merge projection, because the guarded legacy `BeginSession`/`SaveSession` path no longer creates the V2 Firestore session document.
- [ ] Inspect legacy-writer exclusion and exact diff; Unity tests remain unrun.
- [ ] Run `git diff --check`, GitNexus `detect_changes`, independent review, and commit only Task 6 files.

- [ ] Impact installer. Add fake sink tests failing first attempt, applying then throwing before acknowledgment, replay after new writer/reducer, out-of-order state revisions, terminal event on scene unload, cancellation while waiting for retry. Verify no duplicate stored events/logs and no rollback to older state.

```csharp
Assert.That(fakeSink.EventIds.Distinct().Count(), Is.EqualTo(fakeSink.EventIds.Count));
Assert.That(fakeSink.LatestState.state_revision, Is.EqualTo(7));
Assert.That(writer.PendingCount, Is.Zero);
Assert.That(fakeSink.WrittenPaths.All(p => p == "live_sessions/session-1/lesson_graph"), Is.True);
```

- [ ] Implement single ordered write queue, retry delays 1s/2s/4s/8s capped at 30s, cancellation-aware injected delay. Coalesce pending state to newest revision while preserving every audit event. Use RTDB transaction comparing `(run_id,state_revision)` under one session; never overwrite a new run with a late previous-run completion. Capture current run ownership at launch.
- [ ] Firestore transaction reads keyed V2 maps, upserts by identity, derives ordered arrays and merges only V2-owned fields. Each event document Set/merge uses stable ID. Retry frozen UTC timestamps and elapsed values. Explicitly test unknown unrelated session fields survive.
- [ ] Handle partial success independently: successful audit need not be appended again, retry harmless upserts, never send state backwards. Writer survives runner OnDestroy until bounded session-end flush completes or reports pending/error; avoid fire-and-forget async void exception loss.
- [ ] Composition subscribes before StartLessonAsync and unsubscribes after terminal capture; repeat Configure cannot double subscribe. Do not call FirebaseManager.AccumulateQuestLog or alter TelemetryStreamer. In `TimeManager`, identify an active V2 installer once per scene and skip only legacy FirebaseManager calls for that V2 lesson; keep the handshake and other TimeManager duties. Assert the guard in focused tests and reject any remaining ambiguous dual-writer setup at composition. Include a manual scene setup check at handoff.
- [ ] Reviewer verifies only permitted RTDB subtree, Firestore transaction and no business-state mutation by observers. User filters `LessonTelemetryWriterV2Tests`, `LessonTelemetryAdapterV2Tests`; run non-Unity static/diff checks and detect_changes. Suggested commit `feat(telemetry): persist V2 state and idempotent audit`.

## Task 7 — Consume authoritative telemetry and close cross-stack coverage (2.3)

**Depends on:** Task 6. **Deliverable:** dashboard reads V2 RTDB state and node audit history, with contract tests and explicit real-room handoff.

**Files:** Extend `WEB/types/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.ts`, `WEB/lib/lesson-graph-v2.test.ts`; create `WEB/hooks/useLessonGraphTelemetryV2.ts`, `WEB/lib/lesson-graph-telemetry-v2.ts`, `WEB/lib/lesson-graph-telemetry-v2.test.ts`; modify `WEB/app/dashboard/expert/session/[id]/page.tsx`, `WEB/types/index.ts` only for additive session node_logs; create `PY/src/lesson_telemetry_contract_v2.py`, `PY/tests/test_lesson_telemetry_contract_v2.py` as shared schema validation, not a second telemetry writer.

**Interfaces:** `useLessonGraphTelemetryV2(sessionId: string | null)` returns `{state: LessonStateV2 | null, nodeLogs: NodeLogDataV2[], error: string | null}`; `selectLatestLessonStateV2(previous, incoming)` rejects stale revisions/wrong session and respects new run launch ownership. Python pure validator accepts state/audit fixture dictionaries; agent does not write Firebase state.

**Test-first steps:**

- [ ] Run upstream impact for existing web state/page/types symbols to edit; inspect the rendered session page and current subscriptions.
- [ ] Write web parser/selector tests for missing legacy fields, future fields, stale revisions, session switch, duplicate audit, and failed status.
- [ ] Run focused Vitest and record expected missing-feature red.
- [ ] Implement RTDB `lesson_graph` subscription, Firestore log consumption, shared state selector, and page display without a second writer.
- [ ] Add identical literal C#/TS/Python state and audit fixtures; run focused Python tests and Vitest to green.
- [ ] Run scoped ESLint, inspect web/Unity/Python diffs, `git diff --check`, and GitNexus `detect_changes` in each changed repo.
- [ ] Complete independent whole-Epic review against CAP-1 to CAP-6; commit Task 7 files after material findings are resolved.
- [ ] Hand off exact Unity filters and real-room checks; do not claim real-room acceptance without user evidence.

- [ ] Write Vitest cases for legacy session missing node_logs, valid V2 state, future parallel fields, invalid field types, empty/cancelled state, monotonic revision ordering, session switch cleanup, stale prior-run write, failed quest status, duplicated audit docs. Run failing tests then implement parsers/selectors/subscription cleanup.
- [ ] RTDB subscribe only to lesson_graph; Firestore session node_logs or lesson_events subcollection supplies audit display. Render node type/status/timing and rejection reasons. DataPacket and RTDB use one selector so slower RTDB cannot replace fresher packet state. Do not infer progress from high-frequency telemetry or hardcoded quest count.
- [ ] Add same literal state/audit fixtures in Python and TS tests and C# model tests. Python validator must accept additive observation fields and reject missing required correlation. Run `python -m pytest tests/test_lesson_telemetry_contract_v2.py tests/test_verbal_hint_v2.py tests/test_voice_contract_v2.py tests/test_speak_script_v2.py tests/test_voice_v2_reconciliation.py -q`; run `npm test -- src/lib/lesson-graph-v2.test.ts src/lib/lesson-graph-remote-v2.test.ts src/lib/lesson-graph-telemetry-v2.test.ts` and scoped ESLint.
- [ ] Independent reviewer checks full 2.2/2.3 acceptance matrix, unchanged legacy paths, and both repo diffs. Fix material findings and re-review only those fixes. Run git diff --check and GitNexus detect_changes in each changed repo, report all unexpected scope.
- [ ] Handoff changed files, exact unrun Unity filters above, Python/TS results, pending escalation or runtime evidence. Suggested commit `feat(dashboard): observe V2 lesson flow and audit`.

## Execution stop gate

Stop after Tasks 1-7 have code, focused Python/web tests, static diff checks, and independent reviews complete. Keep `codex/epic-2-continuation` and the separate web branch unmerged and unpushed. Report exact Unity EditMode filters, manual real-room steps, and every unverified behavior. The user runs Unity compilation/EditMode/real-room checks and returns failures for focused fixes. Do not mark stories 2.2/2.3 `done` or merge until that evidence is reviewed.

## Required user verification and release gate

Story 2.1 is `done` in sprint status, and the user has confirmed audible dialogue in the real room. Treat that as the established baseline. Run these checks for new 2.2/2.3 behavior after the user compiles/tests Unity:

1. Dashboard targets the active voice binding; one verbal hint speaks once, visual hint shows only selected indicator. Duplicate command, wrong binding and old activation return rejection without advancing node.
2. Pause during dialogue synthesis/playback and during voice/touch work cancels old work and indicator; resume stays on the node with fresh activation. Delayed old completion does not advance it.
3. Disconnect/reconnect Unity, agent, dashboard independently; current state recovers; no transient hint replays; duplicate audit writes remain one per identity. Verify state never regresses.
4. Skip/timeout/failure/abort/scene unload close node logs, preserve terminal state and produce one compatible quest entry per logical QuestNode. Inspect RTDB lesson_graph only and Firestore stable event IDs, UTC timestamps, monotonic duration and unchanged legacy fields.
5. Run one existing legacy lesson and remote hint/skip flow to confirm the dedicated V2 route has not hijacked it.

Deployment cutover of agent.py to agent_v2.py is a separate action. Do not claim real-room acceptance of new 2.2/2.3 behavior until the above evidence exists; keep the verified 2.1 baseline distinct.

## Plan self-review

- SPEC coverage: CAP-1 is the user-verified 2.1 baseline; CAP-2 Tasks 1-2; CAP-3 Task 3; CAP-4 Task 4; CAP-5 Tasks 5-7; CAP-6 cross-stack fixtures, legacy checks, and final review in Tasks 1, 3, 4, and 7. No capability is omitted.
- Placeholder scan: no `TBD`, `TODO`, undefined later-task interface, or vague error-handling step. Type scan: `LessonCommandV2`, `LessonCommandResultV2`, `LessonStateV2`, `LessonSessionContextV2`, and `TelemetryWriteBatchV2` are introduced before consumers; web/Python names match the frozen contract.

- Story 2.2 routing/correlation/rejections: Tasks 1–4; pause/new activation: Task 2; verbal/visual capabilities: Task 3; dashboard and legacy isolation: Task 4.
- Story 2.3 lifecycle/time/model coverage: Task 5; sole writer/path/retry/idempotency: Task 6; dashboard/cross-stack compatibility: Task 7.
- Decisions needing product confirmation only if contrary requirements emerge: CancelAndReactivate policy, activation vs logical-node log aggregation, visual indicator UX, no process-kill delivery guarantee, and V2 free-text speech outside this scope. Implement these stated defaults without a new permission cycle.
- Remaining environment gates: Unity compilation/tests, real LiveKit playback, Firebase security rules permitting the new subtree/documents, and session composition that does not install both legacy and V2 writers. The isolated web worktree already exists and is writable via authorized escalation. If rules reject writes, report exact paths; do not weaken rules as part of this plan.
