# Two-Layer TTS Audio Cache Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development after user approval. Every subagent must use `gpt-6-luna` with high reasoning, isolated scope, and non-overlapping file ownership. Use `wait_agent` after delegation; do not poll agent status. Do not commit or push.

**Goal:** Reuse complete fixed speech across VR sessions through a persistent local cache and private Firebase Storage, with immediate LiveKit streaming on misses and teacher preview/rejection through the same Python TTS implementation.

**Architecture:** The backend retains immutable session voice settings, authenticates cache reads using the existing VR room token, and publishes immutable Storage artifacts. VR owns fixed utterance selection, source resolution and playback lifecycle; the agent owns evaluation, automatic reminder scheduling, streaming and bounded capture. A separate roomless Python service imports the same provider implementation for explicit web previews.

**Tech Stack:** Unity C#, LiveKit RTC/DataPackets, Python 3.10–3.14, locked `livekit-agents` 1.7.1 / `livekit-plugins-google` 1.6.8 / `google-cloud-texttospeech` 2.37.0, installed aiohttp, Next.js 16 / TypeScript, Firebase Admin, Firestore and Storage, pytest and Vitest.

**Spec:** `docs/superpowers/specs/2026-10-13-tts-audio-cache-design.md` (English text is the contract).

**Approval status:** Approved for implementation by the user's “start” on 2026-10-13. The latest profile-selection decision supersedes mandatory single-NPC preview context: use one editor-wide selector from existing shared Firestore profiles for explicit per-row Preview. Live invocations remain separate while preview synthesis and agent publication coalesce. No commit, push, cloud deployment or Unity execution is authorized.

**Implementation checkpoint (2026-10-14):** Product changes and independent reviews are recorded in `docs/superpowers/reviews/2026-10-14-tts-audio-cache-implementation-checks.md` and the SDD checkpoint. Focused Python/web checks and standalone pure C# checks passed. The original task checklists describe the approved work; they are not evidence that Unity compilation, EditMode tests, deployment or headset checks have run. Use the actual consolidated Unity test filters in Task 9 and the handoff.

## Global constraints

- Work in `D:/Lab/VR-Autism` and `D:/Lab/VRA-web`, not the attached clean `0310` worktree. Preserve the pre-existing specification edit and unrelated TMP fallback-font asset edit.
- Product edits stay under `Assets/Project/Scripts/`, `LiveKitAgent/src/`, `LiveKitAgent/tests/`, and `D:/Lab/VRA-web/src/`. This explicitly requested specification/plan update is documentation work. No vendor, installed SDK, package, build-output, Desktop or system-temp edits.
- No Unity launch, batch mode, full Unity suite, commits, pushes, cloud deployment or production configuration mutation. Deployment commands below are a handoff, not execution authorization.
- Run GitNexus upstream impact before every existing symbol edit; report direct callers, processes and risk, and warn before HIGH/CRITICAL edits. Run change detection and `git diff --check` before handoff. Reindex after major files are created with `--index-only` to avoid changing AGENTS/CLAUDE files.
- WAV PCM16 little endian, mono, 24,000 Hz. Capture at most 4,194,304 PCM bytes. Queue at most eight completed buffers; one upload worker per process; shutdown drain five seconds.
- VR disk cap is 256,000,000 bytes including audio, metadata, temporary downloads and the rebuildable index. Protect active readers/players/writers. Cloud audio is retained in this release.
- No profile versions, per-child voice settings, extra synthesis for caching, pending/ready Firestore catalog, periodic cache polling, distributed synthesis lock, audio DataPackets, second microphone capture or new stream-start/hint timeout.
- Voice Quest completion remains matched speech, the existing interaction quest, and teacher skip. Hints/reminders never create a match. Arbitrary conversational replies remain uncached.

## Inspection evidence and deployment prerequisites

- `agent_v2.py` eagerly pre-synthesizes quest phrases and hints and stores job-local frame lists. Script and hint code await `session.say()` before storing handles. Existing `AsyncMock` tests hide the synchronous-awaitable SDK behavior. Completed handles require explicit `exception()` inspection.
- The SDK's default TTS node strips segment-final evidence. API 499 can end the outer stream gracefully; cache completeness must independently require normal text input completion, valid PCM and final evidence for every segment.
- Current profiles are teacher/Aoede and peer/Puck at rate 1.0. Gender/pitch are not effective options. Option update errors and unknown mappings currently fall back silently; tagged cache requests must instead fail.
- Unity's blocking dialogue timeout starts after `SpeakAsync`, which currently returns immediately. Preserve that scheduling interface so resolution time falls inside the existing default 30-second wait. Preserve nonblocking return behavior.
- `LiveKitNpcAudioRouter` routes participant-wide audio tracks, not utterance-labelled PCM. Its class-level GitNexus result is partial; a zero count is not proof of no callers. Source inspection identifies `LiveKitService`, dialogue transport and voice transport as relevant consumers.
- Launch currently happens in client `startLessonOnDevice`: register presence, obtain room token, update RTDB. No retained voice settings, device Firebase-auth flow, backend generation service, or agent restart metadata retrieval exists.
- The Dockerfile starts legacy `src/agent.py`. Preserve it. The eventual V2 deployment must explicitly run `uv run src/agent_v2.py start`; roomless preview runs `uv run python src/tts_preview_service_v2.py`. Neither command changes a running deployment in this task.
- The unused `/api/tts` route is a separate legacy Translate/MP3 path. Leave it intact; new preview routes must not call it.
- GitNexus indexes were refreshed on 2026-10-13. Web incremental refresh hit an FTS inconsistency; an index-only forced rebuild succeeded. Group registry sync completed; it has no inferred cross-links for these new contracts, so fixture and source checks remain necessary.
- Planning impact: `_handle_speak_script_v2` LOW, one direct caller `_process_v2_packet`, three upstream symbols, no indexed process; `startLessonOnDevice` LOW, direct `handleStart`, one launch process. Router, hint and editor graph results are partial; analyze concrete methods again before implementation.

## Review focus

1. Reject/upload races: a completed pre-rejection buffer can never be relabelled as a replacement. Task 2/3 tests use barriers on both sides of publication.
2. RTC bytes arriving after skip: detached routing and current-source ownership must stop audible overlap, not merely discard completion packets. Task 6 tests and headset checks cover this.
3. Reconnect/restart: shared setting edits must not enter the retained session; a new agent must reconcile current lifecycle before reminders resume. Task 4/7 tests cover this.
4. Disk pressure/corruption: valid RAM audio remains playable when persistence fails; partial files, symlinks/path escapes and invalid index entries never become hits. Task 5 tests cover this.
5. Global preview selection and edited rows: old responses/receipts must not survive a profile change across any row, authoritative returned settings must be displayed before playback, and selection/scrolling/preview/reject never saves edits. Task 8 tests cover this.

## Shared contracts: freeze before either producer is implemented

### Configuration and input identity

`SessionTtsConfigV1 = {schema_version:1, remote_cache_wait_ms:int, profiles:ProfileV1[], npc_profiles:NpcProfileV1[]}`. Profile fields are `profile_id`, `provider`, `model`, `language`, `voice_name`, `speaking_rate`, `provider_encoding`, `sample_rate`, `channels`, `audio_format`. NPC mappings contain `npc_binding_id` and `profile_id`. Profile IDs must match `[a-z0-9][a-z0-9-]{0,63}`; no duplicate profile or NPC identifiers. Defaults:

