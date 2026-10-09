# Story 2.4 safe stop — 2026-10-07

## Stop reason and state

The user explicitly requested a safe stop and a prompt for the next session because quota was exhausted. The running `voice_controls` subagent was interrupted. Do not automatically resume work in this session. No commits, staging, deployment, Unity launch, or controller removal occurred. All implementation remains an uncommitted working-tree diff, including new untracked files. This is a preserved checkpoint, not a verified runnable build. The final voice/control C# implementation may be incomplete or contain compilation issues.

Unity/Python worktree: `C:/Users/Admin/.codex/worktrees/d51e/VR-Autism`.
Web checkout: `D:/Lab/VRA-web` (changes are in this separate repository, not in a new worktree).
Baseline Unity HEAD: `60cb1526`. Both checkouts were clean at the start of this session; preserve all current changes and inspect for newer user edits before resuming.

## Authority and scope

Read root AGENTS.md, the current Story 2.4 specification `_bmad-output/specs/spec-story-2-4-legacy-v2-parity/SPEC.md`, English parity matrix `_bmad-output/implementation-artifacts/2-4-legacy-v2-parity-matrix.md`, and Story 2.4 in `_bmad-output/planning-artifacts/epics.md`. The ignored delivery companion is absent in this worktree; it was read from `D:/Lab/VR-Autism/_bmad-output/specs/spec-story-2-4-legacy-v2-parity/delivery-phases.md`. Plan: `_bmad-output/implementation-artifacts/plan-2-4-trial-parity.md`. That plan and review packages are ignored local artifacts; this handoff is the durable continuation record.

Trial lessons were explicitly confirmed: Bathroom-V2 and LearnToAsk-V2. Bathroom uses existing Timeline plus bounded Loop; LearnToAsk uses existing Timeline and linear quests. Do not wait for all Epic 3, or implement deferred 3.4, 3.5, 4.1, or separate Story 4.2 startup migration. Do not modify the bilingual parity matrix. Both parity matrices were left unchanged in this session; the English matrix still describes its old inventory baseline, so this handoff is the current implementation evidence.

Keep ActionManager and the old Quest controller/model stack until accepted cutover and all remaining references/consumers are migrated. Preserve Quiz/Exploration and shared TimeManager/FirebaseManager. LiveKit remains the sole real-time transport; LiveKitService remains the only microphone capture owner. User performs Unity compilation, focused EditMode tests, headset and Firebase/browser acceptance.

Every subagent must use gpt-6-luna, reasoning high by default; xhigh only for justified async lifecycle/contract work. Assign non-overlapping files. After delegation use wait_agent; on a timeout with no new information wait again, without polling status. One independent review per coherent change set, scoped re-review only for material fixes. Do not redo completed inventories/reviews.

## Explicit user decisions

- LearnToAsk-V2 preserves its original finish: no separate congratulations screen. A temporarily copied screen was removed. Bathroom-V2 retains the copied legacy Bathroom congratulations Canvas subtree.
- Use activity-based voice silence reminders: reset on actual detected speech and suppress while child/NPC speaks (including agent evaluation). This intentionally replaces legacy recognized-category reset semantics, with user approval.
- Positive silence timeout preserves periodic reminders; default is 5 seconds. Timeout 0 is implemented/planned as an immediate single reminder per activity window to avoid a tight loop; inspect this edge case in the pending review.
- Communication uses caveman style; most recent conversation language is Vietnamese. Persisted docs/code should use normal prose.

## Completed implementation and review

