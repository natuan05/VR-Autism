# Voice Phrases V2 Cross-Epic Handoff

**Origin:** Story 1.5 implementation  
**Consumers:** Epic 2 and Epic 3  
**Status:** Implemented foundation; downstream stories must consume it
**Canonical implementation source:** [ARCHITECTURE-SPINE.md](../_bmad-output/planning-artifacts/architecture/architecture-voice-phrase-v2-story-1-5-2026-09-05/ARCHITECTURE-SPINE.md)

## Authority and reading order

1. ARCHITECTURE-SPINE.md is the authoritative implementation contract for Story 1.5 and wins when source plans disagree.
2. This handoff is the compact cross-epic index for Epic 2 and Epic 3; it points downstream readers to the decisions they must preserve.
3. VOICE_PHRASE_STORAGE_V2_PLAN.md and the dated implementation plan are retained as historical source/detail references, not competing contracts.

## Purpose

Story 1.5 refactored Firebase Firestore phrase storage and the cross-stack voice contract for Lesson Flow Engine V2. Epic 2 and Epic 3 must extend this foundation, not recreate or bypass it.

## Firestore V2 contract

A V2 lesson carries:

- voice_schema_version: integer 2
- voice_revision: non-negative integer
- quests: ordered entries with unique binding_id, non-empty goal, and default_phrases

A child lesson phrase document uses ID {childId}__{lessonId} and carries schema_version: 2, scope: lesson, exact child_id and lesson_id, non-negative revision, server update metadata, and ordered quest_additions: [{binding_id, phrases[]}].

A child general phrase document uses ID {childId}__general and carries the same metadata with scope: general and ordered phrases[].

Defaults are immutable to specialists. Child additions are keyed by stable binding_id; dotted binding IDs must remain values, never Firestore map paths.

## Resolution and snapshot rules

The effective quest phrase list is canonical lesson defaults plus authorized child additions. Resolution trims Unicode whitespace, removes empty values, deduplicates case-insensitively, and preserves first spelling and order. Values over 240 characters are rejected. A quest accepts at most 50 additions; general phrases accept at most 50 entries.

Session launch resolves phrases once and atomically replaces the runtime snapshot. Missing child documents or empty additions use defaults. Child-fetch failure degrades to defaults with an observable warning. The runtime must not fall back to Inspector phrases or mutate the resolved snapshot during a session.

## V2 packet/runtime rules

V2 packets use contract_version 2, uppercase event names, snake_case fields, and non-empty correlation IDs. Quest packets require activation_id. QUEST_STATUS is ACTIVE, MATCHED, CANCELLED, or FAILED; optional reason values explain terminal rejection/failure.

The same effective quest phrase list drives agent opening speech, verbal hints/reminders, and evaluation examples. General phrases are separate and are sent only as SPEAK_SCRIPT; they are never merged into quest evaluation context.

Every resume, retry, or reactivation creates a new activation_id. Old packets and callbacks cannot complete the new activation.

## Ownership

- Firestore server actions own authorization, normalization, deduplication, revision compare-and-swap, and audit metadata.
- Unity phrase loading/resolution owns validation and immutable session snapshot replacement.
- LiveKitVoiceQuestTransport owns packet adaptation and main-thread delivery; it does not own graph routing or microphone capture.
- LiveKitService remains the only microphone owner and owns remote-track audio binding.
- agent_v2.py and its V2 runtime own activation correlation, cancellation, and status publication; agent.py remains legacy.
- Web consumers use the V2 actions/lib/types contract and must not write legacy quick_phrases during V2 flow.

## Downstream requirements

Epic 2 must:

- pass the resolved snapshot and stable NPC audio-route context into Dialogue/SPEAK_SCRIPT and voice-quest execution;
- preserve phrase-set and lesson revisions in observable launch/activation context where telemetry needs them;
- keep dialogue/general speech separate from quest evaluation;
- test stale revision, missing child data, activation retry, and transport reconnect paths.

Epic 3 must:

- author stable graph binding IDs and optional NPC route identity, never Unity object references or copied phrase defaults;
- preserve snapshot immutability across Parallel, Loop, Checkpoint, and resume flows;
- validate that every voice-enabled binding can resolve a participant before execution.

## Non-goals

- Reimplementing Firestore migration or phrase normalization in Epic 2/3.
- Replacing canonical lesson defaults from the dashboard.
- Switching production deployment from agent.py to agent_v2.py without a separate cutover decision.
- Adding microphone capture to a quest source or graph executor.

## Verification handoff

Downstream implementation should consume the checked-in C#, Python, and TypeScript contract tests/fixtures and add only story-specific coverage. Any schema or packet change must update all three stacks together.
