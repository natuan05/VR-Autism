# Story 2.4 Firebase audit — 2026-10-09

## Scope and outcome

Final scope follows the user's correction: compare one completed V1 lesson as a structural baseline, and audit V2 sessions started on 2026-10-09 in Vietnam/Asia-Bangkok time (UTC+7). The day window is 2026-10-08 17:00 UTC inclusive through 2026-10-09 17:00 UTC exclusive, read up to the audit time. Earlier V2 test/history records are excluded from the current verdict.

The five V2 sessions in this window are all Bathroom. Their session identity, terminal status, node/quest projections, audit events, and populated sensor snapshots agree across stores. No LearnToAsk-V2 session appears in this day window. Two current issues remain: deployed Firestore authorization permits unauthorized reads/writes, and disconnect-path `vr_state.ended_at` records the handshake time rather than the disconnect time.

The user confirmed Unity compilation/test reruns and both selected lessons passing with legacy controllers disabled. This is user-provided runtime evidence; it does not supply missing deployed LearnToAsk persistence evidence or constitute final cutover acceptance. No controllers were removed.

## Method and preservation

Project `vra-project-96d9c`, Firestore database `(default)`. Sources: `D:/Lab/VR-Autism` main at `6f55c39b`, and `D:/Lab/VRA-web` main. Three gpt-6-luna/high subagents performed Firestore auditing, RTDB correlation, and independent deployed-rules review.

All database reads went through Firebase MCP. The official MCP supplied project metadata, deployed Firestore rules and the web SDK configuration. The existing web `scripts/mcp/firebase-mcp.mjs` supplied read-only data tools through stdio. Node used an already-cached MCP SDK without installation. The server's hardcoded obsolete RTDB URL was corrected only in an in-memory loader to the regional URL confirmed by the official MCP: `https://vra-project-96d9c-default-rtdb.asia-southeast1.firebasedatabase.app`.

No source, Firebase records, rules or credentials were modified. No raw personal data was saved. The pre-existing sprint-status edit remains untouched. This report is the only new repository file. The broader initial corpus discovery found 78 Firestore sessions and 127 RTDB live-session records; it is not treated as the final acceptance scope.

## V1 structural baseline

Reference: `sessions/a75ba7ee-b2f1-4168-a5ef-ea6f164ee9f1`, lesson `WashingHand_1`, success. Legacy start `2026-07-08T14:46:23`, finish `2026-07-08T14:47:15`; neither string includes a timezone suffix.

The document contains session, child, host, lesson/name, level, device, type, start/finish, duration, completion, score, video and quest-log fields. It contains six successful `quest_logs`, with all seven web-required quest fields and no duplicate quest indexes. `response_time_from_hint` is present on each row but remains an optional compatibility field. There are no V2 node logs or runtime identity maps.

RTDB has the expected V1 `vr_state/current_activity` representation, without a V2 graph/run, and 25 legacy sensor samples without V2 identity fields. Its confirmation at 07:46:23.147 UTC is consistent with interpreting the naive Firestore start as local UTC+7, although the stored string itself does not declare that zone. The samples span 48.114 seconds. Its disconnected end timestamp equals confirmation time.

This is a structural reference, not a claim that every legacy record is correct. Its session `type` is `theoretical` while the referenced lesson catalog says `practical`; that observed inconsistency should not be copied into V2. Stored duration is 21.037 seconds versus a 52-second timestamp span; the audit does not establish whether legacy timing semantics or an incorrect write explains that difference. One reference does not certify all V1 lessons or shared Quiz/Exploration regressions.

## V2 sessions started on 09 October, UTC+7

| Session ID | Local start | Terminal status | Node logs | Quest projections | Audit events |
| --- | --- | --- | ---: | ---: | ---: |
| `56632813-169b-47bb-87bd-d67ce64edbb2` | 13:27:45 | success | 9 | 5 | 19 |
| `d7900750-a1e8-4635-8550-da23bffd052e` | 13:41:06 | success | 9 | 5 | 20 |
| `d33bc065-8d6c-493d-9a66-ef6d7a9b92be` | 13:48:55 | success | 9 | 5 | 20 |
| `7e301cf5-de6f-4389-bb5c-6a47505cf951` | 14:04:08 | cancelled | 2 | 1 | 5 |
| `786a8b25-194b-4d48-a7be-dfca425418e0` | 16:16:35 | success | 9 | 5 | 20 |

