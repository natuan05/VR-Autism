# Two-Layer TTS Audio Cache — Design Specification

Date: 2026-10-13

Status: Core design, Section 14 web-preview extension, and the revised implementation plan are approved. The user authorized implementation with “start” on 2026-10-13. Section 11 records concrete planning decisions from code inspection. No commit, push, cloud deployment or Unity execution is authorized.

Source: [Brainstorming record](2026-10-12-tts-audio-cache-brainstorm.md).

This document is the current design contract. The source records the discussion; it is not an additional implementation contract. Requirements below preserve the approved design, including the web-preview extension, the latest editor-wide profile-selection decision, and the independent-live-invocation concurrency decision approved on 2026-10-13. The latest preview-selection decision supersedes the earlier mandatory single-NPC lesson/quest context. Section 11 records inspected planning decisions and the user's resolved product choices. Implementation and focused non-Unity verification are recorded in the 2026-10-14 implementation handoff; Unity compilation and headset validation remain user checks.

## 1. Purpose and success criteria

Fixed dialogue and Voice Quest phrases are spoken repeatedly, causing unnecessary TTS charges. Reuse the audio across plays and sessions, including lesson phrases and teacher-provided phrases for individual children.

The system must also play cached dialogue and teacher-requested hints when the agent is unavailable. A phrase that cannot be obtained from either cache or the agent may be skipped. Complete audio availability during an agent outage is not a requirement.

Success means a valid cache hit makes no TTS call; a cache miss starts the existing LiveKit streaming path without waiting for file creation or upload; and cache failures do not interrupt that stream.

Teachers must also be able to listen to cached speech before a session and reject poor-quality audio without editing the phrase or voice configuration. A structurally valid file may still be audibly unsatisfactory.

## 2. Scope and boundaries

In scope:

- Fixed dialogue, Voice Quest openings, teacher-requested hints and automatic silence reminders.
- Persistent VR audio files and a shared Firebase Storage audio repository.
- Session-level voice configuration supplied to both VR and the agent.
- Background capture of the same TTS output used for live streaming.
- Playback cancellation, terminal outcomes and silence-reminder coordination across both audio sources.
- Teacher-initiated preview, generation on a preview miss, and audio rejection in the existing web phrase editor; rejected local artifacts are reconciled when a session opens.

Out of scope:

- Caching arbitrary generated conversational replies.
- Voice configuration customized per child, or deduplication across distinct profiles.
- A profile publishing system, separate version history, management UI or rollback workflow.
- Distributed synthesis locks, mandatory lesson preloading or guaranteed offline playback of every phrase.
- A teacher-controlled fallback mode, local silence detection, local VAD or a second microphone capture.
- A new stream-start timeout or first-audio acknowledgment protocol in this release.
- Changes to Voice Quest completion: the existing interaction quest in the same node and teacher skip remain available.

The agent currently runs on LiveKit Cloud. Durable cache behavior must not depend on an agent container or deployment surviving.

## 3. Responsibilities and storage

| Component | Responsibility |
| --- | --- |
| VR client | Select manual hints, resolve cache hits, play local clips, request streaming on a miss, report local playback lifecycle, and reconcile rejected local artifacts at session launch. |
| Python agent | Evaluate speech, schedule automatic reminders, stream fixed phrases on demand, capture complete TTS output, and upload in the background. |
| Backend | Supply session configuration and authorized audio access independently of the agent; coordinate preview generation through shared TTS logic and persist audio rejection information. |
| Firebase Storage | Persist complete audio objects across sessions and agent replacements. |
| Cloud Firestore | Store shared profile configuration, persistent session metadata and the rejection information needed for later session reconciliation. |
| RTDB | Continue existing session-state and low-frequency telemetry responsibilities. |

The Storage bucket is private. A valid session may read the shared TTS cache region; trusted agent/backend generation code publishes audio. The web browser does not receive provider or Storage write credentials. Preview and reject requests must use backend authorization consistent with the existing phrase editor. Do not add per-child, per-lesson, per-profile or per-audio Storage read ACLs. Lesson and child data determine which phrases the application selects, separately from download authorization.

Microphone audio, live TTS streams, speech evaluation and real-time commands remain on LiveKit. Real-time commands and lifecycle events use DataPackets, not audio bytes in packet payloads. Only the existing `LiveKitService` may start headset microphone capture.

Downloading completed cache files for local playback is an intentional exception to the previous rule that all NPC audio arrives through RTC. It does not restore the deprecated HTTP TTS generation path or legacy P2P transport.

## 4. Audio identity and session configuration

### 4.1 Shared configuration

VR already loads the Voice Quest phrase list for the lesson and child for the session. This does not mean it already loads voice settings or all dialogue text. The current default voices and NPC mappings are defined in Python.

Store the small shared profile configuration and NPC mapping in Firestore. At session launch, the backend reads the configuration once, retains that exact copy in the session data, and supplies the full configuration to VR and the agent. Do not send only an ID and require each consumer to resolve a separately versioned profile.

Keep this configuration fixed throughout the session. Changes to the shared configuration apply to subsequent sessions. An agent restart retrieves the configuration retained for the current session, rather than substituting the latest shared settings. VR obtains session configuration independently of agent availability.

No per-utterance profile negotiation is required. The agent must apply the session's effective TTS settings. An unknown profile, incompatible configuration, failed option application or inconsistent key produces an error for that request; it must not silently use another voice and publish audio under the requested key.

### 4.2 Cache key

