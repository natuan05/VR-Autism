# Story 2.3: Lesson Parameter Syncing

Status: done

## Story

As a therapist or educator,
I want the VR Client to automatically fetch and apply the child's customized lesson parameters (`default_lesson_params`) from Firestore during PIN pairing,
so that the VR environment is instantly personalized to the child's sensory and cognitive needs without any manual configuration inside the headset.

## Acceptance Criteria

1. **[Firestore Sync on Pairing]**: During the PIN pairing session initialization in `SceneMenuController.cs`, asynchronously fetch the child's profile from the Firestore collection `child_profiles` using the received `childId`.
2. **[Nested Composition Parsing]**: Extract the map field `default_lesson_params` and deserialize it into a nested `LessonParameters` object (supporting nested map parsing to populate the `Actions`, `Quiz`, and `Exploration` sections).
3. **[Case & Type Resilience]**:
   - Support both `snake_case` (Firestore standard) and `camelCase` keys (e.g. `enable_visual_guidance` vs `enableVisualGuidance`).
   - Use safe type conversions (`System.Convert.ToSingle`, `System.Convert.ToBoolean`) to handle numeric variations, as Firestore represents numeric maps as `double` or `long`, which causes casting exceptions when directly cast to C# `float`.
4. **[Zero-Allocation Safe Fallback]**: If the child profile is missing, Firestore is offline, or parsing fails:
   - Log a warning/error in the Unity console.
   - Do **NOT** block the gameplay scene from loading.
   - Fall back to `LessonParameters.Default` (singleton instance) to preserve legacy and Inspector settings while avoiding heap allocations.


## Web-Side Integration Requirements (VRA-web)

> [!NOTE]
> This story establishes a contract between the VR Client and the Web Dashboard (`VRA-web`). 
> When creating a new child profile or editing an existing one, the Web Dashboard must support writing the `default_lesson_params` field in Firestore:
> - **Field Type**: `Map` (Map/Object)
> - **Schema (Nested Map - Recommended)**:
>   - `actions`: (Map)
>     - `enable_visual_guidance`: `boolean` (Default: `true`)
>     - `enable_bubble_hints`: `boolean` (Default: `true`)
>     - `speech_silence_timeout`: `number` (Default: `-1` for no-override / fallback to inspector)
>     - `action_reminder_cycle`: `number` (Default: `-1` for no-override)
>   - `quiz`: (Map)
>     - `quiz_intro_delay`: `number` (Default: `-1`)
>     - `quiz_sound_gap`: `number` (Default: `-1`)
>     - `quiz_end_delay`: `number` (Default: `-1`)
>   - `exploration`: (Map)
>     - `camera_move_speed`: `number` (Default: `-1`)
>     - `sound_to_description_gap`: `number` (Default: `-1`)
> - **UI Control**: The Web settings form should initialize numbers with a toggle or preset representing "System Default" (mapped to `-1` on Firestore), allowing therapists to override them with custom integers or floats only when needed.

## Tasks / Subtasks

### Phase 1: Deserialization Engine in `LessonParameters.cs`
- [x] Define `FromDictionary(Dictionary<string, object> dict)` factory inside `LessonParameters.cs`.
- [x] Support root-level keys mapping (or nested maps if the web dashboard nests maps inside `default_lesson_params`). To be safe, parse both root-level fields and sub-maps.
- [x] Add safe boolean conversions using `System.Convert.ToBoolean` or type checks.
- [x] Add safe float conversions using `System.Convert.ToSingle`.
- [x] Wire robust error-handling so that a corrupt parameter field does not discard other valid parameters.

### Phase 2: Async Integration in `SceneMenuController.cs`
- [x] Asynchronously query the Firestore document `child_profiles/{childId}` inside `SceneMenuController.LoadRemoteLesson`.
- [x] Wrap the fetch query in a dedicated `try-catch` block so that network failures or missing profiles do not prevent scene transitions.
- [x] Parse `default_lesson_params` and apply the resulting object to `SessionContext.Instance.CurrentParams`.
- [x] Log success/warning telemetry on key parameter changes.

### Phase 3: Verification & Test Harness
- [x] Write a test script or dry-run command in Unity to verify that a mocked `default_lesson_params` map parses exactly as expected.
- [x] Perform functional check verifying that toggles (visual guidance and speech timeouts) apply properly when simulated.

### Review Findings
- [x] [Review][Patch] Tăng cường tính chống chịu kiểu dữ liệu (Cast Resilience) khi đọc Nested Map trong `LessonParameters.TryGetSubDict` [LessonParameters.cs]
- [x] [Review][Patch] Đảm bảo giải mã an toàn trường `default_lesson_params` từ Firestore trong `SceneMenuController` [SceneMenuController.cs]

## Dev Notes

- **Composition Path Mapping**:
  With Option 2 (Composition) implemented, the fields in C# are nested (e.g. `CurrentParams.Actions.EnableVisualGuidance`). 
  The Firestore dictionary might be flattened (e.g., key `"enable_visual_guidance"`) or nested (e.g., sub-map `"actions"` with key `"enable_visual_guidance"`). 
  To achieve maximal robustness, the `FromDictionary` method should check **both**:
  1. A flattened map format where keys are directly in the dictionary (e.g. `dict["enable_visual_guidance"]` or `dict["enableVisualGuidance"]`).
  2. A nested map format (e.g., `dict["actions"]` is another sub-dictionary containing its respective settings).
- **Fallback Integrity**: Always remember to preserve the sentinel `-1f` when a float parameter is absent from the incoming map.

## Dev Agent Record

### Agent Model Used
Gemini 3.5 Flash / Antigravity

### Completion Notes List
- Completed robustComposition deserialization with snake_case and camelCase fallbacks.
- Integrated parallel async fetching in Unity `SceneMenuController.cs`.
- Implemented corresponding Server Action `updateDefaultLessonParams` in Web Dashboard.
- Designed premium web UI `LessonParametersEditor.tsx` with sentinel-aware slider support.
- Fully integrated settings panel inside the expert stats page layout.

### File List
- `d:\Lab\VR-Autism\Assets\Project\Scripts\Core\Models\LessonParameters.cs`
- `d:\Lab\VR-Autism\Assets\Project\Scripts\Gameplay\WaitingArea\SceneMenuController.cs`
- `d:\Lab\VRA-web\src\actions\expert.ts`
- `d:\Lab\VRA-web\src\app\dashboard\expert\_components\stats\LessonParametersEditor.tsx`
- `d:\Lab\VRA-web\src\app\dashboard\expert\stats\page.tsx`

### Change Log
- Added `FromDictionary` factory method in `LessonParameters.cs`.
- Integrated child profile fetching concurrently with lesson metadata inside `SceneMenuController.cs`.
- Created `updateDefaultLessonParams` Next.js server action to write configurations.
- Created `LessonParametersEditor.tsx` component allowing simple management of custom session configs.
- Mounted the newly built editor on the therapist stats panel dashboard.
