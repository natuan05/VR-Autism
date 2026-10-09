# Bathroom V2 Faucet Effects Implementation Plan

> **For agentic workers:** Use subagent-driven-development for the scoped tasks below. Project AGENTS.md and the user's one-pass/one-independent-review workflow take precedence over generic skill commit or Unity-run steps.

**Goal:** Restore Bathroom-V2 water sound with one authoritative owner for tap animation, running-water particles and local audio.

**Architecture:** A scene-owned `FaucetEffectsV2` accepts explicit idempotent open/close commands from the existing successful Touch callbacks. It consumes the assigned runner's state events for pause/resume, terminal cleanup and run replacement; audio uses a dedicated local AudioSource. The legacy WaterTap component and scripts are retained, with only its Bathroom-V2 component and persistent callbacks disabled.

**Tech Stack:** Existing Unity C#, Animator, ParticleSystem, AudioSource, LessonGraphRunner and NUnit/EditMode tests. No additional packages or wire schemas.

**Spec:** Approved in-chat SetOpen/pause/resume/reset contract; `_bmad-output/specs/spec-story-2-4-legacy-v2-parity/SPEC.md` and `delivery-phases.md`. Existing trial scope remains Bathroom-V2 and LearnToAsk-V2; only Bathroom uses this effect.

## Constraints and interfaces

- Work on the user-authorized main checkout `D:/Lab/VR-Autism`; preserve all prior and user changes. Do not edit the detached backup.
- No edits to SoundManager, WaterTap/vendor assets, the legacy Bathroom scene, LearnToAsk-V2, either parity matrix, packages, transport or telemetry contracts. No legacy controller deletion or deferred Story work.
- Agents perform static checks; the user runs Unity compilation, focused EditMode tests and headset acceptance. Do not stage or commit.
- New file: `Assets/Project/Scripts/Gameplay/LessonGraphV2/Integration/FaucetEffectsV2.cs`; namespace `VRAutism.Gameplay.LessonGraphV2.Integration`; MonoBehaviour `FaucetEffectsV2`.
- Script .meta GUID: `f407129f5c83411da1b74e8e9f39ea32` (checked absent before creation).
- Serialized references: `_runner`, `_tapAnimator`, `_runningWater`, `_waterAudioSource`, `_waterClip`; Animator parameters remain `Open` and `Closed`.
- Public commands: `void SetOpen(bool open)` and `void ResetEffects()`; read-only `bool IsOpen` for observation. Repeating the same open state does not restart animation, particles or audio.
- Opening requires a valid current running lesson. Closing remains safe while paused. Audio is audible only while the tap is open and the lesson is running. Pause/pausing retains tap state and pauses playback; same-run resume uses UnPause, never starts an unrelated voice. Close while paused prevents resume playback.
- Terminal `completed`, `failed`, `cancelled`, run/session replacement, component disable and destruction stop audio, clear running-water particles and set the tap closed. Disabled components reject opening. Subscription cleanup is explicit; repeated enable must not duplicate handlers.
- Use direct Inspector references and scoped runner state; no SoundManager/event-bus lookup, HTTP audio, microphone capture or audio mixer/framework expansion. Existing NPC routing remains separate.
- Missing required references fail closed with a clear diagnostic. EditMode test setup must not start real media services or rely on `AudioSource.isPlaying` as proof of playback.

## Task 1: Component and behavior tests

**Owner:** One gpt-6-luna/high implementation agent.

**Owned files:** New `Integration/FaucetEffectsV2.cs` and `.meta`; new `Tests/Editor/FaucetEffectsV2Tests.cs` and `.meta`.

- [x] Write focused tests first using a small output seam so Play/Pause/UnPause/Stop and visual operations can be asserted without playing headset audio.
- [x] Implement the explicit state transitions and runner event binding. No existing runner/core symbols were edited.
- [x] Add tests for startup closed; duplicate open/close; pause/repeated pause/same-run resume; close while paused; each terminal status; new session/run reset; stale state protection; disable/destroy/unsubscribe and re-enable; missing wiring; rejection of opening outside a running lesson. Unity execution is pending.
- [x] Inspect scoped files and whitespace checks. The recording subclass enables lifecycle callbacks in the Editor test assembly only and destroys its temporary AudioClip after teardown. No RED/GREEN execution is claimed.

## Task 2: Bathroom scene binding and asset checks

**Owner:** Separate gpt-6-luna/high implementation agent; no C# component overlap.

**Owned files:** `Assets/Project/Scenes/Bathroom-V2.unity`; new `Tests/Editor/BathroomV2FaucetEffectsSceneTests.cs` and `.meta`.

- [x] Add the effect component and dedicated AudioSource using `m_AddedComponents`, retaining the old WaterTap component disabled. Component ID `6200000000000003001`; AudioSource ID `6200000000000003002`.
- [x] Wire runner `1130038378`, Animator `652140272`, ParticleSystem `1586076941`, dedicated AudioSource and water clip `d2cd9b723149e6f438a81bad51e82cb2`. Audio defaults: Play On Awake off, Loop on, 3D blend, Doppler 0, volume 0.35, min distance 0.5 m, max distance 8 m; configurable in Inspector.
- [x] Disable six legacy callbacks; add one completed `SetOpen(true/false)` callback per Touch source. Activation has no faucet effect.
- [x] Add scene ownership/wiring and complete local PPtr closure tests, including stripped prefab headers. Unity execution is pending.
- [x] Static validation: 298 unique document IDs, zero missing local references, zero duplicate IDs. New files/scene entries pass whitespace checks; pre-existing scene whitespace is preserved.

## Review and handoff

- [x] Independent combined review completed without production/scene findings. Focused re-review of the Editor lifecycle/resource fixture correction is clean.
- [x] Full GitNexus refresh completed: 28,686 nodes, 52,218 edges, 300 flows. Main-checkout detect-changes run; its CRITICAL aggregate includes pre-existing shared changes and does not count new/untracked files as a complete isolated diff.
- [x] Handoff recorded at `docs/superpowers/handoffs/2026-10-08-bathroom-v2-faucet-effects.md`, including files, test filters, Inspector references and unverified headset behavior.
- [ ] User acceptance: open twice without restart; close twice; water remains open across soap/rub steps; pause/resume while open; close while paused then resume stays silent; abort/end/unload stops and resets; starting a new run starts closed; water sound and NPC speech do not interrupt each other.
