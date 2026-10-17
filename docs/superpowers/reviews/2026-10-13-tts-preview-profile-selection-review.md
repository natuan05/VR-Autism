# Preview Profile Selection — Plan Review

Date: 2026-10-13

Reviewed: [Implementation plan](../plans/2026-10-13-tts-audio-cache-implementation.md) and its referenced [specification](../specs/2026-10-13-tts-audio-cache-design.md).

Scope: replace mandatory single-NPC lesson/quest context with an explicit editor-wide selection from existing Firestore profiles. Selection is transient; per-row Preview reuses or generates the selected phrase/profile audio. Selecting a profile or scrolling does not generate audio or save a phrase/NPC association. Rejection targets the listened artifact. Approved live-invocation independence and preview/publication coalescing remain unchanged.

Method: independent adversarial, edge-case and structure lenses, followed by source verification. No `project-context.md` matched the project inventory search. This review does not edit the plan, spec or product code.

## Conclusion

The new choice is compatible with the existing cache identity, artifact rejection and roomless generation design. The current plan nevertheless explicitly implements the superseded context-only behavior. Update the preview contracts, Task 8, its tests, the approval summary and the linked spec together before implementation.

Existing correct safeguards should remain: backend authorization, server-resolved effective TTS settings, artifact-specific signed receipts, stale-request rejection, configuration-based keys, and generation coalescing by profile/key/rejection epoch. A selected profile ID is not permission to submit arbitrary voice parameters.

## Adversarial findings

| ID | Plan location | Conflict or gap | Required reconciliation |
| --- | --- | --- | --- |
| A1 | Approval status, line 13 | The resolved-choice summary still requires one known NPC. | Record explicit Firestore profile selection; update spec Sections 11/14 together. |
| A2 | HTTP contract, line 118 | Profile options are filtered through unique-NPC quest mappings. | List existing shared profiles independently of NPC mappings; list access performs no synthesis or writes. |
| A3 | HTTP contract, line 119 | Preview requires `context_binding_id` and rejects a selected profile override. | Accept a selected known `profile_id`, resolve its settings server-side, and retire the mandatory NPC-context rule. Keep staff/child authorization and reject arbitrary settings. |
| A4 | Task 8, line 268 | The public interfaces expose NPC contexts rather than selectable profiles. | Replace context-list/resolution DTOs and functions with explicit profile-list/resolution contracts and defined unavailable-profile outcomes. |
| A5 | Task 8, line 264 | Preview requires enriching phrase DTOs/loading with NPC context. | Remove preview-only NPC enrichment; keep phrases shared and selection in editor state. Preserve authorization-helper work that remains necessary. |
| A6 | Task 8, line 270 | The task explicitly prohibits a profile picker and disables multi-NPC/unmapped phrases. | Add one editor-wide profile selector and permit every authorized phrase row to use its selection. |
| A7 | Task 8, line 271 | Tests enforce missing/ambiguous NPC-context failure and restrict quest voice choice. | Test one phrase under two selected profiles, no NPC mapping prerequisite, and unknown-profile rejection before generation. |
| A8 | Task 8, line 275 | UI instructions present an actual NPC without defining ownership of the global selection. | Own selection in the editor, pass it to every row, and display profile/voice/rate without inventing a phrase's unique speaker. |
| A9 | Task 8, lines 275–276 | Existing per-row clearing/response guards need explicit editor-wide behavior after a global selection change. | Stop current audio, clear affected receipts and cancel/ignore pending old-profile responses across all rows. Include profile/selection identity in response guards. |
| A10 | Task 8, line 275 | No-generation wording covers edit/open, but not the new selector or scrolling. | Add zero-generation/zero-save tests for selection, scrolling and row mounting; only an explicit per-row Preview initiates audio lookup/generation. |

## Edge-case findings

| ID | Location | Unhandled path | Guard and consequence |
| --- | --- | --- | --- |
| E1 | HTTP contract, lines 118–119 | A valid selected profile has no uniquely mapped quest NPC. | Resolve the selected Firestore profile directly. Otherwise valid shared/general phrases remain blocked. |
| E2 | HTTP contract, line 119 | Effective settings change after profile options load but before Preview. | Bind the request to the displayed settings, or reconcile the display with authoritative returned settings before playback. Otherwise the teacher may hear settings different from those shown. Do not introduce an incrementing profile version. |

E1 overlaps A2/A3: independent lenses found the same contract conflict. E2 is a separate configuration-consistency issue. The plan already protects artifact receipts and clears them on profile edits; A9 asks to extend that existing protection to the new global selection rather than add a second rejection mechanism.

## Structure findings

| Pass | Location | Recommendation | Word impact |
| --- | --- | --- | --- |
| structure | Approval summary, line 13 | CONDENSE into the current product choice and retained implementation-approval boundary. Claim incorporation only after contracts/tasks agree. | Approximately 0; replace the existing summary rather than add another. |
| structure | Shared contracts, lines 118–120; Task 8, lines 262–278 | MERGE selection behavior into one shared preview-profile contract, retain concrete HTTP shapes, and reference it from UI/test tasks. Reconcile helper names, files and test commands together. | Reorganization alone: 0. Measure semantic replacement after revision. |

