# SDD ledger — plan: docs/superpowers/plans/2026-09-04-voice-phrase-storage-and-transport-v2.md

## Preflight scan

| Scope | Produces / consumes | Finding | Ruling |
|---|---|---|---|
| Task 1 | Docs / Tasks 2, 3, 5, 8 | Storage and packet contract is upstream of all code. | Contract docs are authoritative; code must match them. |
| Task 2 ↔ 3 | Types/domain / persistence actions | Actions consume pure resolver types and validation. | Keep resolver dependency-free and server-reusable. |
| Task 2 ↔ 4 | Resolver / UI | UI needs defaults locked and additions editable. | Keep legacy UI branch; V2 branch uses resolver types. |
| Task 2 ↔ 5 | Type shape / Unity DTO shape | Both must key by `binding_id` and preserve defaults-first order. | Keep language-specific DTOs but identical field semantics. |
| Task 5 ↔ 6 | Scene gate / transport | Snapshot must exist before V2 scene activation and transport use. | Scene gate owns readiness; transport never fetches Firestore. |
| Task 5 ↔ 7 | Snapshot / source | Source resolves only immutable snapshot by binding id. | Do not modify SessionContext or base Terminate. |
| Task 6 ↔ 7 | Transport / VoiceQuestSourceV2 | Source consumes activation-correlated signals. | One activation id per source activation; stale signals ignored. |
| Task 8 ↔ 9 | Agent runtime / acceptance | Runtime must prove first-win, cancel, replay, and same phrase list. | Tests and manual acceptance cover all transitions. |
| Task 1 | Docs only | No internal contradiction found. | Proceed. |
| Task 2 | Web domain + tests | Test cases cover every stated resolver rule. | Proceed. |
| Task 3 | Actions | Revision transaction and additive-only write agree with constraints. | Proceed. |
| Task 4 | UI | Legacy fallback and V2 lock semantics agree. | Proceed. |
| Task 5 | Unity loading | Async scene gate and failure policy agree. | Proceed. |
| Task 6 | Unity transport | Raw adapter preserves legacy interface. | Proceed. |
| Task 7 | Voice source | Own termination observation avoids HIGH-risk base edit. | Proceed. |
| Task 8 | Python agent | One active activation and one phrase list satisfy spec. | Proceed. |
| Task 9 | Cross-stack | Verification commands and manual checks are concrete. | Proceed. |

Ruling: Work in the current checkout on branch `codex/voice-phrase-transport-v2` because the source checkout already contains user-owned dirty changes and creating a clean linked worktree would require moving or stashing them. Cost if wrong: unrelated local changes remain in the branch and must be excluded from commits.

## Progress

- Task 1: complete (docs updated in working tree; no code changes)
- Task 2: complete (Web resolver + 5 passing tests)
- Task 3: complete (Web authorized transactional actions; TypeScript pass)
- Task 4: partial (V2 editor guard and save branch; live session V2 read branch; legacy path preserved)
- Task 5: partial (Unity resolver/store/loader and scene activation gate added; Editor tests/manual scene validation pending)
- Task 6: partial (Unity raw LiveKit V2 adapter and correlated transport added; Unity Editor tests pending)
- Task 7: partial (VoiceQuestSourceV2 added with termination cancellation; Unity Editor tests pending)
- Task 8: complete (Python V2 runtime; 15 tests pass, Ruff pass)
- Task 9: blocked by unavailable Unity Editor and Web `.next` write boundary; cross-stack manual acceptance pending

Ruling: Do not modify the five pre-existing lint failures in `.gitnexus/run.cjs` and `test.js`; they are outside V2 scope. Cost if wrong: Web lint remains red until those baseline files are separately cleaned.

Ruling: Do not run `npm run build` in this workspace because policy marks `D:/Lab/VRA-web/.next` read-only. Cost if wrong: production build evidence is deferred to a permitted Web workspace.

Ruling: Treat GitNexus Web HIGH risk for `LessonsList`/`LiveSessionContent` as a review gate; stop further Web UI edits until a dedicated impact review approves the flow changes. Cost if wrong: V2 UI persistence could regress seven live-session/editor flows.
