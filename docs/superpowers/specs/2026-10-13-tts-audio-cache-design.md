# Two-Layer TTS Audio Cache — Design Specification

Date: 2026-10-13

Status: Draft for review. Core design approved; three decision gates remain in Section 11.

Source: [Brainstorming record](2026-10-12-tts-audio-cache-brainstorm.md).

This document is the current design contract. The source records the discussion; it is not an additional implementation contract. Requirements below preserve the approved design. Recommendations in Section 11 remain unapproved. No product code has been implemented.

## 1. Purpose and success criteria

Fixed dialogue and Voice Quest phrases are spoken repeatedly, causing unnecessary TTS charges. Reuse the audio across plays and sessions, including lesson phrases and teacher-provided phrases for individual children.

The system must also play cached dialogue and teacher-requested hints when the agent is unavailable. A phrase that cannot be obtained from either cache or the agent may be skipped. Complete audio availability during an agent outage is not a requirement.

Success means a valid cache hit makes no TTS call; a cache miss starts the existing LiveKit streaming path without waiting for file creation or upload; and cache failures do not interrupt that stream.

## 2. Scope and boundaries

In scope:

- Fixed dialogue, Voice Quest openings, teacher-requested hints and automatic silence reminders.
- Persistent VR audio files and a shared Firebase Storage audio repository.
- Session-level voice configuration supplied to both VR and the agent.
- Background capture of the same TTS output used for live streaming.
- Playback cancellation, terminal outcomes and silence-reminder coordination across both audio sources.

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
| VR client | Select manual hints, resolve cache hits, play local clips, request streaming on a miss, and report local playback lifecycle. |
| Python agent | Evaluate speech, schedule automatic reminders, stream fixed phrases on demand, capture complete TTS output, and upload in the background. |
| Backend | Supply session configuration and authorized audio download access independently of the agent. |
| Firebase Storage | Persist complete audio objects across sessions and agent replacements. |
| Cloud Firestore | Store shared profile configuration and persistent session metadata. |
| RTDB | Continue existing session-state and low-frequency telemetry responsibilities. |

The Storage bucket is private. A valid session may read the shared TTS cache region; the agent writes audio. Do not add per-child, per-lesson, per-profile or per-audio read ACLs. Lesson and child data determine which phrases the application selects, separately from download authorization.

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

There must be no additional synthesis solely to create a cache file. Cache misses must not wait for complete synthesis, WAV packaging, upload or a subsequent file download before live playback.

## 6. Speech types and silence reminders

### 6.1 Dialogue and openings

Fixed dialogue and Voice Quest openings use the resolver in Section 5. Preserve the existing once-per-activation opening behavior. Dialogue can use cached audio even when the agent is unavailable.

Blocking dialogue retains the existing node timeout policy: the current default is 30 seconds and can be configured per node. Do not add a separate stream-start timer or infer utterance start from RTC track subscription or an agent's generic speaking state. Retain nonblocking dialogue behavior rather than adding a completion wait.

The remote-cache budget is distinct from background upload time. Its duration and deadline integration are decision gate D2; the old five-second stream-start proposal is not an approved timeout for this release. With a cold cache and a hung agent, blocking dialogue may wait until its existing node timeout. No separate hint-completion timeout is introduced.

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
| Same-key concurrency | Coalesce work within one process; tolerate rare duplicate synthesis across instances |

Copy valid PCM into the bounded buffer while yielding original frames. Do not await filesystem operations, upload or cache lookup in the frame-forwarding loop. Capture errors or buffer overflow abandon caching for that utterance while allowing live streaming to continue.

Enqueue only complete buffers without blocking. A full queue drops the new cache job, not the live stream. The worker packages WAV and performs blocking Storage work through `asyncio.to_thread`; packaging may use RAM rather than agent-disk temporary files. Cache-write failure must not trigger a second synthesis or change playback outcome.

Process-local coalescing must preserve complete audio for participating requests, not attach a late request to an incomplete suffix. Exact ownership and buffering mechanics belong in the plan. Do not introduce a distributed lock or a generation-wait step ahead of the live stream.

Use an immutable object path, such as `tts-cache/v1/<profile-id>/<cache-key>.wav`, with format, sample count, duration and checksum metadata. Publish with `if_generation_match=0`; if another writer has published a valid object, retain it instead of overwriting it.

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

## 9. Local cache validity

Only complete, compatible and validated local files are cache hits. Partial downloads, incompatible formats and invalid checksums are misses. Publication must prevent playback from reading a file that is still being written.

The approved persistent-storage cap is 256 MB. Detailed eviction, temporary-space accounting and cloud retention remain decision gate D3. The recommendation in Section 11 preserves active files and treats cache persistence as best effort rather than a prerequisite for playing valid audio.

Consistency does not require identical file sets on all headsets. It requires consistent audio identity, session settings, complete artifacts and invocation outcomes.

## 10. Contract and delivery requirements

The implementation plan must define concrete event names, required fields, types, schema versions, validators, identity rules, terminal outcomes, error mappings, ordering, cancellation and reconnect reconciliation before handler tasks begin. Include JSON examples and shared fixtures; update affected C#, Python and TypeScript contracts together, including changed session metadata or RTDB keys.

