# Voice Phrase Storage V2 Plan

**Status:** Normative implementation contract - approved  
**Date:** 2026-09-04  
**Scope:** Voice phrase storage and resolution for the LessonGraph V2 path

## 1. Purpose

Replace the duplicated, index-coupled legacy phrase model with one stable,
child-aware phrase configuration for each voice quest. The Voice Agent must
receive one resolved list and use that same list for:

- the opening phrase;
- verbal hints and reminders;
- context/examples when evaluating the child's response.

The three uses intentionally share one list. This design changes how that
single effective list is stored and resolved; it does not split the list by
runtime purpose.

## 2. Current State and Problems

The current Firestore model stores phrase data in multiple places:

- `lessons/{lessonId}.quests[].default_phrases` contains lesson defaults;
- `child_profiles/{childId}.quick_phrases` copies phrase lists for many lessons;
- legacy Unity `VoiceQuest.defaultPhrases` provides another fallback.

The current `quick_phrases` structure is also heterogeneous: `general` is an
array of strings, while lesson entries are arrays of objects containing
`quest_name` and `phrases`.

Runtime resolution currently converts the selected lesson's phrase objects
into a positional list and stores it in `SessionContext` by quest index. This
causes several problems:

- lesson defaults are duplicated into every child profile;
- default updates do not propagate safely to copied child data;
- adding, deleting, or reordering quests can attach phrases to the wrong quest;
- display names such as `Bật vòi nước` and technical names such as `Quiz_Q1`
  are used as identity even though they may change;
- partial or malformed phrase arrays can shift later quest indexes;
- three fallback sources make the effective value difficult to explain and
  audit.

## 3. Decisions

1. Lesson defaults remain the canonical source in `lessons`.
2. A specialist may add child-specific phrases but cannot delete or replace
   lesson defaults.
3. The effective list is `default_phrases + child additions`.
4. Phrase data is matched by stable `binding_id`, never by quest index or
   display name.
5. The existing LessonGraph V2 binding ID is reused; no parallel quest-key
   concept is introduced.
6. Phrase configuration is resolved once when a session starts and remains
   immutable for that session. Edits apply from the next session.
7. Scene loading and phrase fetching run concurrently, but scene activation is
   gated until phrase resolution completes.
8. The Agent receives only the current activation, quest goal, and effective
   phrases. It does not need child, session, lesson, or clinical identity data.
9. General quick phrases remain a separate child-specific library and are sent
   through `SPEAK_SCRIPT`; they are never merged into quest phrases.
10. Legacy `child_profiles.quick_phrases` remains read-only during migration.

## 4. Firestore Schema

### 4.1 Lesson defaults

```json
// lessons/{lessonId}
{
  "lesson_id": "WashingHand_1",
  "voice_revision": 4,
  "quests": [
    {
      "binding_id": "washing-hand.turn-on-water",
      "title": "Bật vòi nước",
      "goal": "Hướng dẫn trẻ bật vòi nước",
      "default_phrases": [
        "Con hãy bật vòi nước đi nào.",
        "Mở vòi nước đi con."
      ]
    }
  ]
}
```

`binding_id` is the stable machine identity. `title` is display text and may be
renamed or localized without breaking phrase resolution.

### 4.2 Child-specific additions

Use one top-level document per child and lesson:

```json
// child_phrase_sets/{childId}__{lessonId}
{
  "schema_version": 2,
  "scope": "lesson",
  "child_id": "child-123",
  "lesson_id": "WashingHand_1",
  "revision": 3,
  "quest_additions": [
    {
      "binding_id": "washing-hand.turn-on-water",
      "phrases": [
        "Con thử mở nước giống lúc ở nhà nhé."
      ]
    }
  ],
  "updated_at": "2026-09-04T00:00:00Z",
  "updated_by": "expert-456"
}
```

`quest_additions` is an array because binding IDs contain dots and should not
be used as Firestore map field paths. A single lesson document keeps session
startup to one child-phrase read.

### 4.3 General quick phrases

```json
// child_phrase_sets/{childId}__general
{
  "schema_version": 2,
  "scope": "general",
  "child_id": "child-123",
  "revision": 2,
  "phrases": [
    "Con làm tốt lắm!",
    "Cố lên con nhé!"
  ],
  "updated_at": "2026-09-04T00:00:00Z",
  "updated_by": "expert-456"
}
```

General phrases are dashboard shortcuts. Selecting one publishes
`SPEAK_SCRIPT { text }`; the Agent speaks the supplied text without adding it
to the active quest configuration.

## 5. Resolution Rules

For each lesson quest:

1. Find the lesson entry by `binding_id`.
2. Read its `default_phrases`.
3. Find the matching child addition entry by the same `binding_id`.
4. Append child phrases after defaults.
5. Trim surrounding whitespace and remove empty values.
6. Remove duplicates while preserving the first occurrence and order.
7. Store the result in a read-only session dictionary keyed by `binding_id`.

Example:

```text
Defaults:  ["A", "B"]
Additions: ["B", "C", ""]
Effective: ["A", "B", "C"]
```

The resolved in-memory entry contains:

```text
binding_id -> goal + effective_phrases[]
```