## Minimum coordinated revision

1. Replace the stale product choice in both plan and spec.
2. Define a profile-list response and preview request using a known `profile_id`; backend retrieves effective settings from Firestore.
3. Replace context-only helpers/routes/tests, removing unnecessary phrase/NPC data changes.
4. Specify one editor-wide selection and per-row explicit preview, including global stale-response/receipt cleanup and settings-change handling.
5. Verify two profiles produce separate keys/artifacts, rejection affects only the listened artifact, and selection/scrolling cause no TTS or phrase writes.

No cache-key redesign, rejection-epoch redesign, new profile-version system or live-stream coalescing change is indicated by this review.

## Machine-readable findings

```json
[
  {"lens":"adversarial","location":"Implementation plan:13","trigger_condition":"Approval summary still mandates the superseded single-NPC preview context.","guard_snippet":"Replace it with explicit Firestore profile selection and update the referenced spec.","potential_consequence":"Implementers treat the obsolete choice as authoritative."},
  {"lens":"adversarial","location":"Implementation plan:118","trigger_condition":"Listing exposes only profiles reachable through unique-NPC quest contexts.","guard_snippet":"List existing shared profiles independently of quest mappings without synthesis or writes.","potential_consequence":"Configured profiles disappear from the selector."},
  {"lens":"adversarial","location":"Implementation plan:119","trigger_condition":"Preview requires context_binding_id and rejects explicit profile selection.","guard_snippet":"Accept a known profile_id and resolve effective settings server-side; retain authorization.","potential_consequence":"New selector requests fail, and shared phrases cannot preview each intended voice."},
  {"lens":"adversarial","location":"Implementation plan:268","trigger_condition":"Public DTOs/functions model NPC contexts rather than selectable profiles.","guard_snippet":"Define profile-list/resolution interfaces and unavailable-profile errors before generation.","potential_consequence":"Frontend and backend implement incompatible contracts."},
  {"lens":"adversarial","location":"Implementation plan:264","trigger_condition":"Preview requires NPC-context enrichment of shared phrase DTOs/loading.","guard_snippet":"Remove preview-only NPC enrichment; retain transient selection in editor state.","potential_consequence":"Unnecessary phrase-schema changes imply an incorrect unique speaker association."},
  {"lens":"adversarial","location":"Implementation plan:270","trigger_condition":"UI instructions explicitly prohibit the requested profile picker.","guard_snippet":"Add an editor-wide selector usable by every authorized phrase row.","potential_consequence":"The requested interface is omitted and valid phrases remain disabled."},
  {"lens":"adversarial","location":"Implementation plan:271","trigger_condition":"Tests enforce context-only voice selection and multiple-NPC failure.","guard_snippet":"Test separate selected profiles without NPC mapping and fail unknown profiles before generation.","potential_consequence":"Tests reject a correct implementation of the new behavior."},
  {"lens":"adversarial","location":"Implementation plan:275","trigger_condition":"The task lacks ownership of editor-wide selection and assumes actual-NPC display.","guard_snippet":"Store selectedProfileId in the editor and show the selected profile's voice/rate.","potential_consequence":"Rows retain inconsistent voices or present an invented unique speaker."},
  {"lens":"adversarial","location":"Implementation plan:275-276","trigger_condition":"Global profile changes need propagation to existing row response and receipt guards.","guard_snippet":"Stop audio, invalidate receipts, and cancel/ignore old selection responses across rows.","potential_consequence":"Old-profile audio or rejection becomes active under the new selection."},
  {"lens":"adversarial","location":"Implementation plan:275","trigger_condition":"No-generation tests omit selector changes, scrolling and row mounting.","guard_snippet":"Assert zero generation and saves until an explicit per-row Preview click.","potential_consequence":"Navigation effects create unrequested TTS charges or writes."},
  {"lens":"edge-case-hunter","location":"Implementation plan:118-119","trigger_condition":"Selected valid profile has no uniquely mapped quest NPC.","guard_snippet":"profile = resolveCurrentFirestoreProfile(input.profile_id); // No NPC-context prerequisite","potential_consequence":"Valid shared and general phrases remain blocked."},
  {"lens":"edge-case-hunter","location":"Implementation plan:119","trigger_condition":"Profile settings change after options load and before Preview.","guard_snippet":"Verify expected effective settings or reconcile the displayed settings before playback.","potential_consequence":"Played audio differs from the settings the teacher sees."},
  {"lens":"structure","location":"Approval summary:13","pass":"structure","change":"CONDENSE","original_text":"Completed plan summary records mandatory single-NPC preview context.","revised_text":"State explicit Firestore profile selection and retain the concurrency/approval boundaries.","word_impact":"Approximately 0"},
  {"lens":"structure","location":"Shared contracts:118-120; Task 8:262-278","pass":"structure","change":"MERGE","original_text":"Selection rules are scattered across context-only endpoint shapes, helpers, DTOs, UI and tests.","revised_text":"Centralize the new selection rule; reconcile concrete contracts, task ownership and test commands.","word_impact":"Reorganization: 0; measure semantic replacement after revision"}
]
```
