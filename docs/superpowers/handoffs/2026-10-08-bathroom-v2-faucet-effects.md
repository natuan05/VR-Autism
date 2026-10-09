# Bathroom V2 faucet effects handoff

Authoritative checkout: `D:/Lab/VR-Autism`, branch `main`. Plan: `docs/superpowers/plans/2026-10-08-bathroom-v2-faucet-effects.md`. Changes remain uncommitted; the detached backup was not edited.

## Delivered behavior

`FaucetEffectsV2` is the scene's owner for the tap Animator, running-water ParticleSystem and dedicated local AudioSource. The existing successful on/off Touch callbacks now call `SetOpen(true)` and `SetOpen(false)`. Repeating a command does not restart effects; activation alone does not open water.

The assigned LessonGraphRunner supplies current session/run state. Pausing pauses audio while retaining the tap's open state; same-run resume uses UnPause. Closing while paused prevents later playback. Terminal completed/failed/cancelled, run/session replacement, component disable and destruction close the tap, clear particles, stop audio and release subscriptions. Missing wiring rejects opening with a diagnostic. Stale runner payloads are ignored.

The legacy WaterTap component and six persistent callbacks remain serialized but disabled only in Bathroom-V2. Its source, the legacy Bathroom scene, SoundManager and shared consumers remain unchanged. No global sound events, microphone capture, LiveKit/schema edits or dependencies were introduced. No controller was deleted or cutover accepted.

## Files

- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/FaucetEffectsV2.cs` and `.meta` (new)
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/FaucetEffectsV2Tests.cs` and `.meta` (new)
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/BathroomV2FaucetEffectsSceneTests.cs` and `.meta` (new)
- `Assets/Project/Scenes/Bathroom-V2.unity`
- The plan and this handoff.

## Inspector wiring

Select the existing WaterTap prefab instance in Bathroom-V2. Its new FaucetEffectsV2 component references runner `1130038378`, tap Animator `652140272`, running-water ParticleSystem `1586076941`, dedicated AudioSource `6200000000000003002` and the existing `Bathroom-Sink-Faucet-CloseDistance.mp3` clip. The new component is `6200000000000003001`.

The AudioSource is local to the tap: Play On Awake off, Loop on, Spatial Blend 3D, Doppler 0, volume 0.35, minimum distance 0.5 metres and maximum distance 8 metres. Volume and attenuation remain Inspector configuration; headset acceptance must confirm audibility and comfort. NPC speech uses its separate LiveKit route.

## Checks and limits

Independent combined review found no production/scene issue. Root fixture inspection corrected Editor lifecycle execution and temporary AudioClip cleanup; focused independent re-review is clean. The ExecuteAlways attribute exists only on the recording subclass in the Editor test file; production effects do not execute during scene authoring.

New files and new scene entries pass whitespace checks. Whole-scene HEAD diff still reports existing dirty-scene whitespace, preserved rather than cleaned globally. Complete local-reference scan, including stripped prefab headers: 298 unique document IDs, zero duplicates, zero unresolved local PPtrs. Scoped source/GUID/callback checks passed. The existing legacy scripts and Bathroom scene have no diff.

GitNexus full refresh: 28,686 nodes, 52,218 edges, 300 flows. Final main-checkout CLI detect-changes: 44 changed files, 392 symbols, 40 affected processes, CRITICAL across the full dirty checkout. This includes earlier shared changes and does not fully represent the new untracked files. Legacy consumed symbols had LOW impact, with serialized UnityEvent callers inspected separately; no existing runner or core symbols were edited. The fixture correction's upstream impacts were LOW.

Unity compilation, EditMode execution and headset behavior are **pending**. No Unity launch, staging, commit or deployment was performed.

## Smallest user-run checks

After Unity compiles, run these two EditMode filters:

- `FaucetEffectsV2Tests`
- `BathroomV2FaucetEffectsSceneTests`

Then check Bathroom-V2 on the headset: starts closed and silent; completing turn-on opens and plays water; repeating open does not restart; water continues during soap/rub steps; pause/resume while open restores only its water playback; close while paused then resume stays silent; turn-off, abort/end/unload close and stop; a new run starts closed. Confirm the water's spatial position, loop quality and level, and that it does not stop NPC speech or vice versa. These are required before claiming runtime parity or cutover readiness.

## 2026-10-09: inactive water visual regression

The user reported no visible water. Editor.log confirms the turn-on Touch completed and the graph advanced; Bathroom-V2 explicitly starts the referenced WaterLeak GameObject inactive. Legacy WaterTap.Interact activated that object before particle playback, but the new base output method omitted activation. `ApplyRunningWater(true)` now activates only the assigned particle GameObject before calling Play. Close behavior remains StopEmittingAndClear; no scene/vendor/legacy file was edited in this repair.

Changed files: `Integration/FaucetEffectsV2.cs`, new `Tests/Editor/FaucetEffectsV2ParticleTests.cs` and `.meta`, plus this handoff. The new focused test invokes the real particle output rather than the recording mock, starts with an inactive particle object, asserts activation and playback, checks stop/clear and verifies playback after reopening. The effect owner's inactive GameObject stays untouched.

Production impact: LOW, two direct callers and eight upstream symbols, no indexed flows. The new unindexed test's impact lookup returned UNKNOWN/not found; this is not evidence of zero impact. Scoped whitespace checks passed, final whole-checkout detect-changes was run, and independent review plus focused assertion re-review is clean. Unity execution remains pending. After compilation, run `FaucetEffectsV2ParticleTests` and `FaucetEffectsV2Tests`, then restart the lesson and verify visible water at turn-on, through wet-hands/soap/rub, and stopping at turn-off/terminal cleanup.
