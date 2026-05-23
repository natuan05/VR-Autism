# Story 2.2: Customizable Lesson Core

Status: done

## Story

As a Developer,
I want to refactor the legacy lesson managers to use dynamic parameters instead of hardcoded values,
focusing heavily on the **Actions** lessons by adding customizable visual guidance (active glowing/outlines and floating bubble hints) to guide the child's attention to active target objects.

## Acceptance Criteria

1. Create a serializable `LessonParameters` data model to encapsulate configurable properties for all lesson types.
2. **[Actions Focus: Visual Attention Support]** Update `Quest.cs` so that both the **glowing outline** (`Outline`) and the **floating bubble hints** can be dynamically toggled on/off independently via `LessonParameters`.
3. **[Actions Focus: Dynamic Attention Cues]** Add progressive guidance and sensory parameters to `LessonParameters`:
   - `EnableVisualGuidance` (bool, default `true`): Toggles whether target objects glow (`Outline`) during `State.Enable` and `State.Start`.
   - `EnableBubbleHints` (bool, default `true`): Toggles whether the floating text bubble question (`RequestShowBubble`) appears next to the active object during `State.Enable`.
   - `SpeechSilenceTimeout` (float, default `5.0f`): Idle time before verbal/speech prompt is triggered.
   - `ActionReminderCycle` (float, default `10.0f`): Time interval between automatic visual/verbal reminders.
4. **[Quizzes Focus]** Refactor `QuizController` to use dynamic wait times from `LessonParameters` instead of hardcoded delays.
5. **[AnimalLesson Focus]** Refactor `AnimalLessonManager` to replace hardcoded camera speeds and gaps with dynamic parameter values.
6. Provide a fallback mechanism so that if no `LessonParameters` are explicitly set via `SessionContext`, the system uses the default legacy values.
7. The existing lesson flows must remain functionally identical to the legacy implementation when running with default parameters.

## Tasks / Subtasks

### Phase 1: Actions Lesson Visual Guidance & Core Customization (Highest Priority 🔴)
- [x] Task 1.1: Extend `LessonParameters` with Visual Guidance options
  - [x] Define `LessonParameters` serializable class in `Assets/Project/Scripts/Core/Models/`.
  - [x] Add Actions-specific parameters:
    - `EnableVisualGuidance` (bool, default `true`): Controls if the object outline glows when active.
    - `EnableBubbleHints` (bool, default `true`): Controls if the floating bubble question appears next to the active object.
    - `SpeechSilenceTimeout` (float, default `5.0f`): Silence threshold for teacher prompts.
    - `ActionReminderCycle` (float, default `10.0f`): Interval for quest reminders.
  - [x] Add defaults for Quiz and Exploration modes.
  - [x] Wire `public LessonParameters CurrentParams` to `SessionContext.cs` (initialize with dynamic default).

- [x] Task 1.2: Refactor `Quest.cs` for Target Attention Glowing & Bubbles
  - [x] Modify `Quest.SetState()`:
    - Show/hide the outline (`outline.enabled = ...`) based on state (`State.Enable` or `State.Start`) AND `LessonParameters.EnableVisualGuidance`.
    - Invoke `RequestShowBubble` based on state (`State.Enable`) AND `LessonParameters.EnableBubbleHints`.
  - [x] Ensure `Touch` and `HoldTouch` quest types both glow and show bubbles under correct toggles when waiting for interaction (`State.Enable`), and turn off when `Completed`.
  - [x] Refactor `Quest.cs` to override `reminderCycle` with `SessionContext.Instance.CurrentParams.ActionReminderCycle`.

- [x] Task 1.3: Refactor `SpeechResponser.cs` Silence Timeout
  - [x] Replace hardcoded `5f` prompt cooldown with `SessionContext.Instance.CurrentParams.SpeechSilenceTimeout`.

- [x] Task 1.4: Validation & Regression Checks
  - [x] Verify that when `EnableVisualGuidance` is `false`, target objects do not highlight/glow.
  - [x] Verify that when `EnableBubbleHints` is `false`, the floating bubble question does not spawn.
  - [x] Verify that with both turned on, they activate simultaneously next to the active target object position (`posBubbleQuestion`) in `State.Enable`.

### Phase 2: Quizzes Customization (Medium Priority 🟡)
- [x] Task 2.1: Refactor `QuizController` timing variables.

### Phase 3: Exploration (AnimalTour) Customization (Low Priority 🟢)
- [x] Task 3.1: Refactor `AnimalLessonManager` camera speeds and delays.

## Dev Notes

- **Sensory Guidance Logic:** For children with ASD, visual highlights (glowing outlines) and floating question bubbles are critical to prevent getting lost in VR. However, some children are highly sensitive to glowing effects. Making `EnableVisualGuidance` and `EnableBubbleHints` customizable allows therapists to toggle them off dynamically.
- **Quest State Changes to Observe:**
  ```
  Disable -> SetState(Enable) [Enable Outline if VisualGuidance=true + Show Bubble if EnableBubbleHints=true]
            -> SetState(Start) [Show Progress Bar + Keep/Update Outline if VisualGuidance=true]
            -> SetState(Completed) [Disable Outline + Disable Bubble]
  ```
