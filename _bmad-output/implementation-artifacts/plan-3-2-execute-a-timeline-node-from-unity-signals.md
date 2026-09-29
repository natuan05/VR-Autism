# Story 3.2 Implementation Plan

> **For agentic workers:** Read this plan and its spec before implementation. Follow the worktree and `AGENTS.md` constraints; do not commit.

**Goal:** Run a schema-2 Timeline node through Unity Timeline and complete it exactly once from its configured signal.

**Architecture:** Leave `LessonGraphRunner`'s activation loop unchanged. Add a Timeline executor and a scene-bound playback component that own a per-activation signal session. Register the executor in production composition and narrow the runtime validator's unsupported-feature gate.

**Tech Stack:** Unity 6000.3.13f1, Timeline 1.8.12, `PlayableDirector`, `SignalEmitter`/`SignalReceiver`, C#, Unity Test Framework EditMode.

**Spec:** `_bmad-output/implementation-artifacts/spec-3-2-execute-a-timeline-node-from-unity-signals.md`.

## Global constraints

Use only `C:\Users\Admin\.codex\worktrees\epic-3-story-3-1\VR-Autism` on `codex/story-3-2` at base `8a10eb9b`; preserve the existing sprint-status change. Edit Unity code/tests only under `Assets/Project/Scripts/`. Before each existing symbol edit, run GitNexus upstream impact and report HIGH/CRITICAL risk. Keep schema/serialized Timeline data stable. No scene, Story 3.3, checkpoint/resume, editor, legacy, vendor, Python, web, LiveKit, or Firebase edits. Unity batch/full suite are disallowed. Test-folder `AGENTS.md` requires bounded `[UnityTest] IEnumerator` for task/frame tests. Do not claim Unity tests passed without user evidence.

## File map

- `Runtime/Executors/TimelineNodeExecutor.cs` (+ `.meta`): translate playback signals, timeout, skip, and cancellation into one `NodeResult`.
- `Runtime/TimelinePlaybackController.cs` (+ `.meta`): MonoBehaviour owning one director/session, SignalReceiver reactions, signal-track binding restoration, and immediate disable/destroy cleanup. Expose a small playback/session interface so focused tests can inject controlled signals without relying on Timeline frame scheduling.
- `Runtime/LessonGraphExecutorRegistry.cs`: register Timeline executor when a playback controller is provided, preserving existing constructor call sites via an optional trailing argument.
- `Runtime/LessonGraphRunnerInstaller.cs`: expose the scene playback controller reference and pass it into the registry. Keep telemetry composition and session start behavior unchanged.
- `Validation/LessonGraphValidator.cs`: allow `Timeline` only through `ValidateForExecution`; retain the Story 3.3 feature gate.
- `Tests/Editor/TimelineNodeExecutorTests.cs` (+ `.meta`): focused signal, timeout, skip, cancellation, stale callback, cleanup, and scene lifecycle cases.
- `Tests/Editor/AdvancedGraphRunnerTests.cs`, `LessonGraphExecutorRegistryTests.cs`: focused runner/preflight and registry assertions where needed.
- `_bmad-output/implementation-artifacts/sprint-status-v2.yaml`: preserve user edits and change only Story 3.2 `backlog` to `review` after independent review.

## Task 1: Prove the executor result contract

**Interfaces:** A playback session starts `TimelineNodeConfig.TimelineAsset` and reports signal identity to its active callback; its `Dispose`/stop closes only that session. `TimelineNodeExecutor.ExecuteAsync(NodeExecutionContext)` returns the node/activation IDs unchanged and one canonical `NodeStatus`.

- [ ] Add bounded EditMode cases for configured asset start, correct signal success, wrong/unknown signal ignored, duplicate and stale callback rejection, post-cancellation rejection, local timeout `Timeout` and `Failed` outcomes, runner timeout, skip, abort, and missing playback binding.
- [ ] Implement the smallest executor using `INodeClock.Delay(config.TimeoutSeconds, linkedCancellation)`, `SkipToken`, `TimeoutToken`, and `CancellationToken`; guarantee cancellation precedence before returning success and dispose timers/registrations in `finally`.
- [ ] Verify the focused tests are syntactically coherent by inspection; Unity execution remains for the user. Ensure no test has an unbounded await or incomplete `TaskCompletionSource`.

## Task 2: Bind actual Unity Timeline signals and lifecycle

**Interfaces:** The controller receives a dedicated serialized `PlayableDirector`; a session has an activation-scoped callback and idempotent close. Only `SignalEmitter.asset.name == ExpectedSignalName` is accepted. `OnDisable`/`OnDestroy` close an active session synchronously.

- [ ] Add focused tests with a transient `TimelineAsset`, `SignalTrack`, `SignalEmitter`, and `SignalAsset` where EditMode supports them, including preservation of a prior signal-track binding; use a controlled fake to cover callback races that Timeline frame scheduling cannot deterministically produce.
- [ ] Collect distinct expected signal assets from the asset's signal markers, attach a temporary `SignalReceiver` with one `UnityEvent` reaction per asset, remember each affected `SignalTrack` binding, bind it to the receiver, then start the configured `PlayableAsset` on the director. Do not touch non-signal track bindings.
- [ ] On any close path, mark the old session inactive first, call `PlayableDirector.Stop`, restore prior signal-track bindings, remove reactions/listeners and temporary receiver, and clear the active reference. Guard callbacks from earlier activations using a session identity, not just signal name.
- [ ] Check compile-sensitive Unity API signatures against Timeline 1.8.12 and preserve every newly created C# `.meta` GUID.

## Task 3: Compose production runner and gate execution

**Interfaces:** Existing registry constructor calls remain valid. Installer passes the scene controller into the registry. An authoring-valid schema-2 Timeline graph passes execution preflight only when its node executor exists; Parallel/Gate/Loop and variable/composite conditions remain unsupported.

- [ ] Add focused `AdvancedGraphRunnerTests` cases showing Timeline may start with a provided executor while a Parallel/Loop graph is still rejected before executor lookup. Add registry coverage for present/absent controller.
- [ ] Change only `ValidateForExecution`'s advanced node rejection list, the registry constructor/map, and the installer's composition/reference. Do not edit `LessonGraphRunner` unless an evidenced integration failure requires a fresh impact report and plan update.
- [ ] Inspect the scoped diff for unintended serialized schema, telemetry, graph transition, or scene changes.

## Independent review and handoff

- [ ] Independently review the implementation against each spec requirement and inspect every changed file, including signal-track restoration, cancellation before task continuation, and scene lifecycle behavior. Fix material findings once and re-review those fixes.
- [ ] Run allowed static checks, `git diff --check`, GitNexus `detect_changes` with the explicit worktree path, and report index freshness/coverage limits. Do not run Unity batch mode or the full suite.
- [ ] Preserve the existing sprint-status edit and set Story 3.2 to `review` only after review. Report exact changed files, unverified Unity behavior, and the smallest user-run filters (`TimelineNodeExecutorTests`, relevant `AdvancedGraphRunnerTests`, `LessonGraphExecutorRegistryTests`) plus a manual playable/signal/timeout/cancellation/scene-unload check. State that Unity must open the worktree project path above.