Each profile has a separate cache namespace. Within that namespace, the key identifies:

- The complete text actually spoken, after personalization or template substitution.
- Effective settings that affect TTS output, including voice, speaking rate, language and provider/model where applicable.
- The technical key/audio-format version.

Do not include a session ID, child ID, edit count or incrementing profile revision. Unused options such as the currently unapplied `pitch` or `gender` must not be treated as effective audio settings. Do not silently remove punctuation, change case or collapse spaces to increase hit rates.

C# and Python must serialize key inputs consistently, including number representation. TypeScript must follow the same rules if it computes keys. Shared test vectors must establish that `1` and `1.0` represent the same speaking rate.

For the same phrase and profile, with other inputs unchanged, rates `1.0`, `0.9`, then `1.0` produce keys A, B, then A. Reuse audio A if it remains in either cache. Changing one profile must not change the keys of other profiles.

The key identifies text/configuration inputs. A rejected artifact is no longer a valid hit even though those inputs remain unchanged. Regeneration may provide a different artifact under the same input key. The plan must distinguish the specific listened-to artifact from that key; this is audio replacement metadata, not profile-version management. The rate-change example reuses the current acceptable artifact for key A, never an artifact already rejected.

Child-specific phrases use the same identity rules. Which child may use a phrase is determined by the lesson/child data, not by an additional child-specific cache namespace.

## 5. Playback resolution

For each fixed utterance:

1. Resolve the final text and session profile, then compute the cache key.
2. A valid local hit plays directly on VR.
3. Otherwise, look up and download the complete object through backend-authorized Storage access. Validate it before local playback and persistent publication.
4. A remote miss, access/download error or exhausted remote-cache budget selects normal agent streaming of the exact requested phrase and profile.
5. The agent streams through LiveKit immediately and captures that same TTS output for background cache publication.
6. If neither cached audio nor agent audio is obtainable, end the utterance with the appropriate non-success outcome. Do not leave blocking dialogue waiting indefinitely for a completion signal.

Once streaming has been selected, a late cache download or cache-ready notification must not switch sources, replay the phrase or change its terminal outcome. A valid late artifact may be retained for a future play. An utterance has one chosen playback source.

Backend cache reads inspect Storage object metadata and rejection state without downloading WAV bytes. This applies to lookup, launch reconciliation, existing publication winners and receipt rejection. Trusted publication validates the complete incoming WAV before writing it immutably with its checksum and format metadata. Backend reads validate that metadata and the actual Storage object size; VR validates the downloaded WAV and checksum before local playback or persistence. Read URLs remain bound to the inspected immutable generation.

There must be no additional synthesis solely to create a cache file. Cache misses must not wait for complete synthesis, WAV packaging, upload or a subsequent file download before live playback.

## 6. Speech types and silence reminders

### 6.1 Dialogue and openings

Fixed dialogue and Voice Quest openings use the resolver in Section 5. Preserve the existing once-per-activation opening behavior. Dialogue can use cached audio even when the agent is unavailable.

Blocking dialogue retains the existing node timeout policy: the current default is 30 seconds and can be configured per node. Do not add a separate stream-start timer or infer utterance start from RTC track subscription or an agent's generic speaking state. Retain nonblocking dialogue behavior rather than adding a completion wait.

The remote-cache budget is distinct from background upload time. Section 11 resolves D2 with a configurable 1500 ms default inside the existing dialogue deadline; the old five-second stream-start proposal is not an approved timeout for this release. With a cold cache and a hung agent, blocking dialogue may wait until its existing node timeout. No separate hint-completion timeout is introduced.

### 6.2 Teacher-requested hints

Move sequential phrase selection from the agent to VR, using the Voice Quest phrase list already loaded for the lesson and child. Each new hint request selects the next phrase, wrapping through the list as today. Re-delivery of the same request must not select or advance again.

VR resolves that phrase through both caches. On a miss, it requests streaming of that exact phrase; the agent must not independently select another hint.

A cached manual hint must not be blocked solely because the agent or speech-evaluation state has failed. Missing audio may cause the hint to be skipped, but must not complete the quest.

### 6.3 Automatic reminders

The agent retains silence scheduling and random phrase selection. When a reminder is due, it requests playback of its chosen phrase through VR's resolver rather than also playing it directly. A cache miss returns a streaming request for the same phrase.

VR reports preparation/playback activity so the agent suppresses automatic reminders while an applicable local utterance is being prepared or played. Completion, failure or cancellation releases that activity for a still-active activation and allows silence timing to resume. These are lifecycle updates, not precise first-frame acknowledgments for live streams.

Preserve automatic-reminder interruption when the user starts speaking, and record accepted reminder evidence only after valid playback completion. Delayed events must not change another activation's timer or telemetry. Exact reconnect and event-reconciliation behavior must be specified in the implementation plan.

There are no automatic reminders while the agent is unavailable. Sending lifecycle updates to the agent must not become a prerequisite for playing an already-cached manual hint.

## 7. Invocation lifecycle and cancellation

Use existing activation and invocation identifiers: dialogue uses its activation/sequence identity; manual hints use their command identity. Reuse existing reminder identities where applicable. A cache key identifies an audio asset, not a particular play: repeated plays share a key but retain distinct invocation identities.

The VR playback owner tracks the active fixed utterance, its identity, key and selected source. Duplicate requests, terminal events and events for stale activations must not trigger another play or complete a newer invocation.

