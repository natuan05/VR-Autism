# Story 3.1: Controllable Lesson Event Bridge

Status: done

## Story

As a therapist or educator,
I want the VR gameplay engines to register and respond to remote control events (such as trigger hints, skipping quests, or pausing sessions) dispatched via the custom C# event system of `RemoteCommandListener.cs` (avoiding legacy `EventChannel.cs` architecture),
so that I can dynamically guide and moderate the child's session from the Web Dashboard in real time.

## Acceptance Criteria

1. **[C# Event Dispatcher Definition]**: Define and expose standard static C# `System.Action` events inside `RemoteCommandListener.cs` to act as the central dispatcher:
   - `public static event System.Action OnTriggerHint;`
   - `public static event System.Action OnSkipQuest;`
   - `public static event System.Action OnPauseLesson;`
   - `public static event System.Action OnResumeLesson;`
2. **[Action/Quest Bridge]**:
   - Update `Quest.cs` or `ActionManager.cs` to listen to `RemoteCommandListener.OnTriggerHint` and immediately flash visual guides (flashing the glow outline) and/or prompt the child.
   - Update `Quest.cs` to listen to `RemoteCommandListener.OnSkipQuest` and immediately force complete the current active touch/hold task (transitioning to `State.Completed`).
3. **[Quiz Controller Bridge]**:
   - Update `QuizController.cs` to listen to `RemoteCommandListener.OnTriggerHint` and replay the current question's sounds.
   - Update to listen to `RemoteCommandListener.OnSkipQuest` and skip the current active question (calling `SubmitAnswer` with a dummy index or bypassing directly to the next question to prevent locking).
4. **[Resource Hygiene (Decoupling)]**:
   - Unsubscribe all event listeners properly in `OnDestroy()` or `OnDisable()` to avoid dangling delegates / memory leaks (which are critical in static C# event systems to prevent reference leaks on destroyed gameobjects).
   - Ensure the event bridge operations are robust to null checks on `RemoteCommandListener.Instance` and `SessionContext.Instance`.

## Web-Side Integration Requirements (VRA-web)

*(Documenting how the Web Dashboard will interact with these events later in Story 3.4)*
- The Next.js dashboard will push commands to `live_sessions/{session_id}/commands` using:
  - `trigger_hint`: Invokes `RemoteCommandListener.OnTriggerHint`
  - `skip_step`: Invokes `RemoteCommandListener.OnSkipQuest`
  - `pause_lesson`: Invokes `RemoteCommandListener.OnPauseLesson`
  - `resume_lesson`: Invokes `RemoteCommandListener.OnResumeLesson`

## Tasks / Subtasks

### Phase 1: Expose C# Events in `RemoteCommandListener.cs`
- [x] Uncomment/define static C# events in `RemoteCommandListener.cs`:
  - `public static event System.Action OnTriggerHint;`
  - `public static event System.Action OnSkipQuest;`
  - `public static event System.Action OnPauseLesson;`
  - `public static event System.Action OnResumeLesson;`
- [x] Expose public methods to trigger these events safely:
  - `public void TriggerHint()`
  - `public void TriggerSkipQuest()`
  - `public void TriggerPauseLesson()`
  - `public void TriggerResumeLesson()`

### Phase 2: Action Gameplay Event Bridging (`Quest.cs`)
- [x] Modify `Quest.cs` to subscribe to `RemoteCommandListener.OnTriggerHint` and `RemoteCommandListener.OnSkipQuest` in `Start()` / `OnEnable()`.
- [x] Handle hint triggers by forcing the visual outline enable: `outline.enabled = true` and invoking verbal prompts if active.
- [x] Handle skip triggers by marking the quest completed: `SetState(State.Completed)`.
- [x] Properly unsubscribe from `RemoteCommandListener` events in `OnDestroy()` or `OnDisable()`.

### Phase 3: Quiz Gameplay Event Bridging (`QuizController.cs`)
- [x] Modify `QuizController.cs` to subscribe to `RemoteCommandListener.OnTriggerHint` and `RemoteCommandListener.OnSkipQuest` in `Awake()` or `Start()`.
- [x] Handle hint triggers by **re-playing the existing question and animal sounds** (by running `StartCoroutine(HandleQuestionSounds())`). This serves as the hint by reminding the child of the question, requiring **zero new audio assets**.
- [x] Handle skip triggers by force-answering the current question correctly/incorrectly and enabling the Next button or moving to the next question.
- [x] Properly unsubscribe from `RemoteCommandListener` events in `OnDestroy()`.

### Phase 4: Verification & Dry-run
- [x] Write a temporary debug UI or script in Unity (e.g. keybinds `H` for Hint, `S` for Skip) to call `RemoteCommandListener.Instance.TriggerHint()` / `TriggerSkipQuest()` and verify gameplay components respond flawlessly.

## Dev Notes
- **Dangling Delegates Hazard (Critical)**:
  Since static events on `RemoteCommandListener` are held globally, any subscription (`+=`) **MUST** be cleared (`-=`) in `OnDestroy()` or `OnDisable()`. Failure to do so will keep references to destroyed MonoBehaviours alive, causing severe memory leaks and null-reference exceptions on subsequent runs.
- **Quest Completion Safety**:
  When forcing completion via skip, ensure all transition effects, UI bars, and progress calculations are safely reset/notified just as if the user triggered it naturally.

## Senior Developer Review (AI)

**Review Date:** 2026-05-23
**Outcome:** Approved (all patches applied)
**Layers:** Blind Hunter ✅ | Edge Case Hunter ✅ | Acceptance Auditor ✅

### Action Items

#### decision_needed (3) — all resolved

- [x] [Review][Decision] StopAllCoroutines() in QuizController hint handler is too broad — **Fixed:** Added `_questionSoundCoroutine` field; hint handler now stops only the sound coroutine. [QuizController.cs:86-91]
- [x] [Review][Decision] RemoteCommandListener scene lifecycle undefined — **Resolved:** No DontDestroyOnLoad by design. Added detailed lifecycle comment in class summary. [RemoteCommandListener.cs]
- [x] [Review][Decision] Spec says "flashing the glow outline" — **Fixed:** Implemented 3-cycle blink animation (`BlinkOutlineHint()`) that respects `EnableVisualGuidance` after blinking ends. [Quest.cs]

#### patch (3) — all applied

- [x] [Review][Patch] Debug keybinds Update() not guarded with #if UNITY_EDITOR — **Fixed:** Wrapped entire `Update()` in `#if UNITY_EDITOR / #endif`. [RemoteCommandListener.cs]
- [x] [Review][Patch] Quest hint resets timeReminder=0f but onQuestReminder never fires when reminderCycle=0 — **Fixed:** Now directly calls `onQuestReminder?.Invoke()` instead of relying on timer reset. [Quest.cs]
- [x] [Review][Patch] Missing main-thread safety comment — **Fixed:** Added explicit MAIN THREAD ONLY warning comment with Story 3.4 note on all dispatchers. [RemoteCommandListener.cs]

#### defer (2)

- [x] [Review][Defer] OnSetVolume/OnPlayNpcScript have no subscribers in this story's scope [RemoteCommandListener.cs] — deferred, will be consumed by future stories
- [x] [Review][Defer] Spec AC4 describes null-check on RemoteCommandListener.Instance but static event subscription doesn't require instance — spec inaccuracy, not a code bug [RemoteCommandListener.cs] — deferred, update spec wording in future

### Review Follow-ups (AI)

- Applied 2026-05-24: All 3 decisions resolved, all 3 patches applied. Story marked `done`.