The plan must map local completion, failure and skip into the existing dialogue and quest behavior without making a failed hint complete a quest. It must also map automatic-reminder completion into existing accepted-reminder telemetry.

Delivery order:

1. Correct and test the existing speech lifecycle.
2. Add streaming capture and background Storage publication.
3. Integrate session profiles, VR resolution/local playback and hint lifecycle coordination with synchronized contracts.
4. Validate source changes, cancellation and actual audio behavior with focused headset checks.

No full Unity launch, batch-mode run or full-suite test is required by default. Use focused non-Unity checks and Python tests, then provide the relevant Unity test filters and manual checks for the user. Follow GitNexus impact analysis before editing symbols and change detection before finalizing or committing code. Do not edit vendor SDKs or introduce competing microphone capture.

## 11. Decisions required before the implementation plan is finalized

The following are explicit decision gates, not implied approvals or missing requirements for an implementer to invent.

| Gate | Approved boundary | Remaining choice |
| --- | --- | --- |
| D1 — Download access | Private Storage; valid sessions read the shared cache; backend access independent of the agent. | Device/session authentication, download-URL lifetime and renewal behavior. |
| D2 — Remote-cache deadline | One budget for remote lookup and download; errors/exhaustion select streaming; no new stream-start timeout. | Budget duration, exact start/end points including readiness validation, and interaction with the existing dialogue deadline. |
| D3 — Cache lifecycle | VR cap of 256 MB; use only complete valid artifacts. | Approval or revision of the eviction/retention recommendation below. |

### Recommended D3 policy — not yet approved

- Use LRU on VR without per-child, lesson or profile quotas or age-based expiry. Count audio, metadata and temporary downloads within the cap, reserve required space and bound concurrent disk writes.
- Protect files currently being read, played or written. Do not pin an entire lesson or require preload. If space cannot be reserved, skip persistence; valid audio already obtained within bounded RAM may still play without being saved.
- Download under a temporary name, check size/format/checksum, then publish through a same-directory rename. Remove abandoned temporary files at startup. Use a small rebuildable index, not a second database; unverifiable files remain misses. Cleanup is restricted to the application's TTS cache directory.
- Keep cloud audio in this release. Do not add age-based deletion, cloud access counters, reference counting or a garbage-collection job. Removing a phrase from lesson/child data stops its selection but does not delete an audio object potentially shared elsewhere.
- Deliberate cloud deletion is separate administrative maintenance, with checked scope and a generation precondition. Do not add headset delete permissions, an agent delete API or an administration UI. Cloud deletion does not revoke copies already downloaded to headsets; fleet-wide erasure is outside this design.

Operational details such as shutdown drain duration, exact serialization and resource-limit constants must be concrete in the plan. D1–D3 must be resolved explicitly; the previous five-second stream-start discussion does not select D2's value.

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
| AC-26 | Same-key synthesis requests overlap within one process. | Coalesce work without supplying incomplete audio to a participating invocation. |

If D3's recommendation is approved, also verify eviction with active files, concurrent downloads, a full cache, interrupted writes and an invalid index. Demonstrate that storage stays within the cap, cleanup stays within its directory, and persistence failure does not interrupt valid audio already obtained.

## 13. Technical evidence and verification limits

The capture/lifecycle design was checked on 2026-10-13 against the repository's locked and installed `livekit-agents` 1.7.1 and `livekit-plugins-google` 1.6.8. Recheck version-specific APIs during planning if dependencies change.

Local probes using a real `SpeechHandle` and fake session/room reproduced the await-before-store cancellation gap without contacting TTS. Existing test mocks did not reproduce the SDK's await behavior. Some whole-session reset paths separately call `session.interrupt()`, so the finding does not imply every skip currently leaks audio.

The installed handle API requires explicit generation-error inspection. The inspected provider code also includes a graceful-end path for API error 499 without sufficient final evidence; neither a clean iterator end nor playout completion alone is enough to accept a captured file. The inspected SDK defaults are a 10-second provider timeout and up to three retries. These settings are unchanged by this design and do not confirm that VR received audio.

No Unity/headset verification or cache implementation has been performed. PCM copying has measurable work; "background caching" does not imply zero overhead. Verify stream latency with upload blocked and measure capture cost on the actual implementation.

References:

- [LiveKit pipeline nodes](https://docs.livekit.io/agents/logic/nodes/).
- [LiveKit audio customization](https://docs.livekit.io/agents/multimodality/audio/customization/).
- [Cloud Storage consistency and atomic object publication](https://docs.cloud.google.com/storage/docs/consistency).
- [Cloud Storage generation preconditions](https://docs.cloud.google.com/storage/docs/request-preconditions).
- [Cloud Storage lifecycle management](https://docs.cloud.google.com/storage/docs/lifecycle).
- [Python `asyncio.to_thread`](https://docs.python.org/3/library/asyncio-task.html#asyncio.to_thread).

The detailed implementation plan follows review of this specification and resolution of its decision gates. Creating this document does not authorize product implementation.