Skip, node change, activation cancellation and session termination must cancel the appropriate preparation and playback work. Stop local clips directly; cancel agent streaming with the correlated existing lifecycle. A canceled utterance must not later report success. Suppress late source activity rather than merely ignoring its completion packet.

Agent lifecycle corrections must precede TTS capture or VR cache integration:

- Obtain and store `SpeechHandle` immediately from the synchronous `session.say(...)` call; only then await playback. The current await-before-store sequence can prevent timely cancellation.
- Interrupt the handle belonging to the canceled invocation.
- Check the installed SDK's error outcome through `handle.exception()` rather than assuming an awaited handle implies successful generation.
- Distinguish normal completion, failure and cancellation; verify validity before playback and again before terminal publication.

Tests must model the real SDK's synchronous call returning an awaitable handle. An `AsyncMock` that immediately returns a handle does not cover the observed cancellation gap.

RTC audio tracks can contain multiple utterances and currently do not label PCM by invocation. Filtering late `SPEAK_SCRIPT_DONE` packets alone does not prevent late audio from overlapping a local clip. The implementation plan must define source ownership and interruption behavior and verify delayed cancellation, RTC buffering and routing changes on the headset.

## 8. Streaming capture and cloud publication

### 8.1 Capture point

Override `TeacherAgent.tts_node` in application code for explicitly tagged fixed utterances. Supply immutable context containing the final text, profile, effective configuration and key. Use an application `ContextVar` around the synchronous `session.say` call, resetting it immediately after obtaining the handle. Do not use a mutable global "current phrase" to associate audio with metadata.

For tagged requests, forward text through the existing Google streaming TTS path, close input normally, read `SynthesizedAudio` events and yield the original frames to LiveKit. Keep segment-final metadata for completeness checks and preserve frame userdata/timing. Untagged conversational replies retain the default TTS node.

Remove fixed-phrase cache-miss pre-synthesis from quest/hint paths where it delays live streaming. Do not make an extra `synthesize()` call to build a file. Apply and capture the effective settings at stream creation; the upload worker must not read mutable settings later.

### 8.2 Resource limits and background work

| Setting | Approved value |
| --- | --- |
| Published audio | WAV, PCM 16-bit, mono, 24 kHz |
| VR persistent-cache cap | 256 MB |
| PCM capture buffer | At most 4 MiB per utterance |
| Completed-buffer upload queue | At most 8 buffers |
| Upload concurrency | One worker per agent process |
| Same-key concurrency | Coalesce roomless preview synthesis and completed agent publication within one process; live invocations retain independent playback/cancellation; tolerate cross-instance duplicates |

Copy valid PCM into the bounded buffer while yielding original frames. Do not await filesystem operations, upload or cache lookup in the frame-forwarding loop. Capture errors or buffer overflow abandon caching for that utterance while allowing live streaming to continue.

Enqueue only complete buffers without blocking. A full queue drops the new cache job, not the live stream. The worker packages WAV and performs blocking Storage work through `asyncio.to_thread`; packaging may use RAM rather than agent-disk temporary files. Cache-write failure must not trigger a second synthesis or change playback outcome.

Approved concurrency scope on 2026-10-13: keep separate live invocations; coalesce preview synthesis and agent publication. Concurrent roomless preview requests for the same profile/key/rejection epoch share one generation and each receive complete audio. The agent coalesces completed same-key publication jobs and duplicate invocation delivery, but distinct live invocations retain their own playback, cancellation and terminal outcomes; do not share their paid synthesis or attach a late request to an incomplete suffix. One active live owner prevents overlapping audible output. Do not introduce a distributed lock or a generation-wait step ahead of the live stream.

Use immutable artifact objects with format, sample count, duration and checksum metadata. The original path example is `tts-cache/v1/<profile-id>/<cache-key>.wav`; the plan must finalize object naming and replacement metadata for Section 14 so that rejecting an artifact does not allow a stale upload to restore it. Publish new objects with `if_generation_match=0`; retain a valid current object instead of overwriting it. An existing but rejected object is not a valid concurrent-writer winner.

### 8.3 Completeness, interruption and shutdown

Publication requires complete text input, valid PCM, a normal stream finish and the expected segment-final evidence. Iterator exhaustion, playback completion or a dialogue-done packet alone is not proof of complete synthesis.

- Cancellation, provider failure or missing final evidence before synthesis completes discards the buffer. Do not keep paid synthesis running after cancellation merely to populate the cache.
- If synthesis has completed before playback is interrupted, the complete audio may still be published. The invocation remains canceled.
- Packaging/upload failure has no effect on live playback and produces no cache-ready event.
- Job shutdown allows a bounded worker drain, with its duration defined in the plan. Abrupt process termination may lose pending buffers; this cache is best effort.

### 8.4 Cache-ready notification

After successful publication, emit the proposed `AUDIO_CACHE_READY` DataPacket containing the key, object path and checksum. A separate profile-version field is unnecessary. Do not send WAV, PCM or base64 in the packet, and do not substitute this event for dialogue completion.

VR may fetch the artifact in the background and publish it locally only after validation. The event must never start playback. Background fetching may wait until current playback ends to avoid competing with real-time work.

A lost notification is harmless: a later lookup can find the object by key. Do not add per-audio Firestore pending/ready records or periodic polling.

The rejection information in Section 14 is distinct from a pending/ready catalog. Ready events and background downloads must follow the selected artifact's validity rules and must not restore a known rejected artifact.

## 9. Local cache validity