```json
{"schema_version":1,"remote_cache_wait_ms":1500,"profiles":[{"profile_id":"teacher-npc","provider":"google-cloud-tts","model":"chirp_3","language":"vi-VN","voice_name":"vi-VN-Chirp3-HD-Aoede","speaking_rate":1,"provider_encoding":"OGG_OPUS","sample_rate":24000,"channels":1,"audio_format":"wav-pcm16-v1"},{"profile_id":"peer-npc","provider":"google-cloud-tts","model":"chirp_3","language":"vi-VN","voice_name":"vi-VN-Chirp3-HD-Puck","speaking_rate":1,"provider_encoding":"OGG_OPUS","sample_rate":24000,"channels":1,"audio_format":"wav-pcm16-v1"}],"npc_profiles":[{"npc_binding_id":"teacher-npc","profile_id":"teacher-npc"},{"npc_binding_id":"peer-npc","profile_id":"peer-npc"}]}
```

Keep the current OGG_OPUS provider transport and capture decoded PCM frames. Use the provider model identifier `chirp_3` in the factory; do not send it as a Google VoiceSelectionParams model field for Chirp 3. Do not infer voice gender or publish unapplied pitch/volume-gain settings; the inspected streaming request does not apply volume_gain_db. Validate supported provider/model/encoding/format and rate 0.25–2 with at most six fractional decimal places; reject nonfinite, bool and excess precision instead of rounding effective configuration. Do not normalize spoken text at key time. Existing phrase resolution/personalization happens first.

Canonical identity is SHA-256 over ordered strings, each encoded as four-byte unsigned big-endian UTF-8 byte length followed by its UTF-8 bytes:

`tts-cache-key-v1, profile_id, provider, model, language, voice_name, canonical_rate, provider_encoding, canonical_sample_rate, canonical_channels, audio_format, final_text`.

Canonical decimal has no exponent, plus sign, trailing fractional zero or negative zero; integer one and numeric 1.0 both encode `1`. Hash output is 64 lowercase hex characters. Do not include session, child, rejection epoch or edit history. For the teacher default and exact text `Xin chào.`, rate 1 key is `3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8`; rate 0.9 key is `892436b94b4bfbc26c3d5209aa12c4ab622ed00ed0fe75cb6729e19dd4e958d8`. Rate 1 again must return the first key.

Shared fixtures are byte-identical JSON at `LiveKitAgent/tests/fixtures/tts-cache-v1.json`, Unity `Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Tests/Editor/Fixtures/tts-cache-v1.json`, and web `src/lib/__fixtures__/tts-cache-v1.json`. Include Unicode combining forms as distinct text, emoji, escaped JSON characters, punctuation, repeated spaces, 1/1.0, 0.9, separate profiles, corrupt WAV, and stale artifact/event identities. Fixture parity is checked by SHA-256; no consumer calculates expected values using its own implementation.

### Artifact validity and publication

`ArtifactV1 = {profile_id,key,rejection_epoch,object_path,generation,sha256,byte_size,sample_count,duration_ms,format}`. Generation is a decimal string, never a JS number. WAV is exactly canonical RIFF/WAVE with one 16-byte PCM fmt chunk and one data chunk, positive sample count, even PCM byte length, exact total length, mono/16-bit/24000 Hz; checksum covers the full file. Max object size is 4,194,348 bytes.

Storage path: `tts-cache/v1/<profile_id>/<key>/r<rejection_epoch>.wav`. A rejection epoch is artifact metadata, not key/profile version. Missing Firestore rejection doc means zero. Only rejection creates `tts_cache_rejections/<profile_id>__<key>` with `{schema_version:1,current_epoch,updated_at}` plus immutable rejected receipt records in `rejected/r<epoch>`.

Publication request consumes a complete WAV, immutable profile/key/text/config, and epoch captured before synthesis. Backend recomputes key, parses WAV, verifies checksum and current epoch, then writes with generation-match-zero. If a valid object already exists, return that winner. Recheck epoch after upload; if changed, return `STALE_ARTIFACT`, never emit ready. A stale upload may leave ignored old-epoch bytes but cannot occupy a newer path. Never promote a buffer by changing its epoch, overwrite an invalid winner, or automatically synthesize again after a write failure. Invalid existing objects return `INVALID_ARTIFACT`; maintenance is separate.

`CacheRepositoryAdapter.readObject` returns only the immutable generation, actual Storage byte size and custom metadata. It must never download WAV bytes for lookup, launch reconciliation, publication readback/winner inspection or receipt rejection. Validate the exact metadata fields, canonical size/sample-count/duration relationships, actual object size and rejection epoch. Full WAV/header/checksum validation stays at publication input and on VR after downloading the generation-bound URL. This keeps runtime cache lookup independent of an extra backend audio transfer and leaves the HTTP artifact contract unchanged.

Reject verifies the listened artifact's backend-signed receipt and immutable Storage metadata, then transactionally checks current epoch before incrementing it. `rejected/r<epoch>` records artifact generation, checksum, actor and timestamp. Identical repeated receipt is `already_rejected`; any different stale artifact/epoch returns HTTP 409 `STALE_ARTIFACT`. Keep bytes in Storage. No rejection request accepts a caller-supplied object path as authority.

### DataPackets and ordering

Leave existing version-2 voice/dialogue/remote topics and quest completion packets intact. New cache-aware control uses reliable topic `lesson-graph-v2.audio-cache`, `contract_version:1`. Cache-aware sessions use the new fixed-audio handlers; legacy sessions retain old V2 selection/playback behavior. Do not silently send an expanded V2 packet to old exact-key validators.

`InvocationV1` contains required nonblank `session_id`, `run_id`, `activation_id`, `invocation_id`, `npc_binding_id`, and `kind`, where kind is `dialogue|opening|manual_hint|automatic_reminder`. Reuse dialogue sequence ID, manual command ID and agent reminder command ID as invocation ID; opening uses activation ID with kind `opening`. Scope is the full tuple, never key alone. `owner_revision` is a nonnegative, session-monotonic VR ownership counter. Agent packets also carry `agent_instance_id` from that agent process's boot UUID.

Events and exact additional fields:

- `FIXED_AUDIO_STREAM_REQUEST`: InvocationV1, `owner_revision`, exact nonblank `text` (1–500 UTF-16 code units, matching C#/TypeScript string length), `profile_id`, `key`, `rejection_epoch`. Preview and roomless request validation use the same text bound. Agent verifies session snapshot and recomputed key before a synchronous `say` call. No synthesis/file lookup before live streaming.
- `FIXED_AUDIO_CANCEL`: InvocationV1, `owner_revision`, `reason` (`skip|scope_changed|session_ended|user_speaking|superseded|reconfigured`). Cancels only its owned preparation/handle. Late cancellation cannot interrupt a newer handle.
- `FIXED_AUDIO_ACTIVITY`: InvocationV1, `owner_revision`, `activity_sequence` (monotonic per invocation), `phase` (`preparing|local_selected|stream_selected|playing|terminal`), `source` (`none|local|stream`), `status` (`PENDING|SUCCESS|FAILED|CANCELLED|SKIPPED`), `reason` (string, empty for normal success). Terminal is immutable. This is lifecycle, not a first-frame acknowledgment.
- `FIXED_AUDIO_DONE`: InvocationV1, `owner_revision`, `agent_instance_id`, `status` (`SUCCESS|FAILED|CANCELLED`), `reason`. Applies only to the current selected stream. Script transport maps it to existing dialogue signals.
- `FIXED_AUDIO_REMINDER_REQUEST`: InvocationV1 of kind `automatic_reminder`, `agent_instance_id`, exact selected `text`, `profile_id`, `key`. VR allocates owner revision and resolves it once. No parallel direct agent play.
- `FIXED_AUDIO_REMINDER_CANCEL`: same reminder identity, `agent_instance_id`, `reason`. User speech or activation cancellation stops VR preparation/local audio and the correlated agent handle.
- `AUDIO_CACHE_READY`: `session_id`, `agent_instance_id`, ArtifactV1. No invocation completion semantics, no audio bytes. Ignore known rejected epochs; resolve through backend before background admission.
- `FIXED_AUDIO_HELLO`: `session_id`, `agent_instance_id`. Agent announces its boot UUID after startup/reconnect without inventing a VR run ID. VR authenticates the current server-role agent participant before accepting it.
- `FIXED_AUDIO_RECONCILE`: `session_id`, `run_id`, `agent_instance_id`, `active_activation_ids` (unique strings), `current_activity` (null or complete latest activity), and optional `completed_opening_activation_ids` (at most 1024 unique nonblank IDs, a subset of active IDs; missing means empty). VR answers the authenticated HELLO with its actual run ID and current ownership state, not a new manual hint or opening. Retain successful opening proof separately from latest activity, because a subsequent hint must not erase the evidence required by a replacement agent. Apply proof when the matching evaluation activation exists, regardless of cross-topic arrival order; never replay audio. The agent latches that run only from the paired VR participant before accepting fixed requests or scheduling automatic reminders. Cached manual playback does not wait for this handshake. Retired run IDs cannot regain ownership through delayed reconciliation.

Example stream request:

```json
{"contract_version":1,"event":"FIXED_AUDIO_STREAM_REQUEST","session_id":"session-1","run_id":"run-1","activation_id":"activation-1","invocation_id":"hint-command-1","npc_binding_id":"teacher-npc","kind":"manual_hint","owner_revision":3,"text":"Xin chào.","profile_id":"teacher-npc","key":"3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8","rejection_epoch":0}
```

Example local completion:

```json
{"contract_version":1,"event":"FIXED_AUDIO_ACTIVITY","session_id":"session-1","run_id":"run-1","activation_id":"activation-1","invocation_id":"reminder-1","npc_binding_id":"teacher-npc","kind":"automatic_reminder","owner_revision":4,"activity_sequence":3,"phase":"terminal","source":"local","status":"SUCCESS","reason":""}
```

Validators reject unknown fields/events, wrong types/bools-as-ints, unsupported versions, unsafe profile/path components, inconsistent kind/identity, invalid key/config and oversized payloads (16 KiB maximum). Authenticate packet sender as the paired VR participant or current agent participant, as appropriate. Keep current-activation and processed invocation tombstones; cap inactive tombstones at 1024, but never evict the current owner's terminal state. Sender instance and run checks reject older sessions/agents. Web mirrors schema and reminder evidence even though it does not select VR sources.

Unity's installed SDK exposes no public participant kind. An application-owned adapter reads the SDK's internal `ParticipantInfo` and requires the server-issued `ParticipantKind.Agent`, preserving that field for IL2CPP through an application-owned `link.xml`. Fail closed if role evidence is unavailable; identity prefixes or participant attributes alone are insufficient. Retire old boot IDs, require current participant presence, and resolve overlapping restart participants using server join time or fail closed until unambiguous. This disables automatic reminders when authentication is unavailable while preserving independent local manual cache playback. Never edit the vendor SDK.

A local dialogue success maps to Done/NodeStatus.Success; failure maps Failed; cancel/skip maps Cancelled/NodeStatus.Skipped; the existing node timeout stays Timeout. If agent is known unavailable and no cache is obtainable, terminate dialogue as SKIPPED with `AUDIO_UNAVAILABLE`; no unresolved wait. If agent appears available but hangs, retain existing dialogue timeout. Manual hint failure does not fail/complete the voice quest. Successful automatic reminder completion causes the agent to emit existing `ON_REMINDER` accepted evidence exactly once; failed/canceled reminders emit none.

### HTTP/session contract

- `POST /api/tts-cache/session/launch`: active staff cookie, child assignment and current paired host checks; `{request_id,pin,lesson_id,scene_name}`. Register existing web presence first. Server snapshots shared config in Firestore transaction before updating pairing launch fields. A retry with the same request ID retains the same session/config. Return `{session_id,livekit_token,config}`. The cache-aware launch/renew token signs participant metadata `{"tts_cache_schema_version":1}`. Agent opt-in requires this server-issued capability and exact `vr_<retained pin>` identity; legacy metadata without capability keeps the existing path without a cache REST fetch. A capable session never falls back on missing credentials/snapshot. Preserve failure rollback so VR never launches without a snapshot.
- `GET /api/tts-cache/session?session_id=...`: VR JWT bearer or trusted producer secret; return the retained full config and session scope. Trusted producer retrieval additionally checks that the session still owns its pairing. Agent uses `ctx.room.name` to retrieve the snapshot on every startup, with a 5-second startup fetch budget; a failed/missing cache-aware snapshot does not substitute latest settings.
- `POST /api/tts-cache/session/renew`: active authorized staff cookie and same active session/pair checks; issue another existing four-hour VR room token, with unchanged snapshot. No unauthenticated renewal.
- `POST /api/tts-cache/reconcile`: VR bearer; `{session_id,artifacts:ArtifactV1[]}` (at most 128). Return exactly `{results:[{profile_id,key,rejection_epoch,valid,current_epoch}]}` with one result per submitted artifact. Match full submitted identity, not an unbound positional boolean. Run all launch inventory batches before accepting persistent hits. Failed/unverified entries remain misses; streaming remains available. No cursor or fleet-wide polling.
- `POST /api/tts-cache/lookup`: VR bearer; `{session_id,profile_id,key}`. Authorized session may read the shared namespace regardless of child/lesson ACL. Return `{artifact,read_url,expires_at}` or `{miss:true,rejection_epoch}`. Recheck epoch after reading object metadata. URL lifetime ten minutes, generation-pinned GET only, `Cache-Control: private,no-store` for responses.
- `POST /api/tts-cache/publish`: trusted service secret over HTTPS and raw `audio/wav` body capped at 4,194,348 bytes. `X-TTS-Cache-Profile` carries base64url UTF-8 JSON of the full frozen effective profile; `X-TTS-Cache-Text` carries base64url UTF-8 exact final text; `X-TTS-Cache-Key` and `X-TTS-Cache-Rejection-Epoch` carry the key and captured decimal epoch. Metadata is capped at 64 KiB. Recompute identity from the supplied immutable profile instead of resolving latest shared settings. Returns winner ArtifactV1 or typed failure. No browser/VR write permission. The backend alone uses Firebase Admin Storage.
- `GET /api/tts-cache/profiles?child_id=...`: staff cookie and existing assigned-child authorization. Return `PreviewProfileListV1` from the existing profiles in Firestore `tts_configuration/shared`, without NPC/lesson filtering, profile seeding, audio lookup/generation, or writes. Empty configuration returns an empty list; missing/unreadable configuration returns 503 `PROFILE_LIST_UNAVAILABLE`. Neither outcome selects an invented default.
- `POST /api/tts-cache/preview`: staff cookie and existing phrase-editor child authorization; `PreviewRequestV1 = {child_id,lesson_id,phrase_scope,phrase_binding_id,profile_id,text}`. `phrase_scope` is `quest|general`; `phrase_binding_id` is that quest's binding ID for quest phrases and null for general phrases. Lesson/binding fields retain phrase-editor authorization/context validation only; they never constrain profile choice or imply an NPC association. `profile_id` is required and independently identifies an existing shared profile. Resolve its effective settings from Firestore once per request before audio lookup/generation; reject unknown/removed IDs with 404 `PROFILE_NOT_FOUND`, missing selection with 400 `PROFILE_REQUIRED`, and known unsupported settings with 400 `PROFILE_UNSUPPORTED`. Arbitrary client voice settings or NPC/context overrides are rejected by exact DTO validation. Draft text may be previewed but is never saved. On hit return `PreviewResultV1` with read access and a signed listened-artifact receipt. On miss call the shared roomless service and publish its complete output through the same repository function. Never seed or create a profile from this endpoint.
- `POST /api/tts-cache/reject`: same staff/child authorization, same-origin request checks; `{preview_receipt}` only. Receipt is signed server-side, bound to actor/child/artifact/config/text fingerprint, valid for 30 minutes; UI enables reject only for the artifact whose audio has actually begun playback. Return `{result:"rejected"|"already_rejected"}`. A stale request cannot reject a replacement.
- `POST /api/tts-cache/preview/renew`: same staff/assigned-child and same-origin authorization; exact `{preview_receipt}` request. Verify the signed actor/child/artifact receipt and require the current lookup to match its full immutable artifact. Return `{schema_version:1,artifact,read_url,expires_at}` for that artifact only; return 409 on replacement/rejection. Never synthesize. Browser retries read access at most once for an expired URL/read-access failure, under the existing global selection/request/row/text guards; playback permission denial does not renew.
- Python internal `POST /generate`: server-to-server secret; complete profile, exact final text/key and rejection epoch; returns a complete bounded WAV. Validate before provider call. No room/session creation. Use aiohttp app with 64 KiB JSON request cap, a process-local same-key generation future, four distinct preview keys concurrently, and reject excess work with 429. Web request deadline 120 seconds; expiry/disconnect cancels generation when no other subscriber remains. This is web preview waiting, not a VR stream-start timeout.

HTTP errors: 400 malformed/config/key, 401 missing/expired credential, 403 wrong device/session/child/role, 409 stale receipt/epoch, 413 oversize, 429 bounded preview capacity/rate limit, 502 provider/final-evidence failure, 503 service/snapshot unavailable, 504 preview generation deadline. Download errors and 404/miss select streaming once. Do not expose raw provider exceptions, secrets or signed receipts in logs.

### Shared web-preview profile and selection contract

These DTOs live in `src/types/tts-cache.ts` under orchestrator ownership. `PreviewProfileOptionV1 = {profile_id:string,label:string,effective_profile:TtsProfileV1}`; `PreviewProfileListV1 = {schema_version:1,profiles:PreviewProfileOptionV1[]}`. Label derives from the existing profile ID; display the effective voice/rate separately, without inventing a phrase's NPC. Return every valid configured profile even when `npc_profiles` contains no mapping to it. Invalid shared profile data fails the read with `PROFILE_LIST_UNAVAILABLE`; listing never repairs or seeds it.

`PreviewRequestV1` has the exact fields above. `PreviewResultV1 = {schema_version:1,profile_id:string,effective_profile:TtsProfileV1,artifact:ArtifactV1,read_url:string,expires_at:string,preview_receipt:string}`. Backend `listPreviewProfiles(childId,claims):Promise<PreviewProfileListV1>` and `resolvePreviewProfile(profileId):Promise<TtsProfileV1>` read existing configuration. `previewPhrase(input:PreviewRequestV1,claims):Promise<PreviewResultV1>` resolves and freezes settings once; key, generation, artifact and receipt use that copy even if shared settings change during generation. The browser sends no effective settings and no profile revision.

`VoicePhraseEditorV2` owns one `selectedProfileId` (initially null), one global selection generation, one latest request ID, one active browser Audio instance, and listened receipt state. Rows consume that selection and initiate work only through an explicit Preview click. Profile listing may occur on editor open/reload; selection, scrolling, row mounting and phrase edits perform no audio lookup, generation or saves. Preview/Reject remain disabled without a valid selected profile. Known profiles are selectable independently of quest/general phrase NPC mappings. No preview-only fields or associations are added to existing phrase DTOs; runtime `npc_profiles` mapping and retained session behavior are unchanged.

Every explicit Preview supersedes the previous pending preview, stops previous browser audio, clears old listened receipts and allocates a new request ID. Guard all responses and delayed audio callbacks by editor selection generation, request ID, child/lesson, stable row ID and exact requested text. A profile change increments the editor generation, aborts pending requests/downloads where possible, stops audio and clears every row's receipt immediately. Changing A to B and back to A must not revive an old A response. Row edits/removal/reorder, editor reload/close and authorization failure likewise invalidate affected work; do not identify a row only by its current array index. Abort is best effort: an old completed artifact may remain cached under its original key, but its response cannot become current UI state.

For settings changed since listing, an otherwise current preview response first updates the selected option and editor-wide voice/rate display to its exact `effective_profile`; only then start playback. If effective settings differ from the displayed copy, increment the global selection generation and invalidate all other pending results/receipts; adopt only that already-validated latest response into the new generation. There is one current request globally, so concurrent old-row results cannot flip the settings display. Profile-list responses also carry editor-generation/list-request guards and cannot overwrite the authoritative settings label of current audio. Await the UI display commit before invoking Audio.play, then recheck the request guard; a profile switch during that commit prevents playback. Never reread current shared settings to relabel audio after it has been generated, or generate again just to reconcile display. Existing live-session snapshots are not modified. A removed/unsupported profile response stops/clears current preview, refreshes the list read-only, and requires an explicit valid selection/Preview; no automatic fallback or retry synthesis.

The returned receipt is only a candidate until that response's matching Audio instance emits `play` for the current guard. Reject is enabled only for its listened row/artifact and submits exactly that receipt. Profile/text/settings changes and newer previews clear it globally; errors or stale callbacks cannot enable Reject. An in-flight rejection already accepted server-side still rejects its original listened artifact; a late response cannot alter the new selection's state or retarget the request. Existing artifact-specific receipt verification, epoch transaction and stale-reject safeguards stay unchanged.

RTDB continues `pairing_codes/<pin>` and `live_sessions/<sessionId>`. Add only `tts_cache_schema_version:1` to launch pairing metadata and clear it with existing disconnect/presence cleanup. Config itself is retained in Firestore and retrieved in full. Do not store profile edits or rejection catalogs in RTDB.

## Task 1: Correct speech handle ownership and outcomes

**Owner:** Python lifecycle worker. **Dependencies:** none; finish and review before capture.

**Modify:** `LiveKitAgent/src/agent_v2.py`, `LiveKitAgent/tests/test_speak_script_v2.py`, `LiveKitAgent/tests/test_verbal_hint_v2.py`, `LiveKitAgent/tests/test_agent_v2.py`.

**Interfaces:** `start_owned_speech(runtime, invocation, text, session) -> SpeechHandle` stores the handle returned by synchronous `session.say` before any await. `finish_owned_speech(handle, is_current) -> SpeechOutcome` distinguishes completion, generation exception and cancellation. Integrate scripts, hints, opening and reminder paths with the corresponding owner slot, without redesigning quest completion.

- [ ] Run impact on each changed handler, reset/cancel helper and runtime method; warn/report results.
- [ ] Add SDK-compatible tests with synchronous `say` returning a real handle or faithful awaitable: block playout, cancel while handle is stored, verify only that handle is interrupted; completed exception returns FAILED; stale finish cannot publish success; opening and reminder cancellation are covered.
- [ ] Run those test cases and confirm they fail for the observed await-before-store/error behavior.
- [ ] Replace await-before-store, inspect `exception()` only after done, handle `CancelledError` distinctly and preserve current activation/command guards. Do not wrap synchronous `say` in AsyncMock.
- [ ] Run `uv run pytest tests/test_speak_script_v2.py tests/test_verbal_hint_v2.py tests/test_agent_v2.py -q` from `LiveKitAgent`; targeted cases and existing relevant behavior must pass.
- [ ] One independent scoped review. Fix material findings and rerun affected tests; no commit.

## Task 2: Freeze coordinated contracts and backend artifact repository

**Owner:** Orchestrator owns all shared contract files; backend worker owns repository implementation after interfaces are frozen. **Dependencies:** Task 1 for lifecycle semantics.

**Create:** Python `src/tts_cache_contract_v1.py`; Unity `Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/TtsCacheContractsV1.cs`, `TtsCacheKeyV1.cs`; web `src/types/tts-cache.ts`, `src/lib/tts-cache-contract.ts`, `src/lib/tts-cache-repository.ts`, `src/lib/tts-cache-auth.ts`; three fixture files listed above.

**Modify:** Python `src/voice_contract_v2.py`; Unity `Questing/Voice/VoiceQuestTransportContracts.cs`, `Runtime/Dialogue/DialogueTransportContracts.cs`; web `src/types/lesson-graph-v2.ts` only where mirrored typed evidence/session capability needs explicit integration. Keep existing exact V2 shapes available.

**Test:** Python `tests/test_tts_cache_contract_v1.py`; Unity `AudioCache/Tests/Editor/TtsCacheKeyV1Tests.cs`, `TtsCacheContractV1Tests.cs`; web `src/lib/tts-cache-contract.test.ts`, `tts-cache-repository.test.ts`, `tts-cache-auth.test.ts`.

**Interfaces:** `compute_key(final_text, profile) -> str`; C# `TtsCacheKeyV1.Compute(string finalText, TtsProfileV1 profile) -> string`; TS `computeTtsCacheKey(text:string, profile:TtsProfileV1):string`. Backend `lookupArtifact(profileId,key):Promise<LookupResult>`, `publishArtifact(input:PublishInput):Promise<ArtifactV1>`, `rejectArtifact(receipt:VerifiedReceipt):Promise<RejectResult>`, `verifyVrCacheSession(request):Promise<SessionScope>`. No producer writes Firestore ready state.

- [ ] Add parity/key/packet validation tests and repository race tests before implementation.
- [ ] Implement canonical identity, complete WAV validator, safe path derivation, Storage immutable publication and rejection transaction. Verify signature/issuer/room/subject/current pairing and constant-time equality with the currently stored pairing VR token, never PIN alone. A generic staff-issued room token must not become a device-cache credential.
- [ ] Exercise concurrent epoch-zero upload, rejection then old upload, rejection between pre/post upload checks, replacement epoch-one upload, stale reject against epoch-zero receipt, exact repeated reject, corrupted existing winner and 64-bit generation string.
- [ ] Add HTTP route files `src/app/api/tts-cache/lookup/route.ts`, `publish/route.ts`, `reconcile/route.ts`; expose no credential to clients. Receipt signing uses existing LiveKit secret with a distinct purpose-derived signing key and a fixed receipt issuer/audience.
- [ ] Run Python contract tests and `npm test -- src/lib/tts-cache-contract.test.ts src/lib/tts-cache-repository.test.ts src/lib/tts-cache-auth.test.ts`. Check all fixture file hashes are equal.
- [ ] One independent review of contract/race/auth behavior, including partial GitNexus limitations.

## Task 3: Shared streaming provider, bounded capture and publication worker

**Owner:** Python capture worker. **Dependencies:** Task 1 and Task 2 contract/repository API.

**Create:** `LiveKitAgent/src/tts_generation_v2.py`, `tts_capture_v2.py`, `tts_cache_client_v1.py`; tests `test_tts_generation_v2.py`, `test_tts_capture_v2.py`, `test_tts_cache_client_v1.py`.

**Modify:** `LiveKitAgent/src/agent_v2.py` (`TeacherAgent.tts_node`, startup/shutdown wiring and fixed speech paths). Preserve `src/agent.py`.

**Interfaces:** `stream_fixed_tts(text:AsyncIterable[str], profile:TtsProfileV1) -> AsyncIterator[SynthesizedAudio]`; `CaptureContext` is frozen final text/config/key/epoch/invocation; `FixedCapture.append(event)` copies at most 4 MiB PCM, `finish(input_complete, stream_complete, expected_segment_count:int) -> CompletedCapture|None`; `PublicationWorker.try_enqueue(CompletedCapture) -> bool`, `drain(timeout_seconds:float=5) -> Awaitable[None]`.

- [ ] Tests pin copied ContextVar ownership, synchronous say/task inheritance and immediate context reset; untagged nodes remain default behavior.
- [ ] Tests require original frame identity/order/userdata and early live frames while publication is blocked. Missing expected segments/finals, 499, swallowed input failure, invalid PCM and stream errors must never publish. Include segment 1 final followed by segment 2 RPC failure 499 before start_segment; observed finals alone must not pass.
- [ ] Share one provider factory/stream function with preview. For tagged requests apply immutable effective settings at stream creation; fail on unknown profile/application mismatch. Do not mutate a shared provider while another stream snapshots options.
- [ ] Tee events in `TeacherAgent.tts_node`, yield original frames, record finals by segment and forward text normally. Count nonempty submitted logical segments/flushes before each provider operation, independently of output events. Tagged fixed generation submits one complete text segment followed by end_input with no intermediate flush, so expects exactly one final segment; input forwarded must equal the declared final text. Detect retry/request-ID changes and abandon capture if a complete single synthesis cannot be proven; keep live SDK forwarding unchanged. Do not await upload, disk, lookup or queue capacity in the frame loop.
- [ ] Remove cache-aware fixed-path `_synthesize_phrases` waits. Keep legacy code paths for legacy sessions; no new synthesize call just to create WAV.
- [ ] Use eight-buffer nonblocking queue, one worker, RAM WAV packaging, and `asyncio.to_thread` for synchronous trusted publication HTTP. Overflow or queue full drops cache only. Five-second shutdown drain does not keep incomplete synthesis alive.
- [ ] Implement the approved AC-26 scope: agent has one audible fixed owner per job; overlap is resolved by owner cancellation before starting a new handle. Duplicate invocation deliveries reuse the existing owner/outcome. Coalesce same-key completed publication jobs. Distinct live invocations retain separate synthesis/playback/cancellation/terminal ownership; they do not share a SpeechHandle. Roomless generation coalesces concurrent same-key callers in Task 8; never attach a live invocation to a suffix or make its first stream wait for an unrelated complete synthesis. Test simultaneous cancellation/replacement so old generation ends before a new owner starts and neither invocation inherits the other's terminal status.
- [ ] Run `uv run pytest tests/test_tts_generation_v2.py tests/test_tts_capture_v2.py tests/test_tts_cache_client_v1.py tests/test_speak_script_v2.py -q`. Verify AC-03–09 and incomplete/cancellation cases.
- [ ] One independent review; instrument capture copy time and first-frame timing for headset measurement without logging phrase/provider secrets.

## Task 4: Session snapshot, backend launch and agent restart

**Owner:** Backend session worker; orchestrator owns contract extensions. **Dependencies:** Tasks 2/3.

**Create web:** `src/lib/tts-session.ts`, `tts-session.test.ts`, routes `src/app/api/tts-cache/session/route.ts`, `session/launch/route.ts`, `session/renew/route.ts` with focused route tests colocated.

**Modify web:** `src/lib/firebase/rtdb.ts`, `rtdb.test.ts`, `src/app/dashboard/expert/lessons/_components/StartLessonButton.tsx` only for launch response/error integration, and cleanup in `src/lib/livekit-access.ts` for the new pairing capability field.

**Modify Unity:** `Assets/Project/Scripts/Core/Manager/SessionContext.cs`, `Gameplay/WaitingArea/SceneMenuController.cs`, `Gameplay/LessonGraphV2/Runtime/LessonLaunchContractsV2.cs`, `LessonLaunchCoordinatorV2.cs`, `LessonSessionContextV2.cs`, and `Cloud/RTDB/PairingManager.cs` only to recognize cache-aware launch metadata without breaking the existing event signature.

**Modify Python:** `src/agent_v2.py` startup to fetch retained snapshot by room; `src/tts_cache_client_v1.py` session retrieval. Tests `test_tts_session_v1.py` plus existing launch tests.

**Interfaces:** `prepareTtsSessionLaunch(input,claims):Promise<LaunchResponse>`, `getRetainedTtsSession(sessionId):Promise<SessionTtsConfigV1>`; Unity immutable `SessionTtsSnapshotV1` attached to accepted launch/session, not global mutable settings.

- [ ] Tests: persistence precedes RTDB launch; idempotent request ID retains snapshot; authorization failure writes nothing; failed RTDB launch does not activate an orphan session; edits during session/restart retain original settings; next session takes edits; finalizeSession cannot overwrite subdocument.
- [ ] Seed absent shared configuration transactionally with inspected defaults; no configuration-management UI or profile version. Snapshot malformed settings fail launch rather than silently use changed defaults.
- [ ] Reuse current room JWT and pairing ownership for VR reads. Backend availability is independent of agent process/room participant presence. Renew only same active session under existing staff authority.
- [ ] Preserve registerWebPresence ordering. Snapshot first; RTDB launch update second. Mark `tts_cache_schema_version:1` and clear it on disconnect and onDisconnect presence reset.
- [ ] Agent restart loads exact retained snapshot; fetch timeout is startup/config retrieval only, not a new per-utterance stream timer. A cache-aware missing snapshot prevents fixed speech/reminders rather than inventing settings. Legacy sessions without capability keep legacy behavior.
- [ ] Run `npm test -- src/lib/tts-session.test.ts src/lib/firebase/rtdb.test.ts` and Python `test_tts_session_v1.py`; provide Unity `LessonLaunchCoordinatorV2Tests` filter.
- [ ] One independent review of authorization, snapshot ordering and legacy launch preservation.

## Task 5: VR persistent repository and bounded resolver

**Owner:** Unity cache worker. **Dependencies:** Tasks 2/4.

**Create under `Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/`:** `TtsLocalCacheV1.cs`, `TtsWavValidatorV1.cs`, `TtsCacheBackendClientV1.cs`, `TtsAudioResolverV1.cs`, `Tests/Editor/TtsLocalCacheV1Tests.cs`, `TtsWavValidatorV1Tests.cs`, `TtsAudioResolverV1Tests.cs`, `TtsCacheLaunchReconcileV1Tests.cs`. Create Unity .meta files only beside owned new assets.

**Interfaces:** `TryAcquire(ArtifactIdentity,key,out LocalAudioLease) -> bool`; `Reserve(long bytes) -> Reservation`; `ResolveAsync(FixedUtterance request, CancellationToken) -> Task<ResolvedAudioV1>` with outcome `local_ready|stream_required|cancelled|failed`; `ReconcileLaunchAsync(session,inventory,token)` marks each verified artifact for that launch.

- [ ] Tests first: disk accounting includes index/metadata/temp reservations, LRU tie order deterministic, active leases protected, invalid/missing index rebuilt, corrupt files miss, crash temp removed, failed reservation preserves valid RAM playback, parallel writes remain within cap, cleanup cannot escape owned directory.
- [ ] Use one disk writer, one foreground fetch and idle-only background fetch; cancellation releases reservations/leases. Local metadata includes full ArtifactV1 and last-use sequence. Same-directory atomic rename publishes complete data before index reference; no partially written hit.
- [ ] Download/decode at most 4,194,348 WAV bytes into bounded memory; reject oversize before growing buffers. Validate header, expected size, sample count and full-file checksum. Main-thread AudioClip construction must be ready before remote deadline succeeds.
- [ ] Start one monotonic deadline at local miss. Include queue wait, lookup, URL renewal, download, validation, decode/main-thread scheduling. Default 1500 ms; validated session range 0–10000. On budget/error select stream once; late artifact may persist for a later play but cannot change this invocation.

Networking boundaries: session snapshot fetch and the complete launch-inventory reconciliation each have a 5-second bound. Begin reconciliation at coordinator configuration. If a foreground invocation must wait for still-pending reconciliation, start its one configured deadline at that first network wait and carry the remaining budget into remote lookup/download/decode/readiness; do not grant a second budget after reconciliation. Otherwise start the remote deadline at the local miss. Background READY work runs only while no fixed preparation/playback or unresolved canceled-stream barrier exists, has a 10-second total bound including queue/gate/network/validation/commit, and is canceled by new foreground ownership. Keep at most 64 advisory READY identities in the coordinator and 32 coalesced resolver jobs. Authentication headers go only to same-origin HTTPS backend API requests with redirects disabled; generation-pinned Storage GETs carry no VR bearer.
- [ ] Reconcile all inventory batches before local cache use. Failed batch means its artifacts remain misses. Do not query rejection periodically in active sessions. Cache-ready background work uses authoritative lookup and never starts playback.
- [ ] Supply user filters `TtsLocalCacheV1Tests`, `TtsWavValidatorV1Tests`, `TtsAudioResolverV1Tests`, `TtsCacheLaunchReconcileV1Tests`; perform only non-Unity source/diff checks here.
- [ ] One independent review of boundedness, disk failure and deadline races.

## Task 6: Fixed playback owner and local/RTC isolation

**Owner:** Unity playback worker; owns router/service changes exclusively. **Dependencies:** Task 5 and corrected Python lifecycle.

**Create:** `AudioCache/FixedAudioPlaybackCoordinatorV1.cs`, `LocalTtsPlaybackHandleV1.cs`, `Tests/Editor/FixedAudioPlaybackCoordinatorV1Tests.cs`, `LocalTtsPlaybackHandleV1Tests.cs`.

**Modify:** `Cloud/LiveKit/LiveKitNpcAudioRouter.cs`, `LiveKitService.cs`, `Cloud/LiveKit/INpcAudioRouterV2.cs`; `Runtime/Dialogue/LiveKitDialogueTransportV2.cs`, `Runtime/Dialogue/DialogueTransportContracts.cs` (orchestrator-coordinated), and existing dialogue/router tests. Do not start microphone anywhere new.

**Interfaces:** coordinator `Schedule(FixedUtterance,token)` returns immediately; `Cancel(InvocationIdentity,reason)` stops only matching owner; router `AcquireLocalPlayback(InvocationIdentity,npcBindingId) -> IDisposable` suspends RTC binding and controls the existing NPC AudioSource; `SelectStream(InvocationIdentity,npcBindingId)` explicitly reopens current stream routing.

- [ ] Tests: prior published request is canceled before replacement; local preparation/playback detach RTC streams, stop/clear source and block late track binding; repeated ready/done/cancel cannot affect newer owner; rebind/dispose releases correct lease; one terminal outcome only.
- [ ] Use one fixed playback owner for the participant-wide agent output, even when NPC routes differ. Track route and source separately from key. Preserve AudioSource volume/spatialization/lip-sync routing. Stop local clip and destroy it on main thread after lease release.
- [ ] Detach/dispose RTC AudioStreams during local resolution/playback and after cancellation; do not resume RTC merely because a local clip ended. Require a new explicit current stream selection. Reliable cancellation is published before a new stream request; Python drains/interruption-completes old owned handle before starting the new one, allowing SDK output clearing. Recreate Unity stream routing to discard its buffered data; stale track/reconnect callbacks stay gated by current owner.
- [ ] Suppress agent conversational output while local activity owns the route, using existing agent session interruption/lifecycle state. Keep user microphone/STT available. Restore ordinary replies after the current lifecycle terminal; no cached arbitrary reply path.
- [ ] `SpeakAsync` schedules resolver and returns immediately, preserving the existing dialogue timer and nonblocking semantics. Emit existing dialogue Done/Failed/Cancelled signals for local outcomes, including unavailable skip; no timeout reset.
- [ ] Provide `LiveKitDialogueTransportV2Tests`, `DialogueNodeExecutorTests`, `NpcAudioRouteBindingV2Tests`, and new ownership filters. Headset check must confirm actual buffered RTC audio does not leak when local/stream ownership changes; graph/tests alone cannot establish this.
- [ ] One independent review of lifecycle/source safety. If actual SDK buffering defeats the scoped ownership design, resolve that material fix before claiming source-isolation acceptance.

## Task 7: Manual selection, opening and agent automatic reminders

**Owner:** Unity hint worker and Python reminder worker in separate non-overlapping file sets. Orchestrator alone changes shared DTOs. **Dependencies:** Tasks 3/4/6.

**Unity modify:** `Questing/Voice/LiveKitVoiceQuestTransportV2.cs`, `Questing/Sources/VoiceQuestSourceV2.cs`, `Remote/LiveKitLessonRemoteBridgeV2.cs`, relevant `VoiceQuestSourceV2Tests`, `VoiceQuestTransportV2Tests`, `QuestHintRoutingV2Tests`, `LessonGraphVoiceReminderAuditV2Tests`.

**Python modify:** `src/agent_v2.py`, `voice_quest_runtime_v2.py`, `silence_reminder_v2.py`; tests `test_verbal_hint_v2.py`, `test_voice_quest_runtime_v2.py`, `test_silence_reminder_v2.py`, `test_fixed_audio_lifecycle_v1.py`.

**Interfaces:** VR activation-local `SelectManualHint(commandId) -> FixedUtterance|duplicate`; Python scheduler callback `request_reminder(activationId) -> Awaitable[bool]` awaits correlated terminal lifecycle, not direct speech; `set_external_audio_activity(invocation,phase,sequence)` blocks silence scheduling until current activity terminal.

- [ ] Tests before edits: three hints wrap sequential list; duplicate command does not advance; failed evaluator/absent agent does not block cached manual hints; missing audio skips without match. The agent receives exact VR-selected phrase and makes no second selection.
- [ ] VR handles opening `phrases[0]` once per activation through the same resolver. Cache-aware SET_ACTIVE_QUEST configures evaluation without direct opening play; agent scheduler waits for opening terminal/current activation. Legacy activation behavior remains available for legacy sessions.
- [ ] Agent selects random automatic phrase, sends reminder request, and never simultaneously plays it. VR miss sends exact stream request; hit reports local lifecycle. Preparing/local/stream ownership suppresses reminder scheduling; release resets current activation's silence window.
- [ ] User speech sends correlated reminder cancel and interrupts agent stream. An unavailable/disconnected agent produces no automatic reminders; stale instance requests are ignored. Cached manual playback never waits for lifecycle delivery.
- [ ] After reconnect/restart, reconcile active activation and latest lifecycle before resuming scheduler. Do not replay canceled/completed opening/manual invocation. An in-progress stream on a dead agent fails that invocation; do not silently switch to local or resynthesize it. Existing node timeout still covers a connected but unresponsive agent.
- [ ] On successful current reminder terminal, emit one existing ON_REMINDER accepted packet. Unity bridge records it using existing dedup/evidence behavior; failures/cancel/stale events emit none. Preserve visual reminders and Voice Quest completion rules.
- [ ] Run focused Python lifecycle/reminder tests and web mirrored packet/evidence tests. Supply Unity filters listed above.
- [ ] One independent review of this coherent lifecycle integration, re-review only material fixes.

## Task 8: Roomless preview, listened-artifact rejection and phrase UI

**Owner:** Python preview worker exclusively owns `tts_preview_service_v2.py` and its tests. Web preview worker exclusively owns the web profile-list/preview helpers, controller, routes/tests, editor/row UI and the assigned-child authorization-helper edit/tests listed below. Orchestrator exclusively owns additions to `src/types/tts-cache.ts` and shared fixtures/contracts. Do not give any of these files to two concurrent workers. **Dependencies:** Tasks 2/3/4/5 repository and rejection reconciliation.

**Create Python:** `LiveKitAgent/src/tts_preview_service_v2.py`, `LiveKitAgent/tests/test_tts_preview_service_v2.py`.

**Create web:** `src/lib/tts-preview.ts`, `tts-preview.test.ts`, `src/lib/tts-preview-profiles.ts`, `tts-preview-profiles.test.ts`, `src/lib/tts-preview-controller.ts`, `tts-preview-controller.test.ts`; `src/app/api/tts-cache/profiles/route.ts`, `profiles/route.test.ts`, `preview/route.ts`, `preview/route.test.ts`, `reject/route.ts`, `reject/route.test.ts`; `src/app/dashboard/expert/lessons/_components/PhraseAudioPreviewV1.tsx`.

**Modify web:** `src/actions/voice-phrases.ts` to reuse/export the existing assigned-child authorization helper; `src/actions/voice-phrases.test.ts`; `src/app/dashboard/expert/lessons/_components/VoicePhraseEditorV2.tsx` to own the global selector/controller and attach preview controls without changing Save/Delete behavior. Shared preview DTO additions in `src/types/tts-cache.ts` belong to the orchestrator. No preview-only modifications to `src/types/voice-phrases.ts` or `src/lib/voice-phrases.ts`, and no NPC-context helper/route/test files.

**Interfaces:** `generate_complete(text,profile,key,epoch) -> Awaitable[CompletedCapture]` calls Task 3's same streaming generator. Profile-list/resolve/preview DTOs and functions use the shared web-preview contract above; `rejectPreview(receipt,claims):Promise<RejectResult>` uses Task 2 transaction. `TtsPreviewControllerV1.selectProfile(profileId:string|null):void`, `previewRow(rowId:string,text:string):Promise<void>`, `onAudioPlay(requestId:string):void`, `invalidateRow(rowId:string):void`, and `dispose():void` centralize the global guards. Inject HTTP/audio dependencies and `commitProfileDisplay(profile:TtsProfileV1):Promise<void>` (resolves after the editor commits that display) for deterministic tests with existing Vitest, without new UI-test dependencies.

- [ ] Implement the shared editor-wide selection contract: list existing Firestore profiles independently of NPC mappings, resolve a selected known ID server-side once per request, and use that selected profile for every explicit quest/general row Preview. Keep phrase authorization and runtime NPC mapping unchanged; listing never seeds/writes configuration.
- [ ] Add profile tests: same quest/general phrase under two selected profiles yields separate keys/artifacts; valid unmapped profiles work; missing/unknown/removed/unsupported selections fail before lookup/generation; arbitrary client settings/NPC overrides are rejected; empty/unavailable lists have no fallback. Spy on configuration/phrase writes and provider calls to prove listing/selection cause none.
- [ ] Tests: cached preview makes zero provider calls; miss uses identical provider settings/key/final evidence/WAV as agent and no room/session; two simultaneous preview requests coalesce same-key synthesis and receive complete byte-identical output; cancel one subscriber keeps another legitimate subscriber alive; cancel all aborts provider.
- [ ] Run one process-local generation registry keyed by profile/key/epoch, holding only complete bounded results for followers. Same-key preview callers await complete owner generation (allowed for preview). Four distinct keys maximum; one web miss does not trigger an extra cache-only call. Cross-instance duplicates are tolerated, immutable publication selects winner.
- [ ] Authorize child assignment, same-origin requests and input text limits before any lookup/generation/reject. Rate-limit preview miss generation per actor to ten requests per minute; ordinary hits do not consume provider-generation allowance. Backend private service secret never leaves server. Call provider through shared Python helper, never legacy `/api/tts`.
- [ ] UI uses one editor-owned selector and controller, passing selection/callbacks to every row. Show selected profile/voice/rate without assigning the phrase an NPC; retain explicit Preview/Reject, pending/error states and the distinct Delete phrase control. Use the shared global guards and settings reconciliation before playback. Opening, selecting, scrolling, mounting rows and editing perform no audio lookup/generation/saves. Only the current matching Audio `play` callback admits its listened receipt; URL fetch alone cannot enable Reject.
- [ ] Add controller tests: delayed result/play callback after global A-to-B-to-A selection, old result from another row, edited/removed/reordered row, close/reload, and newer Preview never play or restore a receipt. Profile change stops the sole audio and clears all receipts even if abort fails. Settings changed after listing update the editor-wide label before play, invalidate old-setting results, and never trigger another generation; settings changed during generation keep the response's frozen profile/key/receipt. A late profile-list response cannot relabel current audio; a profile switch while awaiting display commit blocks play. Unknown/removed profile clears selection and requires explicit retry.
- [ ] Add zero-call/write tests for profile listing, selection, scrolling and row mounting through the controller/UI event integration. Verify per-row Preview hit makes no provider call, miss makes one shared generation, and neither saves edits/associations. Keep a focused browser check that selector/scroll/mount effects never call Preview automatically.
- [ ] Expired URL renews access to the same listened/request artifact after auth/epoch validation and under the same global response guard; it never silently previews a replacement. Reject leaves phrase and shared/session profile docs byte-equivalent. Next explicit Preview can create a current-epoch replacement.
- [ ] Tests reject unauthorized actors, changed child assignment, expired/tampered receipts, stale/double reject, old upload after reject, generation 499/missing final evidence, failed upload, response after row edit/profile change, and unchanged phrase/profile/association data. An already-submitted reject carries the original heard receipt even if selection changes; its late response cannot affect the new row/profile state.
- [ ] Run `uv run pytest tests/test_tts_preview_service_v2.py tests/test_tts_generation_v2.py -q` and `npm test -- src/lib/tts-preview.test.ts src/lib/tts-preview-profiles.test.ts src/lib/tts-preview-controller.test.ts src/actions/voice-phrases.test.ts src/lib/voice-phrases.test.ts src/app/api/tts-cache`. The existing voice-phrase library suite is a regression check, not a preview-driven DTO/enrichment edit.
- [ ] One independent review of preview/rejection and launch reconciliation together. Deployment handoff supplies `TTS_CACHE_BACKEND_URL`, `TTS_PREVIEW_SERVICE_URL`, `TTS_CACHE_SERVICE_SECRET`, `PORT` (8080 default), Firebase Admin existing env and Google provider credentials; no runtime secrets printed or cloud services deployed here.

## Task 9: Focused cross-stack validation and handoff

**Owner:** Orchestrator; independent reviewer must not review its own implementation. **Dependencies:** Tasks 1–8.

- [ ] Review scoped diffs against captured initial state; confirm no font/vendor/legacy edits were included or reset.
- [ ] Run focused pytest files introduced/modified above and `uv run ruff check`/`uv run ruff format --check` on those Python source/test files. Use dependency mocks/fake servers; no paid provider call required by default.
- [ ] Run focused Vitest route/repository/session/phrase tests, `npx tsc --noEmit`, and `npx eslint` on touched TS/TSX files. Explain pre-existing unrelated failures separately; do not mask new failures.
- [ ] Verify fixture hash equality, packet JSON examples against all three validators where executable, rate 1/0.9/1 identity, and no stale upload/ready event can admit or play an artifact rejected by the authoritative lookup.
- [ ] Run `git -C D:/Lab/VR-Autism diff --check` and `git -C D:/Lab/VRA-web diff --check`. Refresh changed indexes with `node .gitnexus/run.cjs analyze --index-only` and group sync. Run GitNexus `detect_changes({repo:absoluteRoot,scope:"all"})` for both actual roots; review expected symbols/processes. No commit or push.
- [ ] Record actual changed files and test results. Do not claim Unity compilation, headset audio quality, production deployment or real-provider latency is verified.
- [x] Supply smallest actual Unity filters: `TtsCacheKeyV1Tests|TtsCacheContractV1Tests|TtsUnityCacheIntegrationV1Tests`, then `LiveKitDialogueTransportV2Tests|DialogueNodeExecutorTests`, `VoiceQuestTransportV2Tests|VoiceQuestSourceV2Tests|QuestHintRoutingV2Tests|LessonGraphVoiceReminderAuditV2Tests`, and `NpcAudioRouteBindingV2Tests|LessonLaunchCoordinatorV2Tests`. Cache persistence, WAV, resolver, launch reconciliation, coordinator and local playback cases are consolidated in `TtsUnityCacheIntegrationV1Tests`; execution remains a user check.
- [ ] User headset checks: replay across restart without provider call; remote hit with agent absent; cold miss first live audio while uploads blocked; skip during both sources; immediate new NPC/local phrase after cancellation with no old RTC leak; speak over automatic reminder; absent-agent manual hint; full/corrupt cache and interrupted write; rate 1/0.9/1 across sessions; agent restart after shared edit; preview/reject/replacement then next-session stale-local reconciliation without interrupting active-session playback. Focused web check: profile selection/scrolling is silent and read-only; explicit rows use the global profile; changing it stops old audio/clears Reject; changed server settings display correctly before play.

## Delegation order and ownership

Execute coherent task groups sequentially at contract/risk boundaries: Task 1, Task 2, Task 3, Task 4, Tasks 5–6, Task 7, Task 8, Task 9. Parallelize only independent test/implementation files after interfaces are frozen. `agent_v2.py` has one active editor at a time. Unity router/service has one active editor at a time. Shared C#/Python/TypeScript DTOs and fixture copies belong exclusively to the orchestrator. Return changed files/tests/risks from each worker, then `wait_agent`; never poll status or let two workers edit the same file.

Each coherent change set gets one independent review after focused checks. No repeated broad reviews unless a material fix changes behavior. Reviewers use `gpt-6-luna`, high reasoning, read-only scope, and inspect the scoped diff against this specification/plan. Findings require concrete scenario and location.

## Acceptance mapping

The approved 2026-10-15 same-computer development follow-up is specified in Section 15. Python ownership is limited to `src/tts_cache_client_v1.py`, its existing client test and `tests/test_tts_cache_local_http_v1.py`. Unity ownership is limited to `TtsCacheBackendClientV1.cs`, the new URL policy/Editor menu/focused local-development fixture and `SceneMenuController.cs`; shared payloads and web authorization remain unchanged. Existing production HTTPS rules retain the explicit development exception. Runtime-only setup is documented in `docs/superpowers/guides/2026-10-15-tts-runtime-cache-local.md`. Verify opt-in/host/port/redirect/proxy/bearer/Storage boundaries with focused Python and actual-source C# checks, then leave Unity compilation and `TtsCacheLocalDevelopmentV1Tests` execution to the user. One independent review covers this coherent addition; no commit, push or deployment is part of it.

AC-01/02/10/21–24: Tasks 2/4/5. AC-03–09: Tasks 1/2/3. AC-11–13/25: Tasks 1/6. AC-14/15/19: Tasks 5/7. AC-16–18: Tasks 6/7. AC-20: Task 4. AC-26, as amended by the approved 2026-10-13 choice: Task 3's separate live-owner/dedup/publication invariants and Task 8's same-key roomless synthesis coalescing. AC-27–30/33–38: Tasks 2/8, including the latest profile-selection, zero-generation/write, global stale-result and authoritative-display requirements. AC-31/32: Tasks 4/5/8. LRU/full-cache/temp/index cases: Task 5. Actual RTC buffering, spatial audio/lip sync and Unity compilation remain Task 9 user verification.
