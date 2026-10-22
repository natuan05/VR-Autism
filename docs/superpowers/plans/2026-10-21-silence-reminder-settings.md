# Voice Silence Reminder Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Let assigned experts enable/disable spoken silence reminders and set a per-child delay on the web, delivered with voice activations.

**Architecture:** Extend existing child lesson parameters and SET_ACTIVE_QUEST without changing contract version 2. Keep activation preference separate from Agent readiness, and replay retained voice activation after authenticated cache Agent HELLO. Preserve existing idempotency and launch-time configuration.

**Tech Stack:** Next.js/React/TypeScript, Firestore, Unity C#, Python LiveKit, Vitest, pytest, Unity EditMode.

**Spec:** `docs/superpowers/specs/2026-10-21-silence-reminder-settings-design.md`.

## Global Constraints

- Firestore `actions.enable_speech_silence_reminder` and optional V2 field of the same name default to true.
- `speech_silence_timeout` accepts -1 or finite 1–60 seconds for new web writes; -1 resolves to 5 seconds in Unity.
- Preserve zero-timeout semantics for existing transport callers; never use zero to disable reminders.
- Configuration applies at next lesson launch; all real-time quest events use LiveKit DataPacket.
- Only assigned experts may save/import; don't capture an additional microphone or edit vendor files.
- Do not run Unity batch mode; user performs Unity compilation and focused EditMode/device checks.

## Review Focus

- Reconnection/cache readiness must not override a child's disabled reminder preference (Task 1).
- Missing fields remain enabled/5 seconds and replayed settings cannot mutate a live activation (Task 1).
- Late/restarted Agent receives full retained activation; stale/foreign HELLO and terminal activations don't replay (Task 2).
- Config import cannot bypass validation or erase unrelated lesson categories (Task 3).
- Visual hints remain independent, and disabled reminder UI retains delay (Tasks 2/3).

---

### Task 1: Agent contract and activation preference

**Files:** `LiveKitAgent/src/voice_contract_v2.py`, `voice_quest_runtime_v2.py`, `silence_reminder_v2.py`, `agent_v2.py`; corresponding `LiveKitAgent/tests/test_*v2.py`.

**Interfaces:** `SetActiveQuest.enable_speech_silence_reminder: bool = True`; `ActivationSilenceReminderV2.activate(activation_id: str, timeout_seconds: float, *, enabled: bool = True) -> bool`. Existing `set_enabled()` remains the readiness gate.

- [x] Add failing tests: absent flag is true, false parses, non-booleans rejected; replay changed flag raises; disabled activation never schedules even after `set_enabled(True)`, next enabled activation schedules its configured delay.
- [x] Run `uv run pytest tests/test_voice_contract_v2.py tests/test_voice_quest_runtime_v2.py tests/test_silence_reminder_v2.py tests/test_agent_v2.py -q` from LiveKitAgent; observe new failures.
- [x] Add flag to DTO/runtime identity, pass it from packet handler to scheduler, and gate scheduling independently from readiness.
- [x] Run the same tests plus fixed-audio lifecycle/reconciliation suites and scoped ruff; expect green.

### Task 2: Unity settings propagation and late-Agent replay

**Files:** `Assets/Project/Scripts/Core/Models/LessonParameters.cs`; `Gameplay/LessonGraphV2/Questing/{Sources/VoiceQuestSourceV2.cs,Voice/VoiceQuestTransportContracts.cs,Voice/LiveKitVoiceQuestTransportV2.cs}`; `Gameplay/LessonGraphV2/AudioCache/FixedAudioPlaybackCoordinatorV1.cs`; relevant Editor tests.

**Interfaces:** `ActionParams.EnableSpeechSilenceReminder = true`; append optional `bool enableSpeechSilenceReminder = true` to VoiceQuestActivation constructor; add serialized activation flag. Coordinator emits `AgentReady` only after accepted/authenticated HELLO; transport replays `_desired` only for a current nonterminal activation.

- [x] Add Editor tests for missing/default/false dictionary fields, source propagation and false on reconnect packet; test late-Agent callback replays the full activation while terminal/suspended states do not.
- [x] Add coordinator test that only authenticated current-session/current-instance HELLO emits readiness; Unity execution is deferred by repository policy.
- [x] Parse flag, propagate it through activation copy and packet publishing; wire/unwire coordinator event with transport lifecycle.
- [x] Inspect scoped C# diff and run `git diff --check`; user filters: `VoiceQuestSourceV2Tests`, `VoiceQuestTransportV2Tests`, relevant fixed-audio coordinator tests.

### Task 3: Web controls and validated persistence/import

**Files:** `D:/Lab/VRA-web/src/lib/silence-reminder-settings.ts` + test; `src/actions/expert.ts` + focused tests; `src/app/dashboard/expert/_components/stats/LessonParametersEditor.tsx` + test; `src/types/lesson-graph-v2.ts`.

**Interfaces:** `normalizeSilenceReminderSettings(params: unknown): Record<string, unknown>` returns a copied parameter object with normalized actions, rejects invalid flag/time, preserves other fields. Both save and import use it.