Only complete, compatible and validated local files are cache hits. Partial downloads, incompatible formats and invalid checksums are misses. Publication must prevent playback from reading a file that is still being written.

The approved persistent-storage cap is 256 MB. Section 11 resolves D3 with LRU, temporary-space accounting and retained cloud audio. This policy preserves active files and treats cache persistence as best effort rather than a prerequisite for playing valid audio.

Consistency does not require identical file sets on all headsets. It requires consistent audio identity, session settings, complete artifacts and invocation outcomes.

## 10. Contract and delivery requirements

The implementation plan must define concrete event names, required fields, types, schema versions, validators, identity rules, terminal outcomes, error mappings, ordering, cancellation and reconnect reconciliation before handler tasks begin. Include JSON examples and shared fixtures; update affected C#, Python and TypeScript contracts together, including changed session metadata or RTDB keys.

The plan must map local completion, failure and skip into the existing dialogue and quest behavior without making a failed hint complete a quest. It must also map automatic-reminder completion into existing accepted-reminder telemetry.

Delivery order:

1. Correct and test the existing speech lifecycle.
2. Add streaming capture and background Storage publication.
3. Integrate session profiles, VR resolution/local playback and hint lifecycle coordination with synchronized contracts.
4. Add web preview through shared TTS generation logic, backend rejection and session-launch reconciliation as a separate task group within the same plan. Coordinate artifact publication/replacement contracts before implementing either producer.
5. Validate source changes, cancellation, rejected-cache handling and actual audio behavior with focused headset checks.

No full Unity launch, batch-mode run or full-suite test is required by default. Use focused non-Unity checks and Python tests, then provide the relevant Unity test filters and manual checks for the user. Follow GitNexus impact analysis before editing symbols and change detection before finalizing or committing code. Do not edit vendor SDKs or introduce competing microphone capture.

## 11. Planning decisions resolved by code inspection

The orchestrator resolves these details through code inspection and the simplest design consistent with approved behavior, then records concrete choices in the specification and implementation plan. They do not require separate user decisions for routine technical details. Ask the user if a choice changes the agreed scope or behavior; present the completed plan for approval before implementation.

| Gate | Approved boundary | Concrete decision |
| --- | --- | --- |
| D1 — Download access | Private Storage; valid sessions read the shared cache; backend access independent of the agent. | Reuse the existing four-hour VR LiveKit JWT as a bearer credential to the web backend. Verify HS256 signature, issuer, expiration, room-join grant, exact room/session, `vr_<pin>` subject, and current pairing/session ownership. PIN and Unity's launch token are not credentials. Return generation-bound, read-only Storage URLs valid for 10 minutes. Renewal repeats authenticated lookup for the same artifact; one renewal attempt is allowed inside the original remote budget. Staff renew an expired room token for the same active session through an authorized backend route; renewal never rereads shared voice settings. |
| D2 — Remote-cache deadline | One budget for remote lookup and download; errors/exhaustion select streaming; no new stream-start timeout. | `remote_cache_wait_ms` defaults to 1500, configured in the retained session snapshot, with valid integer range 0–10000. Start a monotonic deadline immediately after a local miss, before backend lookup. End only after download, WAV/checksum validation, decode, and main-thread playback readiness. Waiting for a download slot and URL renewal consume the same budget. Zero bypasses foreground remote lookup. The existing dialogue deadline starts when `SpeakAsync` schedules resolution and includes this time; it is never restarted. |
| D3 — Cache lifecycle | VR cap of 256 MB; use only complete valid artifacts. | Adopt the LRU policy below. Use a conservative byte cap of 256,000,000 including WAV, metadata, index, and temporary writes, one disk writer, one foreground download, and one background download only when no foreground playback/preparation is active. Retain all cloud audio, including logically rejected artifacts, in this release. |

### Adopted D3 policy

- Use LRU on VR without per-child, lesson or profile quotas or age-based expiry. Count audio, metadata and temporary downloads within the cap, reserve required space and bound concurrent disk writes.
- Protect files currently being read, played or written. Do not pin an entire lesson or require preload. If space cannot be reserved, skip persistence; valid audio already obtained within bounded RAM may still play without being saved.
- Download under a temporary name, check size/format/checksum, then publish through a same-directory rename. Remove abandoned temporary files at startup. Use a small rebuildable index, not a second database; unverifiable files remain misses. Cleanup is restricted to the application's TTS cache directory.
- Keep cloud audio in this release. Do not add age-based deletion, cloud access counters, reference counting or a garbage-collection job. Removing a phrase from lesson/child data stops its selection but does not delete an audio object potentially shared elsewhere.
- General cloud deletion remains separate administrative maintenance, with checked scope and a generation precondition. Do not add headset delete permissions or an agent delete API. The approved phrase-editor action in Section 14 rejects a specific poor-quality artifact, rather than providing a general cache administration UI. Immediate fleet-wide erasure during active playback is outside this design.

The cache directory is `Application.persistentDataPath/tts-cache/v1`. Published WAV objects contain at most 4,194,304 PCM bytes plus the 44-byte canonical WAV header. RAM downloading is bounded to that object size; malformed or oversized objects are misses. Metadata and index writes reserve their actual encoded byte sizes before publication. A 5-second shutdown drain allows completed upload jobs to finish, then drops queued jobs without keeping incomplete paid synthesis alive. The upload queue remains eight completed buffers and one worker. The inspected Google streaming provider accepts speaking rates from 0.25 through 2.0; reject unsupported values before launching a session or synthesizing.

