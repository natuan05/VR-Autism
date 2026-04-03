# Architecture Upgrade Ideas

> **Status:** Ideas backlog — not planned for current sprint.
> **Context:** These techniques were evaluated on 2026-04-02 as potential replacements for the current `ActionManager` / `AnimalLessonManager` / `TimeManager` architecture.
> **Decision:** Current Coroutine-based approach is sufficient for Phase 1. Revisit when triggers listed below are met.

---

## Technique A: Replace `ActionManager` with FSM / Visual Scripting

### What it replaces
The sequential `foreach` + `WaitUntil` coroutine loop inside `ActionManager.cs` that drives interactive action lessons (e.g., Bathroom - WashingHand).

### Why upgrade
The current approach is strictly **linear** (Step 1 → Step 2 → Step 3). It cannot handle:
- Child skips a step or performs it out of order
- Escalating hint system (Verbal → Visual → Physical) per failed step
- Re-try logic after a failed action within the same lesson

### How it works
Each action (grab soap, turn faucet) becomes a **Node** in a graph. Transitions between nodes are driven by physics triggers instead of polling `WaitUntil`. This makes non-linear lesson flows possible and debuggable without modifying C# code.

### Tools to evaluate
- **Unity Visual Scripting** (built-in, free) — best for small graphs
- **NodeCanvas** (~$60) — industry standard, FSM + Behavior Tree hybrid
- **Custom lightweight FSM** — 2-3 days to build, zero dependency, recommended first

### Key integration tasks
- Map every `WaitUntil(condition)` to an FSM Transition condition
- Re-wire `TimeManager.Instance.MarkQuestStart()` → FSM node `OnEnter()`
- Re-wire `TimeManager.Instance.LogQuestComplete()` → FSM Transition `OnExit()`
- Create `FSMEventBridge` utility to call `SendEvent(EventID.*)` from graph nodes
- Design hint/timeout sub-states per quest node
- Remove `ActionManager.cs` **only after** side-by-side Firebase data validation

### ⚠️ Migration estimate
~3–4 weeks total (Design: 1 week, Port 1 lesson: 1 week, Validate Firebase: 3 days, Port remaining: 2–3 days each)

### ✅ Trigger to adopt
Therapy team requests: *"We need to track what happens when the child fails a step after 3 hints."* Until that requirement is confirmed, this is premature optimization.

---

## Technique B: Replace `AnimalLessonManager` with Unity Timeline & Cinemachine

### What it replaces
The time-based coroutine loop inside `AnimalLessonManager.cs` that drives guided tour lessons (Zoo — Farm, Ocean, Grassland).

### Why upgrade
Currently, adjusting lesson timing (e.g., make the Rabbit description 2 seconds longer) requires:
1. Developer opens `AnimalLessonManager.cs`
2. Changes `WaitForSeconds(4f)` → `WaitForSeconds(6f)`
3. Rebuild & test → ~30 minute turnaround

With Timeline, a therapist or content designer can drag an audio clip 2 seconds to the right in the editor with zero code changes → 30-second turnaround.

### How it works
A `.playable` Timeline asset replaces the coroutine entirely:
- **Cinemachine Track** → camera glides between animal positions
- **Audio Track** → narration and animal sounds play at exact timestamps
- **Activation Track** → introUI GameObjects fade in/out at correct times
- **Signal Track** → fires a `LessonComplete` signal at the end of the tour

### Key integration tasks
- Install Cinemachine package
- Create one `CinemachineVirtualCamera` per animal position
- Create one `.playable` Timeline asset per lesson scene (Farm, Ocean, Grassland)
- Replace `SoundManager.SendEvent` calls with Timeline Audio Tracks
- Create a `TimelineSignalReceiver` MonoBehaviour to bridge the end-of-lesson signal → `TimeManager.Instance.SaveLessonTimeData()`
- Remove `AnimalLessonManager.cs` after end-to-end verification

