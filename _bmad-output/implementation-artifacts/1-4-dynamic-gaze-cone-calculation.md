# Story 1.4: Dynamic Gaze Cone Calculation

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a clinical researcher / therapist,
I want the VR client to dynamically calculate the gaze cone boundary angle using linear interpolation (LERP) based on child-to-target distance,
so that we obtain physically accurate focus ratio telemetry, ensuring strict joint attention detection when close and frustration-free tolerance when far.

## Acceptance Criteria

1. Replace the static `gazeConeHalfAngle` field in `SensorHarvester.cs` with three configurable inspector fields:
   - `gazeConeMinAngle` (default: `5.0f` degrees)
   - `gazeConeMaxAngle` (default: `15.0f` degrees)
   - `gazeConeMaxDistance` (default: `8.0f` meters)
2. At runtime, the actual gaze cone half-angle is computed dynamically using:
   - `t = Mathf.Clamp01(distance / gazeConeMaxDistance)`
   - `currentHalfAngle = Mathf.Lerp(gazeConeMinAngle, gazeConeMaxAngle, t)`
3. When the child is extremely close ($d \to 0$), the angle clamps to the minimum size ($5^\circ$ half-angle) to enforce strict focus.
4. When the child is far away ($d \ge 8\text{m}$), the angle clamps to the maximum size ($15^\circ$ half-angle) to allow loose/frustration-free alignment.
5. Apply the dynamically calculated angle in `SampleToBuffer()` to evaluate `isInGazeCone` and log `focusObjectName` in the telemetry raw sample buffer.
6. Apply the dynamically calculated angle in `OnDrawGizmos()` in the Unity Editor to visualize the yellow/green wireframe debug cone sizing up/down dynamically according to distance.

## Tasks / Subtasks

- [ ] Update fields in `SensorHarvester.cs` (AC: #1)
  - [ ] Remove `gazeConeHalfAngle` field.
  - [ ] Add `gazeConeMinAngle`, `gazeConeMaxAngle`, and `gazeConeMaxDistance`.
- [ ] Implement dynamic angle calculation in `SampleToBuffer()` (AC: #2, #3, #4, #5)
  - [ ] Compute distance `d` from the camera to the target visual center.
  - [ ] Calculate LERP parameter `t` and interpolate `currentHalfAngle`.
  - [ ] Use `currentHalfAngle` for the gaze cone inclusion check.
- [ ] Update Editor debug rendering in `OnDrawGizmos()` (AC: #6)
  - [ ] Dynamically compute `currentHalfAngle` based on distance inside `OnDrawGizmos()`.
  - [ ] Draw the wireframe debug cone at `maxRaycastDistance` using the dynamic angle.

## Dev Notes

- Code modifications are restricted to [SensorHarvester.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs).
- No other files reference the legacy `gazeConeHalfAngle` field, meaning no compilation regressions will occur.
- Ensure that if `_currentQuestTarget` is null, the logic safely falls back to a default state (or uses a flat angle/raycast check).

### Project Structure Notes

- Keep all changes within `VRAutism.Core.Telemetry` namespace.
- Ensure file encoding remains UTF-8 (no BOM) to prevent C# compiler display issues.

### Project Context Rules

- No custom frameworks or third-party packages are needed for this change.
- Follow Unity's C# optimization rules (minimize GC allocs inside `FixedUpdate` and `OnDrawGizmos`).

### References

- [TELEMETRY_GAZE_DESIGN.md](file:///D:/Lab/VR-Autism/docs/design/TELEMETRY_GAZE_DESIGN.md)

## Dev Agent Record

### Agent Model Used

Gemini 3.5 Flash

### Debug Log References

### Completion Notes List

### File List

- [SensorHarvester.cs](file:///D:/Lab/VR-Autism/Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs)