### Artifact replacement and rejection

Use `tts-cache/v1/<profile-id>/<input-key>/r<rejection-epoch>.wav`. The input key never includes the epoch. An artifact identity contains the input key, rejection epoch, immutable Storage generation (a decimal string), and SHA-256 of the entire WAV. A missing rejection document means epoch zero. Firestore `tts_cache_rejections/<profile-id>__<input-key>` is created only on rejection; it stores the current epoch and rejected artifact identities in a `rejected` subcollection. There are no Firestore pending/ready audio records.

Publication creates the object for the captured epoch with `if_generation_match=0`. Concurrent publishers retain the first valid object. A publisher records its epoch before synthesis starts, never upgrades an old completed buffer to a newer epoch, and checks the current rejection epoch before and after publication. An old upload can leave an ignored old-epoch object but cannot become the current replacement. A ready notification can race with a rejection after the last check; notifications remain advisory and authoritative lookup must prevent admitting a known rejected artifact. Objects are not overwritten or deleted by the headset or agent.

Reject targets the exact listened-to artifact, using a backend-signed preview receipt bound to the authorized teacher, child, profile, input key, epoch, Storage generation, and checksum. A Firestore transaction advances that key's rejection epoch only when the expected epoch is still current and the immutable object matches the receipt. An identical repeat is idempotent; a stale rejection returns a conflict and never advances a replacement's epoch. This counter is artifact rejection metadata, not profile-version management.

At every session launch, VR supplies its local artifact inventory in batches of 128. The backend checks each key against its rejection document and returns validity for that artifact. VR invalidates rejected entries before cache use and admits local hits only after that entry is verified for this launch. Reconciliation failure disables unverified local hits; normal streaming may still run with the retained configuration. Reconnect within the same session does not resynchronize shared settings or push rejection into active playback. Ready packets are hints for a fresh authorized lookup, never authority to restore an artifact.

Snapshot retrieval and complete launch reconciliation are each bounded at five seconds. If foreground resolution must await pending reconciliation, that wait consumes its existing configured cache deadline; later lookup/download/decode do not receive a second budget. Background READY admission waits until fixed playback/preparation and canceled-stream barriers have ended, is bounded at ten seconds including queue/network/validation/commit, and cancels when foreground ownership begins. It never replaces the current utterance's source. Backend API requests use the paired bearer only on the configured HTTPS origin; signed Storage downloads do not receive that bearer.

### Shared configuration and execution environment

Store configuration at Firestore `tts_configuration/shared`; retain the full immutable launch copy at `sessions/<session-id>/runtime/tts_cache`, so existing session finalization cannot overwrite it. Seed missing configuration with the current effective defaults: Google Cloud TTS, `vi-VN`, Chirp 3 HD, 24 kHz mono decoded PCM, rate 1.0; `teacher-npc` uses `vi-VN-Chirp3-HD-Aoede`, and `peer-npc` uses `vi-VN-Chirp3-HD-Puck`. Ignore unapplied gender/pitch. Explicitly configured unknown bindings or failed option application fail the utterance instead of caching a fallback voice.

The roomless preview service runs the shared Python streaming provider module in a separately deployed HTTP runtime. It does not import or start an agent job or room. The web backend authorizes teacher operations and handles private Storage publication; the agent's background worker calls the same trusted publication endpoint. Reuse the already installed `aiohttp` and locked Google TTS dependencies, with HTTPS and an environment-held server-to-server service secret. No provider/Storage write credential reaches the browser or VR.

Cache-aware launch and token renewal sign the VR participant metadata capability `tts_cache_schema_version:1`. The agent requires both that server-issued capability and the exact retained paired identity before cache startup or cache packet acceptance. Legacy participants without capability keep their existing path; capable sessions cannot silently fall back when credentials or retained configuration are unavailable.

The existing Dockerfile launches legacy `src/agent.py`, not `src/agent_v2.py`. Preserve that legacy source and container behavior. Document the V2 worker launch command and preview service command as deployment prerequisites; do not claim the new code is deployed or alter production during implementation.

### Approved preview profile selection

The latest user decision on 2026-10-13 replaces mandatory single-NPC preview context with one editor-wide profile selector populated from the existing profiles in Firestore `tts_configuration/shared`. Every authorized phrase row, including general child phrases and quest phrases without a unique NPC mapping, uses the selected profile for its explicit Preview action. Listing profiles reads existing configuration only: it does not seed profiles, filter them by NPC mappings, synthesize audio, or save anything. Selection is transient editor state and never saves a phrase/NPC association or changes runtime NPC mapping, shared settings, or a retained session snapshot.

The browser submits a known `profile_id`, not arbitrary voice settings. The backend retains active-staff/assigned-child authorization and resolves that profile's effective settings from Firestore once per explicit preview request. Missing/removed profiles and unsupported settings fail before audio lookup/generation; empty or unavailable profile lists disable Preview/Reject rather than inventing a fallback. Phrase DTOs need no preview-only NPC fields. Runtime speech continues to resolve NPC-to-profile mappings from the retained session configuration as specified in Section 4.

Each preview response includes the exact effective profile used for its key and audio. If shared settings changed after the profile list loaded, update the editor's selected-profile voice/rate display from that response before starting playback; never play audio under a stale settings label. A settings change during synthesis does not mutate that request's snapshot. This display reconciliation introduces no profile version, writes, or additional generation.

