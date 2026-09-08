# Voice Phrase Storage and Transport V2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Introduce child-specific additive voice phrases and an activation-correlated LiveKit voice transport for LessonGraphV2 without changing the legacy Voice Agent flow.

**Architecture:** Firestore lesson defaults remain canonical. The Web stores only child additions in `child_phrase_sets`; Unity resolves one immutable effective phrase snapshot while loading the selected scene; LessonGraphV2 sends only the current quest goal, phrases, and activation id to `agent_v2.py`. All V2 packets use a versioned, typed contract over the existing LiveKit room.

**Tech Stack:** Next.js/TypeScript/Firebase Admin, Unity 6/C#/Firestore/LiveKit, Python/LiveKit Agents/pytest.

**Spec:** `docs/VOICE_PHRASE_STORAGE_V2_PLAN.md`; Story 1.5 in `_bmad-output/planning-artifacts/epics.md`.

## Global Constraints

- Keep `LiveKitAgent/src/agent.py`, legacy `VoiceQuest`, legacy quick phrases, and `ILiveKitRoomClient` behavior unchanged.
- Make all new voice behavior use `LiveKitAgent/src/agent_v2.py`.
- Do not switch `LiveKitAgent/Dockerfile` to V2 until end-to-end validation is accepted.
- Only `LiveKitService` may call `Microphone.Start()`.
- Default phrases are read-only in the specialist UI. Specialists can add, reorder, and remove only child additions.
- Effective phrases are `normalize(defaults + additions)` with stable-order deduplication.
- Use LessonGraphV2 `binding_id`; never use quest array index, title, or a new `quest_key`.
- Phrase edits affect only the next session. Never mutate a running session's resolved snapshot.
- The Agent receives no child, session, lesson, scene, or Firestore identifiers.
- Every V2 packet uses uppercase `event`, snake_case fields, `contract_version: 2`, and `activation_id`.
- General phrases stay child-specific Web buttons sent as `SPEAK_SCRIPT`; never merge them into quest phrases.
- Preserve reliable LiveKit DataPacket transport and Unity main-thread dispatch.

## Risk and No-Change Guardrails

GitNexus impact analysis classified `QuestSourceV2.Terminate` as HIGH risk: 38 upstream symbols and the `QuestNodeExecutor` flow are affected. Do not edit it. `VoiceQuestSourceV2` must observe its own `Terminated` event and send cancellation through the V2 transport.

Do not modify these legacy files unless a separately approved migration requires it:

- `LiveKitAgent/src/agent.py`
- `LiveKitAgent/Dockerfile`
- `Assets/Project/Scripts/Quests/VoiceQuest.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitRoomClient.cs`
- `Assets/Project/Scripts/Session/SessionContext.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/LessonGraphRunnerInstaller.cs`
- `D:/Lab/VRA-web/src/actions/expert.ts`

## File Map

### Web Dashboard — `D:/Lab/VRA-web`

Create:

- `src/types/voice-phrases.ts` — Firestore and UI types for defaults, additions, and resolved phrases.
- `src/lib/voice-phrases.ts` — pure normalization, validation, and merge functions.
- `src/lib/voice-phrases.test.ts` — resolver and validation tests.
- `src/actions/voice-phrases.ts` — authorized reads and transactional writes to `child_phrase_sets`.
- `vitest.config.ts` — test runner configuration.

Modify:

- `src/app/dashboard/expert/lessons/page.tsx` — load V2 child phrase sets with lesson data.
- `src/app/dashboard/expert/lessons/_components/LessonsList.tsx` — show immutable defaults and editable additions for V2 lessons; retain the legacy branch.
- `src/app/dashboard/expert/session/[id]/page.tsx` — stop copying defaults for V2 lessons and load resolved/general V2 data.
- `src/app/dashboard/expert/_components/live/NPCChatPanel.tsx` — consume V2 general phrases while retaining legacy fallback.
- `package.json`, `package-lock.json` — add Vitest and `npm run test`.

### Unity VR Client — `D:/Lab/VR-Autism`

Create:

- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases/VoicePhraseContractsV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases/VoicePhraseResolverV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases/VoicePhraseSnapshotStoreV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases/FirestoreVoicePhraseLoaderV2.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitDataPacketClientV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/VoiceQuestTransportContracts.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/VoiceQuestSourceV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoicePhraseResolverV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LiveKitVoiceQuestTransportV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestSourceV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestFirstWinTests.cs`

Modify:

- `Assets/Project/Scripts/Cloud/FirebasePaths.cs` — add the `child_phrase_sets` collection constant.
- `Assets/Project/Scripts/UI/SceneMenuController.cs` — concurrent scene/data load, V2 phrase resolution, and activation gate.
- `Assets/Project/Scripts/UI/PairingUI.cs` — surface retryable V2 startup failures.
- `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs` — implement the narrow raw V2 packet interface while preserving legacy APIs.

### Python Voice Agent — `D:/Lab/VR-Autism/LiveKitAgent`

Create:

- `src/voice_contract_v2.py` — strict packet DTOs and parser.
- `src/voice_quest_runtime_v2.py` — single-active-activation state machine.
- `tests/test_voice_contract_v2.py`
- `tests/test_voice_quest_runtime_v2.py`
- `tests/test_agent_v2.py`

Modify:

- `src/agent_v2.py` — connect V2 packets and runtime to the existing speech/LLM pipeline.

### Documentation

Modify:

- `docs/VOICE_PHRASE_STORAGE_V2_PLAN.md` — freeze exact schema, normalization, revision, and startup rules.
- `docs/db/DATABASE_SCHEMA_DESIGN.md` — document canonical lesson defaults and additive child documents.
- `docs/db/FIRESTORE_LIVE_SCHEMA.md` — document collection paths, fields, indexes, and ownership.

## Contract Definitions

### Firestore

`lessons/{lessonId}` owns defaults:

```json
{
  "voice_schema_version": 2,
  "voice_revision": 7,
  "quests": [
    {
      "binding_id": "washing-hand.turn-on-water",
      "title": "Turn on water",
      "goal": "The child asks to turn on the water",
      "default_phrases": ["Please turn on the water"]
    }
  ]
}
```

`child_phrase_sets/{childId}__{lessonId}` stores additions only:

```json
{
  "schema_version": 2,
  "scope": "lesson",
  "child_id": "child-123",
  "lesson_id": "lesson-456",
  "revision": 3,
  "quest_additions": [
    {
      "binding_id": "washing-hand.turn-on-water",
      "phrases": ["Can you turn on the tap?"]
    }
  ],
  "updated_at": "server timestamp",
  "updated_by": "expert uid"
}
```

`child_phrase_sets/{childId}__general` stores general buttons:

```json
{
  "schema_version": 2,
  "scope": "general",
  "child_id": "child-123",
  "revision": 2,
  "phrases": ["Take a breath", "Try again"],
  "updated_at": "server timestamp",
  "updated_by": "expert uid"
}
```

Normalization is deterministic: trim Unicode whitespace, reject empty strings, compare case-insensitively, preserve the first spelling and original order, cap each phrase at 240 characters, and cap each quest's additions at 50 phrases.

### LiveKit V2 Packets

Unity to Agent:

```json
{"event":"SET_ACTIVE_QUEST","contract_version":2,"activation_id":"uuid","quest_goal":"Ask to turn on the water","phrases":["Please turn on the water"]}
```

```json
{"event":"CANCEL_ACTIVE_QUEST","contract_version":2,"activation_id":"uuid","reason":"lost_race"}
```

Agent to Unity:

```json
{"event":"QUEST_STATUS","contract_version":2,"activation_id":"uuid","status":"ACTIVE"}
```

```json
{"event":"QUEST_MATCHED","contract_version":2,"activation_id":"uuid"}
```

Use topic `lesson-graph-v2.voice` and reliable delivery. Replaying the same activation is idempotent: acknowledge it without replaying the opening phrase. Unknown versions, malformed payloads, stale activation ids, and duplicate terminal signals are logged and ignored.

## Task 1: Freeze the V2 Storage and Packet Contract

**Files:** documentation files listed above.

- [x] Add the exact Firestore examples and normalization limits from this plan.
- [x] Define ownership: lesson authors own defaults; specialists own additions for authorized children.
- [x] Record that edits become visible only on the next session.
- [x] Record that full resolved session snapshot persistence is backlog, outside Story 1.5.
- [x] Record all four LiveKit packet shapes and topic.
- [x] Confirm the docs contain no `quest_key`, positional quest mapping, or replace-default semantics.

Verification:

```powershell
rg -n "voice_schema_version|child_phrase_sets|binding_id|activation_id|next session|lesson-graph-v2.voice" docs/VOICE_PHRASE_STORAGE_V2_PLAN.md docs/db/DATABASE_SCHEMA_DESIGN.md docs/db/FIRESTORE_LIVE_SCHEMA.md
rg -n "quest_key|replace defaults|quest index" docs/VOICE_PHRASE_STORAGE_V2_PLAN.md docs/db/DATABASE_SCHEMA_DESIGN.md docs/db/FIRESTORE_LIVE_SCHEMA.md
```

Commit:

```powershell
git add docs/VOICE_PHRASE_STORAGE_V2_PLAN.md docs/db/DATABASE_SCHEMA_DESIGN.md docs/db/FIRESTORE_LIVE_SCHEMA.md
git commit -m "docs(voice): freeze v2 phrase contract"
```

## Task 2: Implement and Test the Web Phrase Domain

**Files:** `src/types/voice-phrases.ts`, `src/lib/voice-phrases.ts`, `src/lib/voice-phrases.test.ts`, `vitest.config.ts`, `package.json`, `package-lock.json`.

- [x] Define `LessonVoiceQuestV2`, `ChildLessonPhraseSetV2`, `ChildGeneralPhraseSetV2`, and `ResolvedVoiceQuestV2`.
- [x] Implement pure `normalizePhrase`, `dedupePhrases`, `validateAdditions`, and `resolveEffectivePhrases` functions.
- [x] Reject additions whose `binding_id` does not exist in the selected lesson.
- [x] Reject duplicate lesson `binding_id` values because resolution would be ambiguous.
- [x] Add tests for trimming, case-insensitive dedupe, stable order, empty input, unknown binding, limits, and defaults-first merge.
- [x] Add `"test": "vitest run"` without changing existing scripts.

Required interface:

```ts
export function resolveEffectivePhrases(
  quests: readonly LessonVoiceQuestV2[],
  additions: readonly QuestPhraseAdditionsV2[],
): readonly ResolvedVoiceQuestV2[];
```

Verification:

```powershell
npm run test -- src/lib/voice-phrases.test.ts
npm run lint
```

Commit:

```powershell
git add src/types/voice-phrases.ts src/lib/voice-phrases.ts src/lib/voice-phrases.test.ts vitest.config.ts package.json package-lock.json
git commit -m "feat(voice): add v2 phrase resolver"
```

## Task 3: Add Authorized Web Persistence

**Files:** `src/actions/voice-phrases.ts` and its domain test dependencies.

- [x] Reuse current expert authentication and child-access checks from the refactored action layer.
- [x] Read deterministic document ids: `${childId}__${lessonId}` and `${childId}__general`.
- [x] Return an empty additions document when the lesson-scoped document is absent.
- [x] Use a Firestore transaction to compare the submitted `revision`, increment it, and write server timestamps.
- [x] Store additions only; never copy lesson defaults into child documents.
- [ ] Validate all bindings and phrases on the server before writing.
- [x] Leave `updateChildQuickPhrases` and `syncAndGetChildPhrases` untouched for legacy callers.

Required actions:

```ts
export async function getChildPhraseSetsV2(childId: string, lessonId: string): Promise<{
  lesson: ChildLessonPhraseSetV2;
  general: ChildGeneralPhraseSetV2;
}>;