1. Hints/presentation/trial scene mapping: additive QuestSourceV2 hint anchors/clip/reminder cycle and events; three 0.3-second Outline on/off cycles, assigned sound at 0.6*MaxVolume, baseline restoration/cleanup; HoldTouch normalized progress/contact eligibility/reset; new LessonGraphHintPresenterV2 with bubble/progress and audited auto visual hints. Bathroom maps actual anchors/clip and scene water/soap/faucet effects. LearnToAsk retains existing ArrowPath/showArrows and adds existing collector/streamer using the XR Manager prefab's MainCamera and hand transforms. Both scenes wire new lifecycle/sensor components. Independent review found premature water/soap effects on node activation; fixed by moving the three exact touch effects to successful-completion UnityEvents. Scoped re-review: ADDRESSED, no new material findings. Focused tests added but Unity not run.
2. Lifecycle/sensors: new LessonGraphLifecycleV2 and LessonGraphSensorBridgeV2; availability-independent validated TryGetBoundSource without weakening Resolve/preflight; multi-target sampling and scope-buffer clearing; additive telemetry identities; shared elapsed axis for time_offset and last hint; terminal ended/stop/3-second lobby handling without second Firestore write; stale stream/handshake/queued RTDB-command guards. Existing installer startup retained; V2-only obsolete RTDB listeners disabled, Quiz listener preserved. Independent review found cleared-session handshake could restart media; fixed fail-closed when an existing SessionContext has empty ID and added regression test. Scoped re-review: ADDRESSED. Unity not run.
3. Web reporting/telemetry/access: shared runtime reporting adapter and isolated types; four expert/parent history/report pages; assisted success normalized; node-log fallback/logical quests; optional counts/times; RTDB callback fixed to use snapshot value; V2 sensor identity matching and correlated alerts; missing child-profile session-detail access fails closed except authorized admin. Root's independent review found total/hint response conflation, unknown values shown as zero, and session-clock resets. Material fixes inspected: total and hint response separated, unknowns display unavailable, averages ignore unknown scores, node-ID fallback, snapshot time_offset axis, optional V2 alert IDs. No open review findings from this task. Agent-reported 10 focused tests, targeted lint/type/diff checks pass.

## Interrupted work: voice/control (first independent review still pending)

The interrupted worker owns the latest C#/Python implementation. Do not treat it as complete based on presence of files. It was finishing C# cancellation, reminder audit and focused tests when stopped.

Implemented or partly wired:
- Optional `speech_silence_timeout_seconds` on SET_ACTIVE_QUEST, default5, validated finite/nonnegative, immutable duplicate-activation configuration.
- Python activation-bound silence scheduler, opening-completion start, transcript/VAD/agent busy/reconnect activity hooks, cancellation/match/replacement cleanup, approved periodic policy.
- Python sends VOICE_TOPIC ON_REMINDER accepted evidence only after successful playout: exact keys contract_version/event/direction='agent_to_unity'/result='accepted'/activation_id/npc_binding_id/command_id.
- Unity validates/dedupes reminder evidence and relays to `LessonGraphRunner.RecordAcceptedVoiceReminder`; synthetic accepted VERBAL_HINT CommandEvaluated uses existing telemetry audit/counts without speaking twice.
- SET_VOLUME and SPEAK_SCRIPT use existing correlated LESSON_COMMAND with session/run/node/activation/command_id validation/dedupe, finite volume[0,1], nonempty text<=500 and exact NPC route. Existing ApplyCommandAsync API preserved through additive publisher callback overload. Volume updates SessionContext.MaxVolume; script publishes existing typed VOICE_TOPIC packet only after authorization.
- In-flight therapist scripts are tracked and cancelled on pause/node change/unload/reconfiguration, including non-voice touch nodes. Strict VOICE_TOPIC CANCEL_SPEAK_SCRIPT exact keys: contract_version2,event,activation_id,sequence_id,npc_binding_id,reason in lesson_scope_changed|bridge_unload|transport_reconfigured. Python cancels only matching independent script, never unrelated quest; delayed speech-handle acquisition is guarded. Inspect queue/reconnect/teardown cases before accepting.
- Original non-VOICE_TOPIC legacy ON_REMINDER support was restored after root caught its unintended removal; keep legacy consumers until cutover.
- Web control half is complete by agent report: six scoped files types/lesson-graph-v2.ts, lib/lesson-graph-v2.ts and test, lib/lesson-graph-remote-v2.ts and test, live session page. V2/pending no bare script or RTDB volume fallback; retained legacy branch preserved. TS mirrors activation/reminder/cancel packets and additive sensor schema. Agent reported114 focused tests (includes mirrored worktree cases), targeted ESLint/type checks pass. Root has not independently reviewed this task4 half yet; review together with C#/Python contract.

