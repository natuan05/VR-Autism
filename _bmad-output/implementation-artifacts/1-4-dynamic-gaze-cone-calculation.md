# Story 1.4: Dynamic Gaze Cone Calculation

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a clinical researcher / therapist,
I want the VR client to dynamically calculate the gaze cone boundary angle using the natural geometric perspective formula $\theta_{half} = \arctan(R / d)$ clamped between $5^\circ$ and $15^\circ$,
so that the physical detection zone matches the actual object size at any distance, producing accurate focus telemetry.

## Acceptance Criteria

1. Configure three inspector fields in `SensorHarvester.cs` to set thresholds and limits:
   - `gazeConeMinAngle` (default: `5.0f` degrees): The absolute minimum allowable half-angle when far away.
   - `gazeConeMaxAngle` (default: `15.0f` degrees): The absolute maximum allowable half-angle when close.
   - `defaultTargetRadius` (default: `0.5f` meters): Fallback radius $R$ if no collider bounds are detected.
2. At runtime inside `SampleToBuffer()`, dynamically calculate:
   - Target radius $R$ from the active target's collider bounds: `float R = col.bounds.extents.magnitude` (or fallback to `defaultTargetRadius`).
   - Distance $d$ to the visual center: `float d = dirToTarget.magnitude`.
   - Nửa góc nón thị giác tự nhiên: `float currentHalfAngle = Mathf.Atan2(R, d) * Mathf.Rad2Deg`.
   - Clamping: `currentHalfAngle = Mathf.Clamp(currentHalfAngle, gazeConeMinAngle, gazeConeMaxAngle)`.
3. Verify that the dynamic angle scales naturally:
   - **Close range**: Angle opens up (up to $15^\circ$ half-angle) because the object physically occupies a large portion of the visual field.
   - **Far range**: Angle narrows down (down to $5^\circ$ half-angle) because the object occupies a small portion of the visual field.
4. Use this dynamic `currentHalfAngle` in `SampleToBuffer()` for evaluating `isInGazeCone` and logging.
5. Use this dynamic `currentHalfAngle` inside `OnDrawGizmos()` to dynamically draw the yellow/green wireframe cone matching the target object's apparent visual size in the Editor.

## Tasks / Subtasks

- [x] Update inspector fields in `SensorHarvester.cs` (AC: #1)
  - [x] Add `gazeConeMinAngle`, `gazeConeMaxAngle`, and `defaultTargetRadius` fields.
- [x] Implement geometric perspective calculation in `SampleToBuffer()` (AC: #2, #3, #4)
  - [x] Extract target radius $R$ from active target collider or use fallback.
  - [x] Compute distance `d` to active target bounds.
  - [x] Calculate `currentHalfAngle = Mathf.Atan2(R, d) * Mathf.Rad2Deg` and clamp it to `[gazeConeMinAngle, gazeConeMaxAngle]`.
  - [x] Use `currentHalfAngle` for the gaze cone inclusion check.
- [x] Update Editor debug rendering in `OnDrawGizmos()` (AC: #5)
  - [x] Compute `currentHalfAngle` dynamically inside `OnDrawGizmos()` using the same geometric formula.
  - [x] Draw the wireframe debug cone at `maxRaycastDistance` using the dynamic angle.

## Dev Notes

- Code modifications are restricted to [SensorHarvester.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs).
- Ensure file encoding remains UTF-8 (no BOM).
- Ensure that if `_currentQuestTarget` is null, the logic safely falls back to `gazeConeMaxAngle`.

### Project Structure Notes

- Keep all changes within `VRAutism.Core.Telemetry` namespace.

### Project Context Rules

- Minimize GC allocs inside `FixedUpdate` and `OnDrawGizmos`.

### References

- [TELEMETRY_GAZE_DESIGN.md](file:///D:/Lab/VR-Autism/docs/design/TELEMETRY_GAZE_DESIGN.md)

## Dev Agent Record

### Agent Model Used

Gemini 3.5 Flash

### Debug Log References

- None (Unity compilation verified, manual review confirms mathematical and logical accuracy)

### Completion Notes List

- Replaced legacy static `gazeConeHalfAngle` field with three configurable inspector parameters: `gazeConeMinAngle` (default 5.0f), `gazeConeMaxAngle` (default 15.0f), and `defaultTargetRadius` (default 0.5f).
- Implemented natural perspective dynamic gaze cone calculation using `Mathf.Atan2(R, d) * Mathf.Rad2Deg` clamped to min/max angles inside `SampleToBuffer()`.
- Updated `OnDrawGizmos()` to dynamically compute and draw the gaze cone wireframe inside the Editor using the same natural perspective math.

### File List

- [SensorHarvester.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs)