No Firestore listener is attached after resolution. Changes made by a
specialist during an active session apply only when the next session resolves
its configuration.

## 6. Session Startup Flow

```text
Web selects child and lesson
        |
        v
VR receives childId + lessonId + sceneName
        |
        +--> LoadSceneAsync (allowSceneActivation = false)
        +--> Fetch lessons/{lessonId}
        +--> Fetch child_phrase_sets/{childId}__{lessonId}
                     |
                     v
          Validate and resolve phrase snapshot
                     |
                     v
          Store snapshot in persistent SessionContext
                     |
                     v
          allowSceneActivation = true
                     |
                     v
          LessonGraph/VoiceQuest may start
```

Scene asset loading overlaps with network reads. The activation gate prevents
`VoiceQuestSourceV2` from starting before its phrase configuration is ready.

## 7. Agent V2 Contract

Unity sends only the context needed for the active voice quest:

```json
{
  "event": "SET_ACTIVE_QUEST",
  "contract_version": 2,
  "activation_id": "runtime-generated-id",
  "quest_goal": "Hướng dẫn trẻ bật vòi nước",
  "phrases": [
    "Con hãy bật vòi nước đi nào.",
    "Mở vòi nước đi con.",
    "Con thử mở nước giống lúc ở nhà nhé."
  ]
}
```

`agent_v2` stores only:

```text
activation_id
quest_goal
phrases[]
```

Completion and status packets echo `activation_id`. Unity rejects packets for
an inactive or previous activation. `activation_id` is transport correlation,
not clinical identity.

## 8. Validation and Failure Policy

- Missing child phrase-set document: continue with lesson defaults.
- Missing additions for a binding: continue with that binding's defaults.
- Addition references an unknown binding: ignore the entry and emit a warning.
- Duplicate addition entries for one binding: reject the child phrase-set as
  invalid instead of choosing an arbitrary winner.
- Empty addition list: valid; use defaults only.
- Missing or duplicate lesson `binding_id`: configuration error; do not start a
  voice-enabled lesson.
- Lesson fetch failure for a voice-enabled lesson: keep scene activation gated,
  show a retryable startup error, and do not silently use legacy Inspector
  phrases.
- Child-addition fetch failure: use defaults and log that personalization was
  unavailable for this session.

V2 must make the selected source visible in logs: lesson revision, child
revision, binding ID, default count, addition count, and effective count.

## 9. Migration Strategy

1. Add stable `binding_id`, `goal`, and `voice_revision` to lesson quest data.
2. Create `child_phrase_sets` security rules and Web data models.
3. Convert each legacy child lesson entry:
   - compare its phrases against the matching lesson defaults;
   - omit phrases already present in defaults;
   - write remaining values as child additions;
   - require manual review where the legacy quest cannot be matched reliably.
4. Move `quick_phrases.general` into `{childId}__general`.
5. Change the Web editor so defaults are visible but locked; specialists may
   add, edit, reorder, or delete only child-added phrases.
6. Change the V2 Unity startup path to read and resolve the new schema by
   `binding_id`.
7. Keep legacy scenes and `agent.py` on the old read path during rollout.
8. After all active profiles are migrated and verified, stop writing
   `child_profiles.quick_phrases`; remove the legacy field in a separate cleanup.

Migration must be idempotent and produce a report for unmatched lessons,
unmatched quests, duplicate bindings, and empty phrase sets.

## 10. Acceptance Criteria

- A lesson default exists in exactly one production source.
- A child profile stores only phrases added for that child.
- Specialists cannot delete or replace lesson defaults.
- Effective phrases are defaults followed by normalized, deduplicated child
  additions.
- Reordering, inserting, or deleting LessonGraph nodes does not remap phrases.
- Every voice quest resolves by stable `binding_id`.
- A session uses one immutable in-memory phrase snapshot.
- Mid-session edits take effect only in the next session.
- Scene loading overlaps Firestore reads, while scene activation waits for
  successful resolution.
- Agent V2 receives no child or session identity fields.
- Agent V2 uses the same effective list for opening, hint/reminder, and response
  evaluation context.
- General quick phrases remain separate and travel through `SPEAK_SCRIPT`.
- Legacy scenes and `agent.py` remain unchanged during V2 rollout.

## 11. Deferred Backlog

### Persist the resolved phrase snapshot for audit

Do not include persistence of the resolved phrase snapshot in Story 1.5. A
later story may store the exact effective phrases used by each completed
session so reviewers can reconstruct Agent behavior after lesson defaults or
child additions change.

Candidate session data:

```json
{
  "voice_phrase_snapshot": {
    "lesson_revision": 4,
    "child_revision": 3,
    "quests": [
      {
        "binding_id": "washing-hand.turn-on-water",
        "phrases": ["A", "B", "C"]
      }
    ]
  }
}
```

This audit record belongs to the session persistence layer. It does not add
identity fields to the Agent contract.

## 12. Out of Scope

- Hot-reloading phrase configuration during an active session.
- Allowing specialists to remove or replace lesson defaults.
- Giving the Voice Agent direct Firestore access.
- Sending child or clinical identity to the Voice Agent.
- Migrating legacy scenes to LessonGraph V2 as part of the storage change.
- Persisting the full resolved snapshot in Story 1.5.
