# Story 2.4 trial parity — implementation handoff, 2026-10-07

## Outcome and scope

The code implementation and independent reviews for the selected trial lessons are complete, with no open review findings. This is ready for user-run Unity and integrated acceptance checks, not an accepted cutover or a claim that Story 2.4 is done. All changes remain uncommitted in the existing Unity/Python worktree and separate web checkout; no staging, deployment, Unity launch, or legacy controller removal occurred.

- Unity/Python: C:/Users/Admin/.codex/worktrees/d51e/VR-Autism, baseline HEAD 60cb1526321a2123cebb8ff61294fad43561558a.
- Web: D:/Lab/VRA-web, HEAD d24e0220f277ed5ebf49f7e63f560268dbc345e8.
- Trials: Bathroom-V2 and LearnToAsk-V2, explicitly selected by the user.
- Existing dependencies only: Bathroom requires the delivered Timeline and bounded Loop capabilities; LearnToAsk requires delivered Timeline and linear Quest execution. Existing graph validation/preflight remains mandatory in Unity. No blanket Epic 3 gate, deferred Stories 3.4/3.5/4.1 work, or Story 4.2 startup migration was introduced.
- Legacy Action/Quest controllers and Quiz/Exploration/shared managers remain. Both original graph assets remain unchanged. Only the English parity inventory gains current continuation evidence; the bilingual matrix is not modified.

The earlier safe-stop handoff remains a historical snapshot. Its interrupted-work and missing-metadata notes are superseded here.

## Delivered behavior

Hints/presentation: activation-safe three 0.3-second on/off Outline cycles, actual assigned legacy sound at0.6 * MaxVolume, restoration and cleanup; normalized hold progress and contact eligibility/reset; quest bubble/progress at mapped anchors and profile-driven automatic visual hints through the existing accepted command/audit path. Bathroom maps water/soap/faucet effects to actual successful touch, not graph activation. LearnToAsk retains existing ArrowPath and gains the existing sensor collector/streamer without a second camera.

Lifecycle/sensors: one V2 target owner, multiple active binding targets, immutable runtime/session/run/node/activation identity and buffer rotation on transitions/pause; matching elapsed axis for time_offset and hint timing. Successful terminal behavior, one ended/stop and guarded 3-second lobby return; no second legacy Firestore save. Cleared/replaced sessions cannot restart stale media or queued RTDB controls. Existing installer/TimeManager startup responsibilities remain, and Quiz/Exploration behavior is preserved.

Web reporting: shared runtime adapter, logical Quest mapping, successful assisted status, separate total and hint-response timing, unavailable optional values, preserved historical records; corrected telemetry callback/scope association and correlated alerts; missing child-profile detail access fails closed except authorized admin.

Voice/control: configured/default 5-second activity-based silence reminders, positive periodic cadence, duplicate activation/config protection, busy/cancel/match/reconnect cleanup; post-playout correlated ON_REMINDER evidence and exactly one existing accepted verbal-hint audit/count without replay. Therapist SET_VOLUME and SPEAK_SCRIPT use typed correlated reliable LiveKit commands with validation/dedupe/feedback; exact NPC route required. Independent script cancellation is scoped to activation/sequence/NPC on node/pause/unload/reconfiguration, including touch nodes. Legacy non-V2 packet behavior remains supported.

## Accepted user decisions and implementation rulings

- LearnToAsk preserves its original finish, with no separate congratulations screen; Bathroom uses its existing legacy congratulations visual.
- Activity-based voice reminder reset/suppression was explicitly approved, replacing recognized-category reset semantics. Positive timeout keeps repeated reminders; 0 is a single immediate reminder per activity window to prevent an uncontrolled loop. Verify the0 edge in the focused user tests.
- Trial dependencies are selected by actual node use. Deferring unused advanced stories does not waive asset validation or any cutover requirement.
- The availability-independent binding lookup is additive; start-time Resolve/preflight stays strict. Sensors and presentation need active-source observation without weakening activation ownership.
- Exact script route failure is rejected rather than publishing onto another NPC's existing route.
- Sensor-only RTDB fields are synchronized with a TypeScript snapshot interface. Python has no RTDB sensor handler; C#/Python/TS voice packet shapes are synchronized.

## Independent reviews

Each coherent set received one independent review; only material fixes were re-reviewed.

1 Hints/scenes: fixed premature physical effects, then scoped re-review clean.
2 Lifecycle/sensors: fixed post-clear delayed handshake media restart, then scoped re-review clean.
3 Web reporting: fixed total/hint response conflation, unknown-as-zero display, session clock reset and alert correlation; fixes inspected and covering tests pass.
4 Voice/control: fixed invalid C# test metadata assertions, empty-scope cancellation overwriting a valid offline cancel, route failure publishing, non-strict reminder evidence, and unbounded Python waits. Scoped re-review identified fixture-router wiring and null callback decode regressions; both corrected and final scoped re-review reported no new material breakage.

Unity compilation and EditMode execution were deliberately not performed. Static review does not establish a successful Unity build.

## Fresh verification

