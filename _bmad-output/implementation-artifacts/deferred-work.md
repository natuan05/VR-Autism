# Deferred Work

This file tracks technical debt and findings deferred during code reviews and development phases.

## Deferred from: code review of 2-2-customizable-lesson-core (2026-05-22)

- Loose Global Namespace Pollution [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:209] — deferred, pre-existing. The `AnimalLessonManager` class is defined in the global namespace rather than under `VRAutism`.
- Frame-Rate Dependent Unsafe Lerping [Assets/Project/Scripts/Gameplay/Exploration/AnimalLessonManager.cs:242] — deferred, pre-existing. Camera movement uses frame-rate dependent lerping (`Time.deltaTime * speed`), which behaves inconsistently across different hardware framerates.
- State Machine Desynchronization for Dynamic Mid-Session Config Changes [Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs] — deferred, pre-existing. Mid-session configuration changes could desynchronize active quest states due to cached internal state timers.
- Architectural Mutability Hazard [Assets/Project/Scripts/Core/Manager/SessionContext.cs:34] — deferred, pre-existing. The `CurrentParams` property on the `SessionContext` singleton is completely mutable, allowing any external subsystem to modify global runtime parameters.
- Deserialization Failure Boundary [Assets/Project/Scripts/Core/Models/LessonParameters.cs] — deferred, pre-existing. Deserialization of custom dynamic configurations lacks validation boundaries for unknown or corrupted keys.