- **Source components to touch:**
  - `Assets/Project/Scripts/Core/Manager/SessionContext.cs`
  - `Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs`
  - `Assets/Project/Scripts/Player/Player/SpeechResponser.cs`
  - `Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs`
  - `Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs`

### Project Context Rules
- `LessonParameters` must be a plain serializable C# class.
- Follow existing patterns; do not break `UnityEvents` used heavily in `Quest.cs`.

## Dev Agent Record

### Agent Model Used
Claude Sonnet 4.6 (Thinking)

### Completion Notes List
- Tạo mới `LessonParameters.cs` (`[Serializable]`) trong `Core/Models/` với 9 tham số bao phủ cả 3 loại bài học.
- `SessionContext.cs`: Thêm property `CurrentParams` (khởi tạo với `GetDefault()`) và reset trong `Clear()`.
- `Quest.cs`: Outline bật từ `State.Enable` (thay vì `State.Start`), đọc `EnableVisualGuidance` và `EnableBubbleHints` từ `CurrentParams`. ReminderCycle được ghi đè từ `ActionReminderCycle` nếu > 0. Tất cả fallback về `LessonParameters.GetDefault()` khi không có SessionContext.
- `SpeechResponser.cs`: Thêm property `EffectiveSilenceTimeout` ưu tiên `CurrentParams.SpeechSilenceTimeout`, fallback về Inspector field.
- `QuizController.cs`: Thêm property `QuizParams`, thay thế 3 literal `2f`, `0.5f`, `3f` bằng `QuizParams.QuizIntroDelay`, `QuizParams.QuizSoundGap`, `QuizParams.QuizEndDelay`.
- `AnimalLessonManager.cs`: Thêm 2 properties `EffectiveCameraMoveSpeed` và `EffectiveSoundToDescriptionGap`, fallback về Inspector field.
- Tất cả consumer đều có fallback an toàn khi SessionContext chưa được khởi tạo (Editor play, test scene).

### File List
- Assets/Project/Scripts/Core/Models/LessonParameters.cs **(NEW)**
- Assets/Project/Scripts/Core/Manager/SessionContext.cs **(MODIFIED)**
- Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs **(MODIFIED)**
- Assets/Project/Scripts/Player/Player/SpeechResponser.cs **(MODIFIED)**
- Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs **(MODIFIED)**
- Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs **(MODIFIED)**

### Change Log
- 2026-05-22: Implemented Story 2.2 — Customizable Lesson Core. Created LessonParameters data model, wired into SessionContext, refactored Quest/SpeechResponser/QuizController/AnimalLessonManager to use dynamic parameters with Inspector fallback.

## Review Findings

- [x] [Review][Patch] Breaking Scene-Specific Unity Inspector Configurations (Designer Override Invalidation) [Assets/Project/Scripts/Core/Models/LessonParameters.cs] — **Fixed**: Chuyển tất cả float parameters sang sentinel -1f. Consumer dùng `>= 0f` để kiểm tra override hợp lệ.
- [x] [Review][Patch] Dynamic Reminder Logic Bug in Quest.cs (Dead Code under reminderCycle == 0) [Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:143] — **Fixed**: Update loop tính `effectiveCycle` trước, gate bằng `effectiveCycle > 0f`.
- [x] [Review][Patch] Infinite Loop and Application Freeze in Camera Movement Coroutine [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:118] — **Fixed**: Clamp `cachedSpeed = Mathf.Max(0.05f, EffectiveCameraMoveSpeed)` trước vòng lặp.
- [x] [Review][Patch] Inefficient Redundant Property Lookups in Tight Camera Move Loop [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:118] — **Fixed**: Cache speed vào `cachedSpeed` một lần trước `while` loop.
- [x] [Review][Patch] Garbage Collection (GC) Spike in Quest Update Loop [Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:143] — **Fixed**: Thêm `LessonParameters.Default` singleton; tất cả fallback đều dùng singleton thay vì `new LessonParameters()`.
- [x] [Review][Patch] Lack of Range Validation on Timing and Speed Parameters [Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs] — **Fixed**: Thêm `SafeQuizIntroDelay`, `SafeQuizSoundGap`, `SafeQuizEndDelay` helpers với `Mathf.Max(0f,...)` + sentinel check `>= 0f`.
- [x] [Review][Defer] Loose Global Namespace Pollution [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:209] — deferred, pre-existing
- [x] [Review][Defer] Frame-Rate Dependent Unsafe Lerping [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:242] — deferred, pre-existing
- [x] [Review][Defer] State Machine Desynchronization for Dynamic Mid-Session Config Changes [Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs] — deferred, pre-existing
- [x] [Review][Defer] Architectural Mutability Hazard [Assets/Project/Scripts/Core/Manager/SessionContext.cs:34] — deferred, pre-existing
- [x] [Review][Defer] Deserialization Failure Boundary [Assets/Project/Scripts/Core/Models/LessonParameters.cs] — deferred, pre-existing