- [x] Add failing Vitest tests for defaults, custom 15 seconds/false, invalid 0/61/NaN/string/boolean, preserving other settings; action save/import authorization and no write on invalid input; rendered control labels/disabled state.
- [x] Run `npm test -- src/lib/silence-reminder-settings.test.ts src/actions/expert.test.ts src/app/dashboard/expert/_components/stats/LessonParametersEditor.test.tsx`; observe failures.
- [x] Add server normalization on save/import, new toggle/defaultable seconds slider and next-launch text; rename visual toggle to `Tự động gợi ý bằng hình ảnh`. Disable delay editing when off while retaining stored value. Add optional TS wire flag.
- [x] Run focused Vitest, TypeScript and scoped ESLint; expect green or report pre-existing failures separately.

### Task 4: Integration review and handoff

- [x] Run focused Python/web regression suites, inspect diffs and `git diff --check` in both repos.
- [x] Run GitNexus detect_changes in both repos and confirm expected contract/settings scope. Refresh indexes with `--index-only` after added source files.
- [x] One independent review across both repositories; fix material findings with focused regression coverage.
- [x] Record outcomes here and deliver changed files, checks, and smallest Unity/manual checks. Leave scoped changes reviewable; no push or deployment.

## Execution record

- Baseline: 36 focused Python tests and 13 web expert-action tests passed.
- Working branches: `codex/web-silence-reminder-config` in both repositories; existing scene/recovery/docs changes preserved.
- Native execution authorized by user's “write plan then implement”; no additional planning approval round.
- Impact: Publish (Unity) and _is_busy (scheduler) HIGH; user warned before edits. Most other targets LOW. Graph partial results on data types are supplemented with direct source/reference inspection.
- Tasks 1–3 implemented. New Python/web tests were observed failing before production changes, then passing. Unity tests were authored and inspected; execution is deferred to the user by repository policy.
- Final focused runs: 66 Python tests passed; 46 Vitest tests passed. Scoped Ruff/ESLint and `git diff --check` passed.
- Source-only TypeScript check: 201 files, 0 diagnostics. Full `tsc --noEmit --incremental false` is blocked by existing `.next/{dev/,}types/validator.ts` references to absent `parent/children/[id]/charts/page.js`; generated directories were not modified.
- One independent review completed with no material findings. Legacy non-cache readiness, existing authorization/import semantics, and pre-existing generated files were outside the feature scope.
- Index recovery completed: dedicated web FTS repair followed by full `--force --index-only` rebuilds restored both repositories. GitNexus detect_changes ran in both: web 12 mapped symbols/7 flows; VR 62 mapped symbols/15 flows; aggregate HIGH reflects shared activation/cache paths and pre-existing documentation changes. Scoped manual diff confirmed only the planned product files; untracked additions are listed below. No unexpected product subsystem changes. All tasks complete; Unity compilation, Editor execution, and device verification remain explicitly deferred to the user.

## Changed-file manifest

VR-Autism source:
- `Assets/Project/Scripts/Core/Models/LessonParameters.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/VoiceQuestSourceV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/VoiceQuestTransportContracts.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/FixedAudioPlaybackCoordinatorV1.cs`
- `LiveKitAgent/src/voice_contract_v2.py`
- `LiveKitAgent/src/voice_quest_runtime_v2.py`
- `LiveKitAgent/src/silence_reminder_v2.py`
- `LiveKitAgent/src/agent_v2.py`

VR-Autism tests:
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestSourceV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestTransportV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Tests/Editor/TtsUnityCacheIntegrationV1Tests.cs`
- `LiveKitAgent/tests/test_voice_contract_v2.py`
- `LiveKitAgent/tests/test_voice_quest_runtime_v2.py`
- `LiveKitAgent/tests/test_silence_reminder_v2.py`
- `LiveKitAgent/tests/test_agent_v2.py`

VRA-web (`D:/Lab/VRA-web/`):
- `src/actions/expert.ts`
- `src/types/lesson-graph-v2.ts`
- `src/lib/silence-reminder-settings.ts`
- `src/app/dashboard/expert/_components/stats/LessonParametersEditor.tsx`
- `src/lib/silence-reminder-settings.test.ts`
- `src/actions/expert-silence-reminder.test.ts`
- `src/app/dashboard/expert/_components/stats/LessonParametersEditor.test.tsx`

## Focused user verification

Run Unity Editor filters `VoiceQuestSourceV2Tests`, `VoiceQuestTransportV2Tests`, and `TtsUnityCacheIntegrationV1Tests.OnlyCurrentAuthenticatedHelloSignalsVoiceReplayReadiness` after normal Unity compilation.

On device, save enabled/15 seconds on the child's web page, launch a voice quest and verify the first silence reminder waits 15 seconds after NPC speech. Interrupt it by speaking. Save disabled, relaunch, wait longer than 15 seconds and verify no automatic spoken reminder. Confirm manual verbal hints and visual auto hints still work independently. Restart the Agent during an active cache-aware V2 quest and confirm activation/settings recover without replaying an already completed opening. Updating the Agent before deploying the Unity build avoids sending the new optional field to an old strict V2 parser.