export async function saveChildLessonPhraseSetV2(input: SaveChildLessonPhraseSetV2): Promise<{ revision: number }>;

export async function saveChildGeneralPhraseSetV2(input: SaveChildGeneralPhraseSetV2): Promise<{ revision: number }>;
```

Verification:

```powershell
npm run test
npm run lint
npm run build
```

Commit:

```powershell
git add src/actions/voice-phrases.ts src/types/voice-phrases.ts src/lib/voice-phrases.ts
git commit -m "feat(voice): persist child additions"
```

## Task 4: Update the Specialist and Live Session UI

**Files:** the four Web page/component files in the file map.

- [x] Branch on `lesson.voice_schema_version === 2`; preserve the current component behavior for legacy lessons.
- [x] Render defaults as locked rows with a clear “Default” label and no delete control.
- [x] Render additions as editable rows; allow add, reorder, and delete.
- [ ] Save only additions with the last-read revision; show a reload message on revision conflict.
- [ ] In the live session, load V2 general phrases directly instead of invoking legacy default-copy synchronization.
- [x] Continue sending general buttons through existing `SPEAK_SCRIPT` behavior.
- [x] Use the refactored Web types (`unknown` plus narrowing) instead of introducing new `any` values.

Verification:

```powershell
npm run test
npm run lint
npm run build
```

Manual Web checks:

- [x] A specialist cannot delete a default phrase.
- [x] Adding a phrase does not duplicate defaults in Firestore.
- [x] A second browser with a stale revision receives a conflict instead of overwriting.
- [x] A legacy lesson still uses `child_profiles.quick_phrases` unchanged.
- [x] General buttons still publish `SPEAK_SCRIPT`.

Commit:

```powershell
git add src/app/dashboard/expert/lessons/page.tsx src/app/dashboard/expert/lessons/_components/LessonsList.tsx src/app/dashboard/expert/session/[id]/page.tsx src/app/dashboard/expert/_components/live/NPCChatPanel.tsx
git commit -m "feat(voice): edit additive phrases"
```

## Task 5: Resolve an Immutable Phrase Snapshot During Unity Scene Load

**Files:** four new `Phrases/*.cs` files, `FirebasePaths.cs`, `SceneMenuController.cs`, `PairingUI.cs`, and `VoicePhraseResolverV2Tests.cs`.

- [x] Run GitNexus impact analysis for every existing symbol before editing it.
- [x] Parse lesson defaults and child additions into typed DTOs keyed by `binding_id`.
- [ ] Resolve and freeze a read-only snapshot containing `lesson_id`, lesson `voice_revision`, child revision, and effective phrases by binding.
- [x] Start `SceneManager.LoadSceneAsync(sceneName)` immediately with `allowSceneActivation = false`.
- [x] In parallel, fetch the lesson and deterministic child phrase document.
- [x] For V2, set the snapshot before allowing scene activation; for legacy, retain current positional parsing and `SessionContext` behavior.
- [x] If the child additions fetch fails, log a warning and resolve defaults only.
- [ ] If the lesson fetch, duplicate binding validation, or defaults parsing fails, keep activation blocked, show a retryable message, and release/unload the pending operation safely.
- [x] Do not put child/session identifiers into the Agent-facing snapshot view.

Required interfaces:

```csharp
public interface IVoicePhraseSnapshotV2
{
    bool TryGet(string bindingId, out VoiceQuestPhraseSnapshotV2 snapshot);
}

public static IReadOnlyDictionary<string, VoiceQuestPhraseSnapshotV2> Resolve(
    IReadOnlyList<LessonVoiceQuestV2> quests,
    IReadOnlyList<QuestPhraseAdditionsV2> additions);
```

Tests must use `[UnityTest] IEnumerator`, bounded frame completion, and complete every `TaskCompletionSource` on all branches.

Verification in Unity Editor:

- [ ] Open `Window > General > Test Runner`.
- [ ] Run EditMode tests filtered to `VoicePhraseResolverV2Tests`; all pass.
- [ ] Start a V2 lesson with an existing additions document; the scene activates with defaults plus additions.
- [ ] Start without an additions document; the scene activates with defaults only.
- [ ] Simulate lesson read failure; the scene does not activate and retry is available.
- [ ] Edit phrases after the scene starts; the running snapshot does not change.

Commit:

```powershell
git add Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases Assets/Project/Scripts/Cloud/FirebasePaths.cs Assets/Project/Scripts/UI/SceneMenuController.cs Assets/Project/Scripts/UI/PairingUI.cs Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoicePhraseResolverV2Tests.cs
git commit -m "feat(voice): resolve session phrases"
```

## Task 6: Add the Typed Unity LiveKit V2 Transport

**Files:** `ILiveKitDataPacketClientV2.cs`, `VoiceQuestTransportContracts.cs`, `LiveKitVoiceQuestTransportV2.cs`, `LiveKitService.cs`, and `LiveKitVoiceQuestTransportV2Tests.cs`.

- [x] Add a narrow raw packet interface implemented by `LiveKitService`; do not replace `ILiveKitRoomClient`.
- [x] Publish V2 packets reliably on `lesson-graph-v2.voice`.
- [x] Parse only matching-topic packets with `contract_version == 2`.
- [x] Queue LiveKit callbacks and drain them from `Update()` on Unity's main thread.
- [x] Track one current activation id and terminal state.
- [ ] Ignore unknown, stale, malformed, and duplicate terminal packets.
- [ ] On reconnect, resend the current active request with the same activation id.
- [x] Ensure replay acknowledgement cannot complete a quest twice.
- [x] Preserve all legacy switch cases and microphone capture in `LiveKitService`.

Required interface:

```csharp
public interface IVoiceQuestTransport
{
    event Action<VoiceQuestSignal> SignalReceived;
    Task ActivateAsync(VoiceQuestActivation request, CancellationToken cancellationToken);
    Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken);
}
```

Verification in Unity Editor:

- [ ] Run EditMode tests filtered to `LiveKitVoiceQuestTransportV2Tests`.
- [ ] Confirm tests cover exact serialization, malformed/unknown version, stale id, duplicate match, cancellation, reconnect replay, and main-thread delivery.
- [ ] Search for microphone ownership:

```powershell
rg -n "Microphone\.Start" Assets/Project/Scripts
```

Expected result: only `LiveKitService.cs`.

Commit:

```powershell
git add Assets/Project/Scripts/Cloud/LiveKit/ILiveKitDataPacketClientV2.cs Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LiveKitVoiceQuestTransportV2Tests.cs
git commit -m "feat(voice): add v2 livekit transport"
```

## Task 7: Add VoiceQuestSourceV2 Without Touching the Base Lifecycle

**Files:** `VoiceQuestSourceV2.cs`, `VoiceQuestSourceV2Tests.cs`, `VoiceQuestFirstWinTests.cs`.

- [x] Resolve the quest's snapshot by the source's stable `BindingId`.
- [x] Forward the fresh activation id created by `QuestNodeExecutor` and send goal plus the single effective phrase list.
- [x] Complete only when `QUEST_MATCHED.activation_id` equals the current activation.
- [x] Subscribe to the source's existing `Terminated` event and send `CANCEL_ACTIVE_QUEST` for cancelled/failed runs.
- [x] Unsubscribe on disposal/destruction and ignore late async callbacks.
- [x] Do not edit `QuestSourceV2.Terminate`.
- [ ] Prove Voice-vs-Touch/Hold first-win: after another source wins, voice cancellation is sent and a later match cannot complete again.

Verification in Unity Editor:

- [ ] Run EditMode tests filtered to `VoiceQuestSourceV2Tests`.
- [ ] Run EditMode tests filtered to `VoiceQuestFirstWinTests`.
- [ ] Run all `LessonGraphV2` EditMode tests; all pass.

Commit:

```powershell
git add Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/VoiceQuestSourceV2.cs Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestSourceV2Tests.cs Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestFirstWinTests.cs
git commit -m "feat(voice): add v2 quest source"
```

## Task 8: Implement the Python V2 Contract and Runtime

**Files:** `voice_contract_v2.py`, `voice_quest_runtime_v2.py`, three new test files, and `agent_v2.py`.

- [x] Parse V2 messages strictly without importing legacy Agent state.
- [x] Keep exactly one active activation with goal, phrases, and terminal status.
- [x] Use the same effective phrase list for the opening utterance, reminders/hints, and LLM evaluation examples.
- [ ] On a new activation, cancel/replace the previous activation atomically.
- [x] On cancellation, make that activation permanently unable to emit success.
- [x] On same-id replay, send `QUEST_STATUS: ACTIVE` without replaying the opening.
- [x] Send `QUEST_MATCHED` once, then a terminal `QUEST_STATUS`, both with the current activation id.
- [x] Ignore stale LLM/transcript callbacks after activation changes.
- [x] Keep `agent.py` and Dockerfile unchanged.

Core state transition:

```python
if result.activation_id != runtime.active_activation_id:
    return
if runtime.mark_matched(result.activation_id):
    await publish_quest_matched(result.activation_id)
```

Verification:

```powershell
uv run pytest tests/test_voice_contract_v2.py tests/test_voice_quest_runtime_v2.py tests/test_agent_v2.py -q
uv run ruff check src/voice_contract_v2.py src/voice_quest_runtime_v2.py src/agent_v2.py tests/test_voice_contract_v2.py tests/test_voice_quest_runtime_v2.py tests/test_agent_v2.py
uv run pytest -q
```

Commit:

```powershell
git add LiveKitAgent/src/voice_contract_v2.py LiveKitAgent/src/voice_quest_runtime_v2.py LiveKitAgent/src/agent_v2.py LiveKitAgent/tests/test_voice_contract_v2.py LiveKitAgent/tests/test_voice_quest_runtime_v2.py LiveKitAgent/tests/test_agent_v2.py
git commit -m "feat(agent): correlate v2 voice quests"
```

## Task 9: Cross-Stack Validation and Story 1.5 Acceptance

- [ ] Start Web, VR, and `agent_v2.py` against one test room.
- [ ] Select a V2 lesson on Web; confirm scene download/load and phrase reads begin concurrently.
- [ ] Confirm scene activation waits for the resolved snapshot, not for a second post-load fetch.
- [ ] Confirm the opening, reminder, and evaluator all use the same resolved list.
- [ ] Complete a voice quest normally; exactly one graph completion occurs.
- [ ] Let Touch/Hold win while Voice is evaluating; cancellation is sent and late Voice success is ignored.
- [ ] Disconnect and reconnect LiveKit; the current activation resumes without replaying opening or duplicating completion.
- [ ] Edit additions during the session; verify they appear only in the next session.
- [ ] Run Web build, full Python tests, and all Unity LessonGraphV2 EditMode tests.
- [ ] Confirm legacy Voice Agent and legacy lesson phrase editing still work unchanged.

Refresh code intelligence after creating files:

```powershell
node .gitnexus/run.cjs analyze
node .gitnexus/run.cjs detect-changes --scope compare --base-ref main
```

Run the same commands from `D:/Lab/VRA-web`. Review both reports before final commits; unexpected HIGH/CRITICAL paths require stopping and reporting the blast radius.

Final commit after acceptance-only adjustments:

```powershell
git add Assets/Project/Scripts LiveKitAgent/src LiveKitAgent/tests docs
git commit -m "feat(voice): complete story 1.5"
```

## Completion Criteria

- Web stores no copied defaults in V2 child documents.
- Specialists cannot delete or replace defaults.
- Unity resolves by `binding_id` once per session and gates scene activation on a valid lesson snapshot.
- Agent V2 knows only activation id, goal, and phrases.
- All V2 events are versioned and activation-correlated.
- Cancellation, duplicates, stale callbacks, and reconnects cannot complete a quest twice.
- Voice participates safely in LessonGraphV2 first-win behavior.
- Legacy Agent, legacy Web data, and legacy Unity contracts remain operational.
- Docker deployment remains on legacy until the user separately approves the V2 switch.

### Review Findings

Checklist synchronized on 2026-09-05 from already-known implementation and review evidence; no new code review was performed. A checked implementation item means evidence exists for that item, not that Story 1.5 has passed full acceptance.

- [x] [Review][Decision] Agent V2 cutover model — Keep `agent_v2.py` as the isolated V2 entrypoint; keep production Docker/task automation on legacy `agent.py` until separate user-approved cutover.
- [x] [Review][Decision] Voice participation beside Touch/Hold — Keep `QuestNodeExecutor` as the sole existing first-win owner. Register Voice, Touch, and Hold as independent uniquely bound sources; do not add `CompositeQuestSourceV2`.
- [x] [Review][Decision] Recoverable V2 scene-load failure policy — Keep scene activation gated on snapshot success. On fatal phrase failure, mark the launch token failed, release the quarantined scene, disable its LessonGraph installer before first `Start`, unload it, and expose retry.
- [x] [Review][Decision] Snapshot owner — Unity resolves once and stores the typed immutable snapshot in `VoicePhraseSnapshotStoreV2`; do not modify legacy `SessionContext`.
- [ ] [Review][Patch] Hydrate the V2 editor from `child_phrase_sets` and retain the loaded revision [D:/Lab/VRA-web/src/app/dashboard/expert/lessons/_components/LessonsList.tsx:67]
- [ ] [Review][Patch] Save additions using canonical `quest.binding_id`, never `quest.id` or title [D:/Lab/VRA-web/src/app/dashboard/expert/lessons/_components/LessonsList.tsx:166]
- [ ] [Review][Patch] Replace hard-coded `expectedRevision: 0` and update local revision after each save [D:/Lab/VRA-web/src/app/dashboard/expert/lessons/_components/LessonsList.tsx:174]
- [ ] [Review][Patch] Persist V2 general phrases and feed V2 phrase data into the live-session UI [D:/Lab/VRA-web/src/app/dashboard/expert/session/[id]/page.tsx:96]
- [ ] [Review][Patch] Validate lesson additions against the canonical Firestore lesson inside the transaction, not client-supplied quests [D:/Lab/VRA-web/src/actions/voice-phrases.ts:68]
- [ ] [Review][Patch] Apply shared normalization, length, count, and deduplication rules to general phrases [D:/Lab/VRA-web/src/actions/voice-phrases.ts:93]
- [ ] [Review][Patch] Clear stale snapshots at session start and guard malformed `voice_schema_version` parsing [Assets/Project/Scripts/Gameplay/WaitingArea/SceneMenuController.cs:112]
- [ ] [Review][Patch] Reject duplicate child additions and log unknown bindings consistently across Web and Unity [Assets/Project/Scripts/Gameplay/LessonGraphV2/Phrases/VoicePhraseResolverV2.cs:14]
- [ ] [Review][Patch] Propagate Agent `Cancelled` and `Failed` signals into `VoiceQuestSourceV2` termination [Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Sources/VoiceQuestSourceV2.cs:37]
- [ ] [Review][Patch] Preserve unsent activation/cancellation packets across disconnects and hook actual RTC reconnect events [Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs:70]
- [ ] [Review][Patch] Ignore every status after a terminal transport state, including late `ACTIVE` [Assets/Project/Scripts/Gameplay/LessonGraphV2/Questing/Voice/LiveKitVoiceQuestTransportV2.cs:104]
- [ ] [Review][Patch] Send correlated `FAILED` when Agent activation setup fails and permit a defined retry [LiveKitAgent/src/agent_v2.py:411]
- [ ] [Review][Patch] Reset Agent instructions/evaluation binding and stop stale speech when an activation is cancelled or replaced [LiveKitAgent/src/agent_v2.py:397]
- [ ] [Review][Patch] Make terminal packet publication recoverable and reject same-ID replay with changed payload [LiveKitAgent/src/voice_quest_runtime_v2.py:49]
- [ ] [Review][Patch] Harden migration deduplication, ambiguous mapping, validation, resumability, and unmatched reporting [D:/Lab/VRA-web/scripts/migrate_voice_phrase_v2.js:46]
- [ ] [Review][Patch] Add missing Unity, Web action/UI, migration, and production-entrypoint integration tests [Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests:1]
- [x] [Review][Defer] Clean legacy Ruff violations in `agent.py` and `test_agent.py` [LiveKitAgent/src/agent.py:7] — deferred, pre-existing and outside V2 scope.
- [x] [Review][Defer] Diagnose GitNexus incremental indexing failure `Failed calling LOWER: Invalid UTF-8` [.gitnexus:1] — deferred, tooling issue; source review continued with direct reads.