### ⚠️ Migration estimate
~1 week per lesson scene (3 scenes = ~3 weeks)

### ✅ Trigger to adopt
Content team (therapists / educators) needs to adjust lesson timing or audio independently without developer involvement.

---

## Technique C1: Replace `TimeManager.Instance` Singleton with Dependency Injection

### What it replaces
The `TimeManager.Instance` Singleton pattern used across `QuizController`, `ActionManager`, and `AnimalLessonManager`.

### Why upgrade
The Singleton pattern makes every consumer class impossible to unit test in isolation:
```csharp
// This will always crash in a test environment — no Unity scene → no Instance
TimeManager.Instance.MarkQuestStart();
```

With DI, an `ISessionTracker` interface is injected into each consumer, allowing fake/mock implementations to be substituted during testing.

### Tools to evaluate
- **VContainer** — lightweight, modern, Unity-first
- **Zenject (Extenject)** — more powerful, heavier, industry standard

### Key integration tasks
- Extract `ISessionTracker` interface from `TimeManager`'s public API
- Install VContainer/Zenject via Package Manager
- Create a `LifetimeScope` bootstrap object that wires `TimeManager → ISessionTracker`
- Replace all `TimeManager.Instance.*` calls with `[Inject] private ISessionTracker`
- Write unit tests (the main payoff of this change)

### ⚠️ Migration estimate
~1 week for wiring + 1–2 weeks for writing meaningful unit tests

### ✅ Trigger to adopt
Team grows to 3+ developers writing parallel features, OR first automated test suite is being written. **Not worth the overhead for a 1–2 person team.**

---

## Technique C2: Replace Direct Asset References with Addressables

### What it replaces
The direct `GameObject associatedObject` reference in `QuizConfig.cs` (and future similar configs), which forces Unity to load **all** referenced 3D models into RAM when a scene loads.

### Why upgrade
Currently all animal 3D prefabs referenced in `Farm_Quiz.asset`, `Ocean_Quiz.asset`, etc. are loaded into RAM simultaneously at scene start — even models for questions not yet displayed. As the quiz library grows (50+ questions, each with a unique 3D model), this becomes a memory bottleneck on standalone headsets (Quest 2/3).

### How it works
Mark prefabs as **Addressable assets**. Replace `GameObject` fields with `AssetReferenceGameObject`. Load each model async only when its question is presented, release it from RAM when moving to the next question.

### Key integration tasks
- Mark all 3D quiz prefabs as Addressable in Inspector
- Create Addressable Groups by lesson (Farm, Ocean, Grassland)
- Replace `public GameObject associatedObject` with `public AssetReferenceGameObject associatedObject` in `QuizConfig.cs`
- Rewrite `QuizController.PresentQuestion()` using `async/await` + `Addressables.LoadAssetAsync<>()`
- Call `Addressables.Release()` on question change and on `EndQuiz()`
- Add a loading state indicator UI (spinner/fade) during async load

### ⚠️ Migration estimate
~1 week (measurable before/after with Unity Profiler Memory view)

### ✅ Trigger to adopt
Unity Profiler shows scene memory usage exceeding **300MB** on a standalone build, OR quiz content library grows beyond **20 unique 3D models**.

---

## Priority Matrix

```
                    ┌──────────────────────┬────────────────────────┐
                    │     High Impact      │      Lower Impact      │
┌───────────────────┼──────────────────────┼────────────────────────┤
│ Moderate Effort   │ B: Timeline          │ -                      │
│                   │ C2: Addressables     │                        │
├───────────────────┼──────────────────────┼────────────────────────┤
│ High Effort       │ A: FSM               │ C1: DI / VContainer    │
└───────────────────┴──────────────────────┴────────────────────────┘
```

### Recommended adoption order
1. **B — Timeline** when content team needs independent lesson editing
2. **C2 — Addressables** when memory profiling shows bottleneck
3. **A — FSM** when therapists request non-linear branching logic
4. **C1 — DI** when team size and test coverage requirements grow