Python worker reported30 focused tests before cancellation additions; later29 pass on voice-contract/script/agent subset, and8 pass on audio entrypoint fixtures. A full suite attempt hit credential-dependent `test_tts.py` missing Google ADC plus lightweight JobContext fixtures missing add_shutdown_callback; registration was made optional and audio fixtures rerun successfully. Do not conflate these varying subset counts with one final suite pass. No Python tests were rerun after interruption.

Environment: worktree initially lacked installed LiveKit SDK. An attempted `uv run --no-sync` created an ignored empty `.venv` using Python3.14. Recursive cleanup was reportedly rejected by the safety layer; it was not retried/restored. Preserve and inspect before any cleanup; exact rejection/precreation evidence was requested from worker but not received before stop. Do not touch user's canonical environment. Existing read-only environment `D:/Lab/VR-Autism/LiveKitAgent/.venv` has pytest/livekit.agents. For focused tests use UV_PROJECT_ENVIRONMENT pointing there, PYTHONDONTWRITEBYTECODE=1, `uv run --no-sync python -B -m pytest -p no:cacheprovider <focused files>` from worktree LiveKitAgent. No dependency installation required.

## Safe-stop checks and limitations

Fresh `git diff --check` in both repositories passed after interrupt; only CRLF conversion warnings. GitNexus detect_changes(scope=all) at stop: Unity/Python33 tracked files,213 changed symbols,31 affected,CRITICAL; web12 tracked files,40 changed symbols,29 affected,CRITICAL. New untracked files/scenes are not fully represented by indexed-symbol counts; they are not evidence of complete coverage. Shared runner, media/session and history flows require careful pending review.

Initial local GitNexus full index succeeded (28,360 nodes). A worker's incremental refresh later failed `Failed calling LOWER: Invalid UTF-8`; successful final fresh index is not established. Web index was refreshed before edits (1,790 nodes/3,708 edges) and is now stale versus uncommitted additions. Resume with appropriate refresh after writers finish; prefer --index-only to avoid modifying AGENTS/CLAUDE files. Upstream impact must still precede symbol edits; MCP/CLI absolute repo paths disambiguate registered siblings. No final cutover evidence, Firebase deployed readback/access rules, browser/headset run, or post-removal regressions exist. Story2.4 remains in progress.

## Exact continuation priorities