The editor owns a single active browser audio instance and a latest-preview request guard across all rows. Changing the selected profile stops audio, aborts pending preview/download work where possible, invalidates every row's listened receipt, and makes all earlier responses ineligible, even if selection later returns to the same profile ID. Guard with editor selection generation, request ID, stable row identity, and exact requested text. These are transient UI counters, not cache identity or profile-version management. A response that reconciles changed effective settings invalidates other old-setting responses/receipts before the accepted response is displayed and played. Reject uses only the still-current receipt whose matching audio actually began playback; no result is attached to a different row/profile. A rejection already accepted by the backend still targets that original heard artifact, never the newly selected profile.

### Approved live same-key concurrency scope

Independent planning review identified a conflict between live synthesis sharing and distinct immediate playback/cancellation on the current participant-wide output. Approved on 2026-10-13: keep separate live invocations; coalesce preview synthesis and agent publication. Section 8.2 and AC-26 explicitly reflect that scope. A newer live owner cancels the previous owned speech before starting its own stream; neither invocation adopts another's terminal outcome. The revised implementation plan is now approved.

### Restart reconciliation and participant authentication

On the reliable cache topic, the agent sends `FIXED_AUDIO_HELLO` with contract version 1, session ID and its boot UUID. This packet does not invent a VR run ID. VR authenticates the current server-role agent participant, then replies with `FIXED_AUDIO_RECONCILE` carrying its actual run ID, active activation IDs and current activity. Only the paired VR participant can establish the agent's run scope. Automatic reminders remain disabled until reconciliation; cached manual playback remains independent of agent availability. This handshake introduces neither a first-audio acknowledgment nor a stream-start timeout.

Reconciliation optionally includes `completed_opening_activation_ids`: bounded unique active IDs whose opening previously completed successfully. Retain this proof separately from latest playback activity, so a later manual hint cannot erase it. The replacement agent applies proof when the corresponding evaluation activation arrives, without replaying the opening. Ignore retired runs, stale activities and updates attempting to reopen terminal outcomes. A valid automatic-reminder terminal activity releases its correlated scheduler and emits accepted evidence once; local playback does not require an extra VR DONE packet.

The installed Unity SDK has no public participant-kind property. Application-owned code may inspect its internal `ParticipantInfo`, with an application-owned IL2CPP preservation declaration, to require the server-issued `ParticipantKind.Agent`. Identity prefixes and mutable attributes alone do not establish agent authority. Require current room presence, retire old boot IDs and reject their late events. Use server join time to resolve overlapping restart participants, or fail closed until the agent is unambiguous. Missing role evidence disables automatic reminders rather than accepting an unverified sender. Do not edit vendor SDK code.

## 12. Acceptance criteria

