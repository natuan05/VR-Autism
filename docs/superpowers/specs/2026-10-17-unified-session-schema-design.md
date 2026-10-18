# Unified session persistence contract

Date: 17 October 2026

Status: Proposed contract for review. No production code or cloud data has been changed for this design.

## Goal

Every Action, Quiz, and Exploration lesson must write the same session contract. The schema is selected by its version, never inferred from a Unity runtime, a `v2_` prefix, or the presence of a particular log array. Runtime implementations adapt to this contract; dashboards, reports, and recommendations consume one reporting model.

This replaces the additive compatibility approach implemented earlier. New documents must not contain both old and new representations. Historical compatibility belongs in a read-only adapter.

## Storage layout

### Session document: `sessions/{sessionId}`

| Field | Meaning |
| --- | --- |
| `schema_version` | `2`, identifying this replacement contract. |
| `child_profile_id` | Child identity; preserve the existing authorization identity. |
| `hosted_by` | Immutable session owner identity. |
| `device_id` | Device identity for operational diagnosis and ownership correlation. |
| `lesson` | Small historical snapshot: `id`, `name`, `type` (`action`, `quiz`, or `exploration`), and optional level `{index, name}`. It is not a copy of the curriculum document. |
| `started_at` | UTC Firestore timestamp for actual session start. |
| `ended_at` | UTC Firestore timestamp, null while running. |
| `elapsed_seconds` | Measured session elapsed time, using the same definition for every lesson. Includes pauses and the closing interval until the lesson's existing terminal signal. |
| `status` | `running`, `completed`, `cancelled`, or `failed`; describes lesson execution, not teacher opinion or quiz correctness. |
| `review` | Null until reviewed, then `{reviewer_id, rating, outcome, evaluation, reviewed_at}`. Rating is finite and 0–10; outcome retains `success`/`failed`; reviewed_at is a UTC Firestore timestamp. Teacher outcome remains distinct from machine status. |
| `recording_ref` | Optional stable recording reference, only if a recording exists. |

The document ID is the session ID. Do not repeat `session_id` inside this document. DataPackets, API DTOs, and authorization snapshots may still require an explicit session ID; this storage rule does not remove those checks.

Preserving the small lesson name/level snapshot is intentional: a later curriculum edit must not rename the historical lesson presentation. Keep measured `elapsed_seconds`: UTC timestamps can move with clock correction, and overlapping step durations cannot reconstruct session elapsed time. Do not also store a `duration` alias.

### Logical activity documents: `sessions/{sessionId}/steps/{stepId}`

All activities use one base structure:

| Field | Meaning |
| --- | --- |
| `content_id` | Stable lesson activity identifier. |
| `order` | Display order within the observed session. |
| `name` | Historical activity label. |
| `kind` | `action`, `question`, or `exposure`. |
| `outcome` | `active`, `completed`, `failed`, `skipped`, or `interrupted`. |
| `assistance` | `independent` or `assisted`; separate from completion and correctness. |
| `hints` | Accepted verbal/visual/physical counts when applicable; omit unavailable dimensions instead of manufacturing observations. |
| `timing` | `{started_offset_seconds, elapsed_seconds, hint_response_seconds}`. Start offset uses the monotonic session clock; elapsed accumulates that activity's measured execution intervals, including its retries; hint response is nullable when unavailable. Do not store another end-offset/duration alias for the same accumulated measurement. |
| `assessment` | `{correct: boolean}` for an actually assessed Quiz answer; absent for skipped answers and non-assessment activities. |
| `details` | Small typed activity-specific evidence, not another copy of the lesson or activity definition. |

Action retries/activations project into one deterministic logical step. A delayed write from an earlier activation must not replace a newer authoritative outcome. Preserve existing accepted-hint, run ownership, ordering, and retry guards.

Quiz records the actual answer and correctness. Skip has no correctness assessment and cannot increase correct-answer counts. An answered incorrect question and a failed Action activity must retain their distinct assessment evidence even when both are unsuccessful learning outcomes.

Exploration records actual content presentation and interaction/exposure timing. Presentation is not evidence that the child looked at or understood the content. Do not manufacture an accuracy score or a cognitive completion percentage for Exploration.

### Evidence documents: `sessions/{sessionId}/events/{eventId}`

Use a shared event envelope `{type, source, occurred_at, offset_seconds, payload}`. Occurred_at is a UTC Firestore timestamp; offset_seconds uses the monotonic session clock and may be null for a historical observation without a reliable offset. This collection holds activation/retry evidence, accepted/rejected commands, sensor alerts, and teacher observations. Stable event IDs support idempotent writes. Preserve command IDs and activation IDs where they carry correlation or audit meaning.

Events must not copy the complete current logical step or repeat whole lesson definitions. Their purpose is historical evidence; the logical step is the reporting result. Command and activation audit events are append-only. Alert duration updates and teacher annotations refer to the same stable observation ID rather than creating duplicate array entries; observation updates require revision/ownership guards so stale requests cannot replace a newer observation or teacher note.