1. Read this handoff and inspect current scoped diff without overwriting anything. Check the interrupted voice/control C# code/test/.meta state for completeness; no automatic reset, checkout, stash, commit or new implementation inventory.
2. Complete only missing voice/control wiring and metadata. Inspect literal serialization fixture compatibility, strict malformed payload rejection, exact NPC/activation scopes, duplicate reminder audit/no replay, script cancel before/after handle creation, pause/reconnect/unload, and retained legacy packet behavior. Ensure new Editor test files have .meta (VoiceReminderAudit test was still listed without one at stop).
3. Perform ONE independent review of combined task4 (C#/Python/web); tasks1/2/3 already reviewed. Fix only material findings, re-review only fix scope. Do not launch Unity or broaden to deferred stories.
4. Run focused runnable Python/web checks once on final code, inspect scoped diff, git diff --check and GitNexus detect_changes. Refresh relevant indexes after new files if possible; record any failure accurately. Avoid repeated full suites/cloud integration without credentials.
5. Update English implementation evidence/handoff only if useful, never bilingual matrix. Deliver changed-file list and smallest Unity test filters/manual checks. Do not claim parity/cutover done; user must compile/test/headset/readback and explicitly accept cutover before any removal.

Unity filters for reviewed tasks: QuestSourceV2Tests, HoldTouchQuestSourceV2Tests, LessonGraphSensorLifecycleV2Tests; telemetry adapter/writer and remote command tests for affected shared flows. New task4 filters: VoiceQuestSourceV2Tests, VoiceQuestTransportV2Tests, LessonRemoteContractV2Tests, LiveKitLessonRemoteBridgeV2Tests, LessonGraphTherapistControlsV2Tests, LessonGraphVoiceReminderAuditV2Tests. These are recommendations, not claims of passing Unity tests.

## Changed files at safe stop

Unity/Python repository status (tracked and untracked):

```
M Assets/Project/Scenes/Bathroom-V2.unity
 M Assets/Project/Scenes/LearnToAsk-V2.unity
 M Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs
 M Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs
 M Assets/Project/Scripts/Core/Manager/TimeManager.cs
 M Assets/Project/Scripts/Core/Models/AggregatedSnapshot.cs
 M Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs
 M Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Bindings/LessonGraphBindings.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/HoldTouchQuestSourceV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/QuestSourceV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/VoiceQuestSourceV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/VoiceQuestTransportContracts.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Remote/LessonCommandCodecV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Remote/LessonRemoteContractsV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Remote/LiveKitLessonRemoteBridgeV2.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/LessonGraphRunner.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/LessonGraphRunnerInstaller.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/HoldTouchQuestSourceV2Tests.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonRemoteContractV2Tests.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LiveKitLessonRemoteBridgeV2Tests.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/QuestSourceV2Tests.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestSourceV2Tests.cs
 M Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestTransportV2Tests.cs
 M LiveKitAgent/src/agent_v2.py
 M LiveKitAgent/src/voice_command_runtime_v2.py
 M LiveKitAgent/src/voice_contract_v2.py
 M LiveKitAgent/src/voice_quest_runtime_v2.py
 M LiveKitAgent/tests/test_agent_v2.py
 M LiveKitAgent/tests/test_speak_script_v2.py
 M LiveKitAgent/tests/test_voice_contract_v2.py
 M LiveKitAgent/tests/test_voice_quest_runtime_v2.py
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphLifecycleV2.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphLifecycleV2.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphSensorBridgeV2.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphSensorBridgeV2.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation/
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphSensorLifecycleV2Tests.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphSensorLifecycleV2Tests.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphTherapistControlsV2Tests.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphTherapistControlsV2Tests.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphVoiceReminderAuditV2Tests.cs
?? LiveKitAgent/src/silence_reminder_v2.py
?? LiveKitAgent/tests/test_silence_reminder_v2.py
```

Web repository status:

```
M src/actions/history.ts
 M src/app/dashboard/expert/_hooks/useLiveTelemetry.ts
 M src/app/dashboard/expert/history/page.tsx
 M src/app/dashboard/expert/reports/page.tsx
 M src/app/dashboard/expert/session/[id]/page.tsx
 M src/app/dashboard/parent/history/page.tsx
 M src/app/dashboard/parent/reports/page.tsx
 M src/lib/lesson-graph-remote-v2.test.ts
 M src/lib/lesson-graph-remote-v2.ts
 M src/lib/lesson-graph-v2.test.ts
 M src/lib/lesson-graph-v2.ts
 M src/types/lesson-graph-v2.ts
?? src/actions/history-access.test.ts
?? src/actions/history-access.ts
?? src/lib/live-telemetry-v2.test.ts
?? src/lib/live-telemetry-v2.ts
?? src/lib/session-reporting.test.ts
?? src/lib/session-reporting.ts
?? src/types/session-reporting.ts
```

## Prompt for the next session

Continue Story2.4 from `C:/Users/Admin/.codex/worktrees/d51e/VR-Autism/docs/superpowers/handoffs/2026-10-07-story-2-4-safe-stop.md`. Preserve all uncommitted changes in that worktree and `D:/Lab/VRA-web`. Read AGENTS/spec/phase/English matrix and this handoff; do not restart completed inventories or reviews. Finish only interrupted voice/control wiring, perform its one independent review and scoped fixes, then focused non-Unity checks and handoff. Use gpt-6-luna high for subagents (xhigh for justified async review), assign non-overlapping files, and wait_agent after delegation without status polling. Trial scope is Bathroom-V2 and LearnToAsk-V2. User approved activity-based silence; LearnToAsk keeps original finish without congratulations. Do not implement3.4/3.5/4.1/Story4.2 startup migration, change bilingual matrix, remove legacy controllers, commit/deploy or launch Unity. Keep Story2.4 in progress until user-run acceptance/cutover gates pass. Speak concise Vietnamese.