All five use `Bathroom-V2-LessonGraph_Schema2` / lesson `WashingHand_2`. Totals: four success, one cancellation, 38 node logs, 21 quest projections, 84 audit events. The unfiltered Firestore read was below its 300-document cap; all scoped event collections were below their 500-event caps. Ordering alone was not used to prove corpus completeness because documents without the order field can be excluded.

### Integrity and synchronization

- RTDB graph identities and Firestore session/run/graph/lesson/launch/revision identities match. Per-node session/run/graph/lesson/activation identities match their owner.
- RTDB completed maps to Firestore success; cancellation also agrees. Firestore finish times and RTDB graph updates agree to approximately 1 ms.
- Node/quest ID maps match their compatibility arrays; stable event IDs and closed-node/quest projections are consistent, with no duplicate event IDs found.
- Session duration is present and consistent with the terminal audit. A cancellation is an expected terminal outcome, not itself a data-integrity failure.
- Both stores retain the intended V1-compatible session and quest information; V2 adds node/activation/run identity and audit history. Maps plus ordered arrays are deliberate projections, not duplicate session writes.
- The V1 reference has `video_url`; it is absent from the five V2 documents. Optional-field compatibility can display an unavailable video, but these reads do not establish whether any video was recorded or whether its URL should have been written. Recording/URL persistence remains unverified.
- Legacy quest projections intentionally do not carry V2 session/run fields. Identity is provided by their containing session and V2 node/audit representation.
- No `live_sessions/{id}/commands` branch exists for any of these five sessions; no legacy current-activity ownership conflict was observed. V2 controls use LiveKit DataPackets, so absence of RTDB command children is expected. Three success sessions have a command-accepted audit; the success with 19 total events has none, and the cancelled run has entered/closed/lesson-cancelled events. Full live command/ack behavior is not established by these stored records.

### Sensor snapshots

All five `behavior_snapshots/{sessionId}` branches are populated: 219 samples total (bucket sizes 19, 37, 51, 56, 56). Samples use `runtime=lesson_graph_v2`, matching session/run, nonmissing node/activation, running observation status and nonregressing elapsed offsets. Every observed activation is found in that session's historical node logs. A running historical sensor sample is compared with its historical node activation, not the final completed graph state.

Median cadence is approximately 2.004-2.006 seconds; maximum observed gaps are 2.010-2.160 seconds. Short and terminal nodes need not have a sample under this cadence. Omitted `active_binding_ids` on some samples is consistent with RTDB omitting empty arrays and the web parser accepting that optional empty representation.

The targeted recent Bathroom documents inspected for alert storage have no `auto_alerts` or `behavior_logs`. No persisted alert/feedback payload was available to correlate; the runtime attachment and storage paths were inspected in source only. This is an evidence limit, not proof that a triggered alert would be persisted correctly.

## Current defects

### Disconnect timestamp is captured too early

Source: `Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs:72-75`. `OnDisconnect` receives a concrete `DateTimeOffset.UtcNow` value when the handshake registers, so the queued update uses registration time. The explicit end path at lines 178-182 instead writes the actual end time.

In the five current V2 sessions, four have VR status disconnected and `ended_at == confirmed_at`; one has explicit ended status with an end time 113.177 seconds after confirmation. The graph and Firestore terminal state still agree. This defect concerns the RTDB disconnect timestamp, not the validated Firestore finish/duration fields.

Use a server-evaluated timestamp for the disconnect update and verify both explicit-end and disconnect paths. No fix was applied in this read-only audit.

### Deployed Firestore rules permit unauthorized mutation

The official Firebase MCP returned the deployed rules; a separate subagent independently confirmed:

- Two overlapping `/sessions/{sessionId}` matches include public read and write. Anyone may read, create, update or delete session documents; the role-restricted match does not override the permissive match.
- `/sessions/{sessionId}/lesson_events/{eventId}` allows create/update based only on path/payload ID equality, without authentication, writer/run ownership or schema validation.
- `/child_profiles` and `/child_phrase_sets` are publicly readable and writable by any authenticated account without assignment checks.

These are current access/integrity defects even though the inspected V2 records are internally consistent. Restrictive rules must preserve the actual Unity/web authentication and assignment model, with intended-client and denied-client verification before deployment. Admin reads bypass rules and are not permission tests. The official MCP returned `Invalid URL` for RTDB rules, so deployed RTDB authorization remains unverified.

Independent rules assessment:

```json
{
  "score": 1,
  "summary": "Current Firestore rules permit unauthorized session access and telemetry mutation.",
  "findings": [
    {"check": "Identity authorization", "severity": "critical", "issue": "Public session read/write, including delete", "recommendation": "Authenticate and enforce verified session ownership and reader assignment"},
    {"check": "Identity authorization", "severity": "critical", "issue": "Public child profile reads and unrestricted authenticated writes", "recommendation": "Enforce role and child assignment; restrict mutable fields"},
    {"check": "Authority and type safety", "severity": "major", "issue": "Unauthenticated lesson event create/update with only path-ID equality", "recommendation": "Authorize the writer and validate immutable IDs, run and event schema"},
    {"check": "Identity authorization", "severity": "major", "issue": "Public phrase sets and unrestricted authenticated writes", "recommendation": "Enforce child/lesson assignment and schema/size constraints"}
  ]
}
```

## Remaining evidence and next step

No LearnToAsk-V2 session started on 09 October exists in the audited data. Its user-confirmed runtime pass remains accepted; a paired persisted run or exact session context is needed for the Firebase gate. Browser rendering, alert-trigger persistence, intended/denied client access and full shared-consumer regressions were not exercised by these read-only checks.

Earlier V2 test/history discrepancies are excluded from this current verdict as requested. Do not delete or backfill historical data merely from this audit. Resolve the current disconnect timestamp and authorization defects, obtain the missing deployed evidence, then record explicit cutover acceptance before retiring legacy controllers. Deferred Epic 3 stories and the separate Story 4.2 startup migration are not introduced as prerequisites.

## Repository verification

The scoped report was reviewed against both data-audit summaries and the independent deployed-rules review. Root inspected the disconnect timestamp source independently. Repository `git diff --check` passed; GitNexus main-checkout `detect-changes --scope all` reported no changed source symbols. The report is new/untracked documentation and does not appear in that source-symbol result. No Unity or test execution was needed for a read-only data audit.

## Follow-up: disconnect fix and user-deferred rules

After the audit, the user requested a fix, then explicitly deferred rewriting Firebase rules until the app-build hardening phase. The rules agent stopped without modifying files or Firebase configuration. No rules were deployed, and deployed authorization remains as audited above. Deferral does not establish the final access/cutover gate.

`LiveSessionReporter.SendLiveSessionHandshake` now schedules `ended_at` with `Firebase.Database.ServerValue.Timestamp` rather than a client timestamp captured at registration. The change is one source line. It corrects RTDB disconnect time; it does not change Firestore start/finish/duration, explicit lesson-end writes, statuses, or session-generation guards. The bundled Firebase Database 13.9.0 SDK supports this server timestamp field. No new dependency was added.

One independent review found no critical or important issues. Pre-edit upstream impact was LOW with no indexed callers; source inspection identified `TimeManager.Start` as an actual caller missed by the graph. Method-level post-edit detection reports a broader handshake impact, so final Unity and disconnect behavior remain user-owned verification.

Smallest manual checks: run the paired Bathroom-V2 and LearnToAsk-V2 lessons to normal completion and retain their new session IDs; separately force disconnect after handshake and confirm numeric `vr_state.ended_at` is later than `confirmed_at` and approximates server disconnect time. Normal completion should retain the existing explicit-ended behavior. No Unity compilation or headset run was performed by agents; the source fix is uncommitted.