- Python: 61 passed across 8 focused V2 contract/runtime/silence/agent/script/audio/hint/reconciliation test files, 8.41 seconds. 25 warnings are dependency deprecations, not test failures. Used the existing read-only canonical environment through uv --no-sync, bytecode disabled and pytest cache provider disabled. No dependencies installed.
- Web: 84 passed across 6 focused builder/remote/telemetry/reporting/live-snapshot/access test files; mirrored worktrees excluded.
- Web tsc --noEmit and targeted ESLint: exit 0.
- Ruff on changed Python source/tests: passed after import-only blank-line formatting in two test files; runtime/test semantics unchanged.
- Both repository git diff --check: pass, with Git CRLF conversion warnings only.
- All new C# script files have .meta. Newtonsoft JSON is already provided by Packages/packages-lock.json version 3.2.2; no package modifications were made.
- GitNexus full indexes were restored successfully after the safe stop. A subsequent incremental refresh reproduced LOWER/Invalid UTF-8; a final full restore is recorded in the verification addendum below. Scope detection is recorded there too. New/untracked assets are explicitly listed below and cannot be inferred from tracked-symbol counts alone.

Credential-dependent test_tts was not run in the final focused pass; the earlier full-suite attempt lacked Google ADC. This is not a claim of an entire Python suite pass. No full Unity suite, browser session, deployed Firebase readback/rules, or real-room/headset exercise was run.

## Smallest user verification sequence

1 Compile Unity normally. Run focused EditMode filters: VoiceQuestTransportV2Tests, LiveKitLessonRemoteBridgeV2Tests, LessonGraphTherapistControlsV2Tests, LessonGraphVoiceReminderAuditV2Tests, VoiceQuestSourceV2Tests, LessonRemoteContractV2Tests. Then the changed presentation/lifecycle filters QuestSourceV2Tests, HoldTouchQuestSourceV2Tests, LessonGraphSensorLifecycleV2Tests; existing telemetry adapter/writer and remote command tests cover affected shared flows.
2 Open both trial scenes and run their existing graph validator/preflight. Verify Timeline expected signals and Bathroom's bounded wet-hands Loop. No deferred checkpoint recovery/editor/handoff/startup work is required.
3 Headset: actual touch timing of faucet/soap; hold 0–100% and contact reset; bubble/anchor positions; 3-cycle light/sound, guidance and volume profiles; no mesh disabled; pause/skip/rebind/exit cleanup. LearnToAsk path remains and its finish has no congratulations screen; Bathroom does.
4 Real room: actual speech resets configured/default reminders, no overlap while user/NPC speaks or evaluation runs; one accepted reminder audit/count; volume/script scope, exact route, malformed/duplicate/stale/early rejection; cancel script while touch-node/pause/unload/offline/reconfigure; no delayed old speech/media effects.
5 Verify 2-second sensor snapshots and persisted alerts against exact session/run/node/activation/binding, including Bathroom's two completion sources; no mixed old-window observations after pause or transition. Check live and expert/parent history/report views on legacy and V2 sessions, with assisted success and unknown timing/counts displayed correctly.
6 Firestore/RTDB readback, allowed/denied access, retry/reconnect/session reuse: one terminal record/ended, no duplicate node/quest/audit records or same-session legacy overwrite. Run focused Quiz and Exploration regressions using retained managers/listeners.

Record these results against the English parity matrix. Do not retire controllers or mark Story 2.4 done until required dependencies and parity are actually verified, user accepts cutover, all remaining serialized/script/web consumers are migrated, and post-removal regressions pass.

## Workspace/environment preservation

An ignored empty LiveKitAgent/.venv created by the earlier uv probe remains; cleanup was blocked and no destructive retry was made. The canonical environment was used without syncing/installing. Do not delete either environment without distinguishing task-created files from preexisting user data. Review packages/plan under ignored _bmad-output are local evidence; this handoff is a normal untracked repository document and must be preserved with the code diff.

## Changed files

Unity/Python (current tracked and untracked status):

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
 M _bmad-output/implementation-artifacts/2-4-legacy-v2-parity-matrix.md
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphLifecycleV2.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphLifecycleV2.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphSensorBridgeV2.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/LessonGraphSensorBridgeV2.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation/LessonGraphHintPresenterV2.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation/LessonGraphHintPresenterV2.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphSensorLifecycleV2Tests.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphSensorLifecycleV2Tests.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphTherapistControlsV2Tests.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphTherapistControlsV2Tests.cs.meta
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphVoiceReminderAuditV2Tests.cs
?? Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphVoiceReminderAuditV2Tests.cs.meta
?? LiveKitAgent/src/silence_reminder_v2.py
?? LiveKitAgent/tests/test_silence_reminder_v2.py
?? docs/superpowers/handoffs/2026-10-07-story-2-4-safe-stop.md
?? docs/superpowers/handoffs/2026-10-07-story-2-4-trial-parity.md
```

Web:

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

## Verification addendum after final review

Final Unity/Python full index restore succeeded: 28,633 nodes, 52,162 edges and 300 flows. Web full refresh succeeded: 1,839 nodes, 3,870 edges and 148 flows. The incremental Invalid UTF-8 failure was recovered with a full index rebuild; no source files or agent instructions were altered by indexing.

Fresh final detect_changes(scope=all):

- Unity/Python: changed_files=34, changed_count=289, affected_count=37, risk_level=critical.
- Web: changed_files=12, changed_count=30, affected_count=36, risk_level=critical.

The aggregate CRITICAL blast radius includes shared runner/media/session and reporting flows. The changed scopes were independently reviewed; this risk rating still requires the focused user-run Unity and integrated acceptance checks above. New/untracked files are listed explicitly rather than presumed covered by tracked diff counts. No commit or staging was performed.
