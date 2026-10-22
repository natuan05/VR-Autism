# Per-child voice silence reminder settings

## Agreed behavior

Experts configure voice silence reminders in the child's existing lesson parameters on the web. A separate toggle controls spoken silence reminders, independent of visual auto hints. A seconds control accepts 1–60 seconds, or system default (5 seconds). Turning the toggle off disables editing the delay without discarding it. Existing children and V2 packets default to enabled. Existing transport behavior for zero seconds is preserved; new web writes must not use zero as an off switch.

Settings apply at the next lesson launch, not live. Unity reads the child's parameters into SessionContext once at launch, and sends the resolved settings with every SET_ACTIVE_QUEST. Agent startup does not read a child's profile. Activation replay must neither replace settings nor restart the opening or silence clock.

## Contract

- Firestore: `default_lesson_params.actions.enable_speech_silence_reminder: boolean` (missing = true).
- Firestore: existing `speech_silence_timeout: number` (-1 = default; new custom values 1–60).
- SET_ACTIVE_QUEST V2: optional `enable_speech_silence_reminder: boolean` (missing = true), alongside existing `speech_silence_timeout_seconds`.
- Agent scheduler: activation-level preference is independent of transport/cache readiness. Both must permit reminders.
- Web server validates both normal save and configuration import; unrelated parameter categories are retained.

## Independent startup

For the current cache-aware LessonGraph V2 launch, reuse authenticated FIXED_AUDIO_HELLO and reconciliation. Once Unity accepts the current Agent's HELLO, replay the retained active voice quest including its settings. Do not replay terminal/cancelled quests. Repeated HELLO is safe through existing activation idempotency. Existing Unity reconnect replay remains. No new readiness protocol, polling, startup ordering requirement, RTDB key, or Firestore listener is introduced. Legacy non-cache voice startup is outside this HELLO extension.

## Verification and boundaries

Test defaults, false settings, invalid types/ranges, config imports, activation replay mismatch, suppressed countdown despite readiness re-enable, and next activation re-enable. Add Unity EditMode coverage for dictionary parsing, source propagation, reconnect payload and authenticated late-Agent replay. Run focused Python/Vitest tests and static checks. Do not launch Unity; user runs focused Editor filters and headset checks. Preserve pre-existing scene/recovery/document changes. Do not push, deploy, or commit unrelated changes.

Transport reference: [LiveKit Data packets](https://docs.livekit.io/transport/data/packets/) documents that reliable packets are not buffered for disconnected receivers; explicit readiness/replay is required.