| ID | Scenario | Required result |
| --- | --- | --- |
| AC-01 | Replay a valid cached phrase/profile across sessions. | No additional TTS call. |
| AC-02 | Local miss and remote hit with the agent unavailable. | VR downloads, validates and plays the audio. |
| AC-03 | Both caches miss while the agent is healthy. | One TTS output supplies live streaming and background capture; upload is not on the playback critical path. |
| AC-04 | Upload worker is blocked, fails or its queue is full. | Live frames continue; no extra synthesis or playback-outcome change. |
| AC-05 | Capture exceeds 4 MiB or fails. | Drop capture only; continue the stream. |
| AC-06 | Cancel before complete synthesis, or end without expected final evidence. | No valid cache publication or ready event. |
| AC-07 | Interrupt playback after complete synthesis. | Complete audio may be cached; the invocation stays canceled. |
| AC-08 | Concurrent instances publish the same key. | Retain one valid object; no overwrite or distributed lock. |
| AC-09 | A ready packet is lost or arrives after cancellation. | Later lookup can hit; no automatic replay or revival of the canceled invocation. |
| AC-10 | Remote-cache access fails or exceeds its configured budget. | Select stream once; a late download never takes over the invocation. |
| AC-11 | Skip/change node during local playback or live streaming. | Stop the correct source; no old audio or terminal event affects a newer invocation. |
| AC-12 | SDK returns a handle that is still playing, then cancellation arrives. | The stored handle is interrupted; the test uses real SDK-compatible await semantics. |
| AC-13 | SDK reports a generation exception on a completed handle. | Report failure rather than success. |
| AC-14 | Repeat a teacher hint request or deliver it twice. | New requests advance sequentially; duplicate delivery does not advance or play again. |
| AC-15 | Manual hint cache miss. | Stream the phrase selected by VR, with no second selection by the agent. |
| AC-16 | Automatic reminder becomes due. | Play the agent-selected phrase through the cache resolver; no additional direct agent play. |
| AC-17 | VR is preparing/playing local audio, then completes/cancels. | Suppress competing automatic reminders; resume silence timing only for the current activation. |
| AC-18 | User starts speaking during an automatic reminder, or the activation ends. | Cancel the corresponding preparation/playback; delayed events do not affect another activation. |
| AC-19 | Agent fails. | Cached dialogue/manual hints remain usable; no automatic reminder or false quest completion. |
| AC-20 | Change shared settings during a session and restart the agent. | The current session retains its original configuration; the next session receives the changed settings. |
| AC-21 | Compare C#/Python key fixtures, including `1` versus `1.0`. | Identical effective inputs produce identical keys. |
| AC-22 | Change rate `1.0`, `0.9`, then `1.0`. | Reuse the first key/audio if available; no third identity due to edit history. |
| AC-23 | Change only one profile. | Other profiles retain their keys; distinct profiles remain separate even with equal settings. |
| AC-24 | Encounter a partial, corrupt or incompatible audio file. | Treat it as a miss; never publish or play it as valid cache. |
| AC-25 | Cached dialogue completes, fails or is canceled. | Existing node behavior receives the correct terminal outcome; no unresolved blocking wait. |
| AC-26 | Same-key preview generation or completed agent publication jobs overlap within one process; distinct live invocations arrive concurrently. | Coalesce preview synthesis and agent publication; preview callers receive complete audio. Live invocations retain separate playback/cancellation/terminal outcomes, with one audible owner and no suffix joining or generation wait ahead of live streaming. |
| AC-27 | Teacher explicitly previews a row with the editor-wide selected profile and a valid cached artifact. | Resolve the selected existing Firestore profile server-side and play the current cached artifact; make no additional TTS call or require a phrase/NPC mapping. |
| AC-28 | Teacher explicitly previews a row/selected profile with no valid cached artifact. | Generate through shared TTS logic without creating/joining a LiveKit room, publish a complete artifact, and play it on the web. |
| AC-29 | Compare preview and agent generation configuration. | Use the same key rules, effective options, provider synthesis behavior and cache format; no independent web TTS implementation. |
| AC-30 | Teacher rejects the artifact just heard. | Reject that artifact without changing the phrase/profile; another preview can generate a replacement. A stale reject must not remove a newer acceptable artifact. |
| AC-31 | A headset retains the rejected artifact, then opens a session. | Reconcile rejection information before cache use; disregard/remove the old local artifact and resolve a replacement normally. |
| AC-32 | An utterance is already playing when the artifact is rejected elsewhere. | Do not push an interruption or replacement into that active session; apply rejection synchronization at subsequent session launch. |
| AC-33 | A pre-rejection upload finishes after rejection. | It cannot restore the rejected artifact as the current valid cache entry or invalidate the replacement. |
| AC-34 | Preview/reject request is unauthorized or preview generation fails. | Deny unauthorized operations; publish no incomplete artifact on generation failure, and leave phrase/profile data unchanged. |
| AC-35 | Open/list/scroll/mount phrase rows or change the editor-wide selected profile without clicking Preview. | List existing profiles and update transient UI only; perform no TTS generation, audio lookup, phrase/profile writes, or phrase/NPC association saves. |
| AC-36 | Preview the same authorized quest/general phrase under two existing profiles, including a profile without a runtime NPC mapping. | Use the selected profile in each case; produce separate configuration-based keys/artifacts and reject unknown/removed/unsupported profiles before lookup/generation. |
| AC-37 | Change profile while a preview/download/response is pending, or return to the previously selected profile. | Stop old audio, invalidate receipts across all rows, cancel/ignore stale responses globally, and never play or reject under the new selection using an old response. |
| AC-38 | Shared settings change after profile listing or during preview generation. | Display the exact server-resolved request settings before playback; key/audio/receipt use that immutable snapshot, with no silent session/configuration change or profile-version management. |

Verify the adopted D3 policy with active files, concurrent downloads, a full cache, interrupted writes and an invalid index. Demonstrate that storage stays within the cap, cleanup stays within its directory, and persistence failure does not interrupt valid audio already obtained.

## 13. Technical evidence and verification limits

The capture/lifecycle design was checked on 2026-10-13 against the repository's locked and installed `livekit-agents` 1.7.1 and `livekit-plugins-google` 1.6.8. Recheck version-specific APIs during planning if dependencies change.

Local probes using a real `SpeechHandle` and fake session/room reproduced the await-before-store cancellation gap without contacting TTS. Existing test mocks did not reproduce the SDK's await behavior. Some whole-session reset paths separately call `session.interrupt()`, so the finding does not imply every skip currently leaks audio.

The installed handle API requires explicit generation-error inspection. The inspected provider code also includes a graceful-end path for API error 499 without sufficient final evidence; neither a clean iterator end nor playout completion alone is enough to accept a captured file. The inspected SDK defaults are a 10-second provider timeout and up to three retries. These settings are unchanged by this design and do not confirm that VR received audio.

Implementation is in progress following plan approval. No Unity/headset verification has been performed. PCM copying has measurable work; "background caching" does not imply zero overhead. Verify stream latency with upload blocked and measure capture cost on the actual implementation.

References:

- [LiveKit pipeline nodes](https://docs.livekit.io/agents/logic/nodes/).
- [LiveKit audio customization](https://docs.livekit.io/agents/multimodality/audio/customization/).
- [Cloud Storage consistency and atomic object publication](https://docs.cloud.google.com/storage/docs/consistency).
- [Cloud Storage generation preconditions](https://docs.cloud.google.com/storage/docs/request-preconditions).
- [Cloud Storage lifecycle management](https://docs.cloud.google.com/storage/docs/lifecycle).
- [Python `asyncio.to_thread`](https://docs.python.org/3/library/asyncio-task.html#asyncio.to_thread).

## 14. Web preview and audio rejection — approved extension

### 14.1 Purpose

A complete, correctly formatted TTS file can still have audible defects: an inappropriate voice, stumbled pronunciation or other sound-quality problems under the same profile configuration. Technical file validation does not establish acceptable speech quality.

Approved on 2026-10-13: a teacher can listen to the relevant cached audio inside the existing phrase editor and reject an unsatisfactory artifact without deleting the phrase or changing its profile. Its cache-consistency rules are part of this specification; the web interface is a separate task group in the implementation plan, not an independent cache contract.

### 14.2 Preview and replacement flow

1. The editor lists existing shared Firestore profiles in one editor-wide selector. The teacher selects a profile, then explicitly clicks **Preview audio** on a phrase row. Selection, scrolling and row mounting never perform audio lookup/generation or save data. The backend authorizes the operation and resolves the selected profile's effective settings server-side. If a valid current artifact exists, play that artifact without another synthesis.
2. On a miss, the web requests backend generation. Trusted server-side code uses the same TTS generation logic, effective settings, key rules and audio format as the Python agent, publishes the complete artifact to Firebase Storage, and returns authorized audio access for playback.
3. Generation does not require a LiveKit room or an active lesson session. Voice-profile data alone is insufficient: backend code still needs provider access and credentials. The browser only requests generation and plays the resulting audio.
4. If the teacher finds the audio unsatisfactory, **Reject audio** removes it from valid cache use while leaving phrase text and profile settings unchanged. This action must be visibly distinct from the existing **Delete phrase** action.
5. A subsequent explicit preview may generate, cache and play a replacement. Do not automatically generate on phrase edits, profile selection, scrolling or row mounting, load every phrase in advance, or add an approval queue; synthesis happens only on an explicit Preview miss.

The generated artifact is available to VR through the same cache repository. Preview generation may wait for the complete file before web playback; this does not change immediate streaming on a VR cache miss. Share the provider synthesis implementation rather than building a separate web TTS path that might behave differently under the same displayed profile.

### 14.3 Session-launch synchronization

Approved on 2026-10-13: phrase-editor audio rejection is synchronized to VR when a session opens. Do not add a live push mechanism that interrupts or replaces an utterance already playing in an active session.

Deleting the cloud file alone is insufficient because a headset may retain that audio across sessions. The backend must retain enough rejection information for VR to identify and remove or disregard the rejected local artifact when opening a subsequent session. Resolve this information before accepting that artifact as a hit for the new session. Rejecting audio does not delete the phrase, change the voice settings or introduce separate profile-version management.

Rejection must target the artifact the teacher actually heard. Duplicate/stale requests must not reject a newer artifact by accident. An upload already in flight when rejection occurs must not restore the rejected artifact as an acceptable replacement. Preserve immutable artifact publication and configuration-based input keys; finalize the necessary artifact identity and concurrency checks in the plan.

### 14.4 Planning details

- Populate one editor-wide selector from existing shared Firestore profiles independently of runtime NPC mappings. Use its known `profile_id` for explicit per-row preview, retain backend authorization and server-resolved effective settings, and display the chosen profile/voice/rate without inventing a phrase's unique NPC speaker. Do not add preview-only NPC enrichment to phrase DTOs or save selection as an association.
- Apply the global stale-response/receipt guards and authoritative settings display defined in Section 11. Profile listing/selection/scrolling is read-only and never invokes synthesis; empty/removed/unsupported profiles have explicit unavailable states rather than fallbacks.
- Define the authenticated web/backend generation route and execution environment that reuse the Python synthesis logic without requiring a room. Provider credentials remain server-side.
- Define rejection metadata, artifact identity, conditional publication/deletion, stale-request handling and session-launch reconciliation. Metadata must survive deleting the cloud file, or an old local file could become a false hit.
- Specify generation error responses, duplicate preview requests, repeated rejection and stale ready events in the shared contract and tests. Preview/rejection must not silently save phrase edits or change session configuration.

The revised detailed implementation plan incorporates these decisions and was approved by the user's “start” on 2026-10-13.

## 15. Same-computer runtime cache development — approved 2026-10-15

The user approved a bounded development exception to the backend API HTTPS requirement. Production HTTPS, cache identity, immutable session configuration, streaming/capture/publication, authentication and rejection semantics remain unchanged. Audio stays in Firebase Storage; all signed Storage read URLs remain HTTPS-only.

- Python defaults to HTTPS. HTTP is accepted only when `TTS_CACHE_ALLOW_LOCAL_HTTP=true` and the parsed backend host is exactly `localhost` or `127.0.0.1`. Reject other loopback spellings, IPv6, LAN/public hosts, invalid or empty explicit ports, credentials, queries and fragments. Local HTTP bypasses proxies and authenticated API requests never follow redirects. The existing shared producer secret and session checks remain required.
- Unity Editor has an explicit `EditorPrefs` local override for `http://127.0.0.1:3000`; the configured production URL remains serialized independently. Select the effective URL once per session launch and retain it for snapshot fetch and subsequent cache requests. A dedicated Editor-only HTTP JSON path preserves existing cancellation, size and timeout bounds without changing project-wide insecure-HTTP settings. Backend bearer access requires the same scheme, host and port; signed Storage reads never receive it and cannot follow redirects.
- Player builds exclude the override and local transport and reject HTTP or loopback backend URLs at runtime. HTTPS loopback URLs remain usable in Editor with the existing TLS certificate validation. No change to scenes, build settings, profile contracts or LiveKit transport is required.
- Run Next.js in development mode on port 3000 with its existing Firebase/LiveKit configuration and shared service secret. The roomless preview service is optional and separate: `TTS_PREVIEW_SERVICE_URL` is not required for runtime cache tests. Physical headsets require a separately reachable HTTPS backend. Container, VM and remote-agent loopback networking is not included.

Setup and focused checks: [Runtime cache local setup](../guides/2026-10-15-tts-runtime-cache-local.md).