Sensor samples continue using their existing RTDB transport. This proposal does not copy all samples into each Firestore session or introduce Freeze/stress detection.

### Operational runtime documents

Keep launch, execution, and TTS authorization state separate from reporting data. Existing `sessions/{id}/runtime/tts_cache`, retained session configuration, request-ID retry guarantees, and rejection/publication semantics remain valid. Do not delete them as if they were duplicate reporting fields.

Move V2-only graph/run/activation correlation and content/voice revision metadata out of the root reporting document into the appropriate operational context or audit evidence. Preserve current RTC/RTDB/DataPacket contracts unless a separately coordinated change requires otherwise.

## Reporting and redundant data

Do not persist a generic `score`, `correct_count`, `attempted_count`, `accuracy_percent`, or `completion_percent` alongside canonical activity evidence. Remove the new root-level compatibility metric fields and the old root `quest_logs`/`node_logs` arrays and `v2_*_logs_by_id` maps from new-schema writes.

Compute results through one reporting service:

- Quiz accuracy uses eligible correctness assessments; skipped and assisted results remain excluded from the approved independent-accuracy denominator. Report assistance separately.
- Action completion uses logical activities and includes completed activities with assistance. Do not count assisted completion as failure.
- Exploration reports presented content/interactions and measured exposure; accuracy is unavailable, not zero.
- Teacher rating is a human judgment on a 0–10 scale. It never replaces machine outcomes or becomes a quiz percentage.

List responses and detail responses are separate DTOs. Query only the steps needed for a requested summary and cache computed summaries at the application boundary. Do not load all audit events for dashboards. Invalidate cached results when activities or reviews change; session IDs alone do not identify a fresh report.

No persisted summary counters are introduced in the initial canonical schema. If measured production query cost later justifies one, propose one rebuildable summary explicitly; do not silently reintroduce the old parallel representations.

## Write and lifecycle rules

All runtimes use one shared session-writing model and lifecycle contract. The adapters provide activity-specific observations; they do not invent alternate session field names.

- Preserve actual closing behavior: Exploration audio/rest and Quiz result delay precede the existing ready-to-exit signal. Action graph timing remains user-owned.
- Ended sessions cannot be reopened by delayed telemetry or an old activation.
- Machine writes do not overwrite `review`; review writes do not overwrite machine identity, timestamps, timing, or outcomes.
- Preserve offline queuing/retry behavior. Do not add transactions solely to merge unrelated fields if that removes existing offline queuing.
- First-review child counting remains transactional and idempotent. Its current meaning is reviewed sessions; do not silently redefine it as all launched sessions.
- Teacher review authorization verifies the stored owner and assigned child. Unknown owners are not accepted.
- The child returns to GameMenu while the teacher remains to review and save. Drafts, observation notes, and save retries are retained.

## Required adaptation surface

Unity: `Cloud/Models/SessionData.cs`, `Cloud/FirebaseManager.cs`, `Core/Manager/TimeManager.cs`, the shared step/event models and writer, Quiz activity recording, Exploration presentation recording, and the V2 telemetry sink/reducer projection. Existing graph execution and NPC audio behavior are not part of this storage refactor.

Web: session types, the reporting service, history list/detail readers and cache invalidation, expert/parent/center statistics and reports, radar/trends, chatbot context, AI recommendations, summary saving, and authorization of step/event reads. Inspect all remaining queries for `hostedBy` versus `hosted_by`, `startTime` versus timestamp fields, and incompatible status names; translating JSON alone cannot fix those readers.

Audit the TTS session/auth/cache modules before moving any session runtime fields. Preserve their existing identity snapshots and service/session authorization.

## Historical records and rollout

New writers emit only schema 2. A read-only adapter maps historical records to the same reporting DTO. It must not guess missing correctness, repair old skip-as-success records without evidence, or silently assign a timezone to old timestamps lacking offsets.

Do not migrate or delete cloud records as part of local implementation. A data migration requires a separate, reviewable mapping and explicit authorization. Keep existing uncommitted work, make no commits or pushes, and do not run Unity or provider synthesis during this design stage.

## Acceptance criteria

1. Action, Quiz, and Exploration produce the same root structure and logical activity/event envelopes.
2. New writes contain no legacy result arrays, duplicate maps, generic raw score, or stored percentages.
3. All report, chat, and recommendation consumers use the same reporting service; runtime-specific persistence shapes do not leak into UI code.
4. Quiz skip/correctness, Action assisted completion, and Exploration non-assessment semantics remain accurate.
5. V2 retries, ordering, stale-event rejection, TTS runtime configuration, and authorization remain intact.
6. Review data cannot be overwritten by late machine writes, and first-review counting cannot double-increment.
7. Focused tests cover cross-runtime fixtures, historical reads, stale writes, ownership, review races, unavailable assessments, and cache invalidation. Unity compilation and focused headset checks remain with the user.
