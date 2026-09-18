# LiveKitService Decomposition Design

**Date:** 2026-09-18
**Status:** Approved design; implementation not started
**Scope:** Unity VR client under `Assets/Project/Scripts/Cloud/LiveKit/`

## 1. Objective

Refactor `LiveKitService` from a god object into cohesive services with single responsibilities while preserving all observable system behavior and existing integration contracts.

The refactor must:

- preserve the current `LiveKitService` singleton façade and its public API;
- preserve Inspector configuration, scenes, prefabs, DataPacket topics, payloads, reliability, and event semantics;
- maintain exclusive microphone ownership within the LiveKit subsystem;
- prevent stale callbacks, overlapping asynchronous operations, and client-connection generations from racing;
- keep all Unity object operations on the Unity main thread;
- permit incremental migration with verification after every extraction.

The Unity client does not provision a LiveKit room on the server. It creates a client-side `LiveKit.Room` SDK object and joins the room identified by the server-issued token. This document therefore uses the term **room connection**, not room creation.

## 2. Current Context and Risk

`LiveKitService` currently combines:

- singleton and Unity lifecycle management;
- LiveKit client connection lifecycle;
- DataPacket transport and legacy packet parsing;
- microphone capture and publication;
- POV camera capture and video publication;
- legacy remote audio playback;
- V2 NPC audio routing, pending-track management, and stream rebinding;
- compatibility APIs and public gameplay events.

GitNexus reports a **HIGH** upstream blast radius: at least eight direct consumers across LiveKit, voice, gameplay, RTDB, and waiting-area code. This is a lower bound because some consumers bind through interfaces. The working tree also contains uncommitted V2 audio-routing work that forms part of the behavioral baseline and must not be overwritten.

## 3. Compatibility Contract

The first refactor phase is zero-touch for existing consumers.

The following remain unchanged:

- `LiveKitService.Instance` and singleton creation behavior;
- `LiveKitService` as the only required scene component;
- `ILiveKitRoomClient`, `ILiveKitDataPacketClientV2`, and `INpcAudioRouterV2`;
- all existing public methods, properties, and events;
- all serialized fields, default values, and Inspector workflow;
- test seams such as `StreamFactory`, route counters, and track simulation helpers;
- DataPacket topics, payload shapes, encoding, and reliability;
- the special raw forwarding behavior for topic `lesson-graph-v2.voice`;
- legacy packet handling for `SET_ACTIVE_QUEST`, `VERBAL_HINT`, `ON_REMINDER`, `QUEST_MATCHED`, `AGENT_INIT_FAILED`, and `QUEST_STATUS`;
- the initial-connect behavior that raises `ReconnectedV2` after a successful connection;
- the existing POV connection wait limit of 50 attempts at 200 ms each;
- legacy NPC `AudioSource` fallback when no V2 route is registered.

No Python agent or web-dashboard change is required because this refactor does not change a cross-stack contract.

## 4. Chosen Architecture

Use a compatibility façade with plain C# services behind it. `LiveKitService` remains a `MonoBehaviour`, Unity lifecycle host, and composition root. It constructs the internal services in `Awake`, drains the LiveKit-scoped main-thread queue in `Update`, and triggers deterministic disposal in `OnDestroy`.

No service may call `LiveKitService.Instance`, search the scene for dependencies, or create a second global dispatcher.

```text
LiveKitService
|
+-- LiveKitLifecycleCoordinator
|   +-- serializes Connect / Disconnect / Destroy
|
+-- LiveKitRoomConnection
|   +-- owns the client-side LiveKit.Room object and SDK subscriptions
|
+-- LiveKitMainThreadExecutor
|   +-- generation-aware FIFO command queue drained from Update
|
+-- LiveKitDataPacketTransport
|   +-- sends and receives raw bytes, topics, and reliability
|
+-- LegacyVoicePacketAdapter
|   +-- preserves legacy packet serialization and parsing
|
+-- LiveKitMicrophonePublisher
|   +-- owns microphone capture and the local audio track
|
+-- LiveKitPovVideoPublisher
|   +-- owns capture camera, RenderTexture, source, coroutine, and video track
|
+-- LiveKitNpcAudioRouter
    +-- owns remote audio routing, pending tracks, and active streams
```

### 4.1 LiveKitService

Responsibilities:

- preserve the current public and serialized surface;
- construct and wire internal services;
- forward public API calls to the correct service;
- relay internal events through the existing public events;
- host `Update`, coroutine operations, and Unity destruction.

It must not contain packet parsing, media resource state, route dictionaries, or connection state-machine logic.

### 4.2 LiveKitLifecycleCoordinator

Responsibilities:

- serialize connection lifecycle operations;
- issue monotonically increasing connection generations;
- cancel or invalidate superseded operations;
- coordinate media and routing teardown before connection teardown;
- enforce latest-request-wins semantics for overlapping connect requests;
- observe and log every internal task failure.

It is the only component that decides the cross-service teardown order.

### 4.3 LiveKitRoomConnection

Responsibilities:

- create and own the Unity client's `LiveKit.Room` SDK object;
- join the server-selected room using URL and token;
- subscribe and unsubscribe exact SDK event delegates;
- expose a connection-state snapshot and current handle;
- forward SDK callbacks into the main-thread executor with the captured handle;
- disconnect and dispose only its own connection object.

It does not own microphone, video, remote audio, or gameplay protocol state.

### 4.4 LiveKitMainThreadExecutor

This is a LiveKit-scoped executor owned by `LiveKitService`, not an application-wide singleton.

Responsibilities:

- accept commands from SDK callback threads;
- establish a total FIFO processing order at the enqueue boundary;
- associate each command with a connection generation;
- execute valid commands from `LiveKitService.Update`;
- reject stale-generation commands and commands posted after shutdown.

Payload byte arrays are copied at the SDK callback boundary before enqueueing. Unity objects are created, mutated, or destroyed only while draining on the main thread.

### 4.5 LiveKitDataPacketTransport

Responsibilities:

- publish raw byte payloads with the requested topic and reliability;
- validate that a current connected handle and local participant are available;
- receive raw SDK payloads after main-thread dispatch;
- forward V2 payloads without understanding their JSON schema.

It has no dependency on VoiceQuest, dialogue, or legacy packet DTOs.

### 4.6 LegacyVoicePacketAdapter

Responsibilities:

- preserve the current legacy packet byte representation;
- build packets used by `SendActiveQuest`, `SendVerbalHint`, and `SendOnReminder`;
- parse non-V2 incoming packets;
- emit internal speech-matched, agent-error, and quest-status signals.

The first refactor does not replace string-built legacy JSON with a new serializer. Any wire-format improvement is a separate change with its own compatibility tests.

### 4.7 LiveKitMicrophonePublisher

Responsibilities:

- remain the sole owner of microphone selection and capture;
- own the microphone GameObject, `MicrophoneSource`, and `LocalAudioTrack`;
- publish once, mute or unmute idempotently, and unpublish from the exact connection handle that created the track;
- roll back all temporary resources if publication fails or the generation becomes stale.

No other component may start competing microphone capture.

### 4.8 LiveKitPovVideoPublisher

Responsibilities:

- resolve or validate the requested VR camera;
- wait for a connected handle using the current bounded wait behavior;
- own the capture camera, `RenderTexture`, `TextureVideoSource`, coroutine, and `LocalVideoTrack`;
- publish with the existing dimensions, frame rate, bitrate, and camera source options;
- stop and release resources in the required GPU-safe order;
- unpublish from the exact connection handle that created the track.

### 4.9 LiveKitNpcAudioRouter

Responsibilities:

- preserve legacy `SetAudioSource` behavior;
- own V2 route registration and active route selection;
- own pending and active track dictionaries;
- bind, replace, rebind, and dispose remote `AudioStream` instances;
- preserve multi-NPC isolation, route swapping, pending-track drain, duplicate-SID replacement, and legacy fallback;
- retain the injectable stream factory and current test-facing state queries.

All state mutations occur on the main thread. SDK track callbacks reach the router only through the executor.

## 5. Connection Handle and Ownership

Every successful or attempted client connection is represented by an immutable handle:

```text
RoomConnectionHandle
+-- Generation: monotonically increasing identifier
+-- SdkRoom: the LiveKit.Room instance for that generation
```

Media publishers retain the handle used to create their tracks. Cleanup uses that retained handle rather than reading a mutable global `room` field. This prevents a continuation belonging to connection A from unpublishing or mutating resources belonging to later connection B.

Only `LiveKitRoomConnection` may replace the current SDK room reference. Only the lifecycle coordinator may advance the active generation.

## 6. Lifecycle State Machine

```text
Disconnected -> Connecting -> Connected -> Disconnecting -> Disconnected

Disconnected / Connecting / Connected / Disconnecting -> Destroyed
```

Internally, lifecycle methods return `Task`; internal logic does not use `async void`. The existing public `void` signatures submit tasks to the coordinator, which observes and logs completion and failure.

### 6.1 Connect

1. Assign the request a new generation.
2. Invalidate and cancel the previous generation.
3. Serialize behind any active lifecycle operation.
4. Tear down the previous handle if necessary.
5. Create a client-side SDK room object and attach captured-handle callbacks.
6. Await SDK connection.
7. Verify that the generation is still current.
8. Commit the handle and publish the connected state.
9. Raise `ReconnectedV2` on the main thread, preserving current semantics.

If a newer connect request arrives while an earlier connect is awaiting, the newest request wins. The earlier continuation may only clean up resources tied to its own handle.

### 6.2 Disconnect

Disconnect immediately invalidates the current generation, cancels pending operations where supported, and schedules the following teardown order:

1. reject new commands for the old generation;
2. stop and unpublish the POV track;
3. mute/stop and unpublish the microphone track;
4. dispose remote audio streams and clear pending routing state;
5. detach SDK event handlers;
6. disconnect the client-side SDK room object;
7. publish `Disconnected` state.

The operation is idempotent. A cleanup failure is logged and does not prevent the remaining cleanup stages.

### 6.3 Destroy

`OnDestroy` closes the executor and generation immediately, performs best-effort synchronous cleanup of owned Unity resources, detaches callbacks, and disconnects the SDK object. It does not block the Unity main thread indefinitely waiting for a network task.

Any asynchronous continuation that completes afterward must validate its captured generation before taking action.

## 7. Data and Event Flows

### 7.1 Incoming DataPacket

```text
Room.DataReceived
  -> capture handle and copy payload
  -> LiveKitMainThreadExecutor
  -> LiveKitDataPacketTransport
       |-- topic == "lesson-graph-v2.voice"
       |      -> LiveKitService.DataReceivedV2
       |      -> return without legacy parsing
       |
       +-- any other/legacy topic
              -> LegacyVoicePacketAdapter
              -> existing public legacy events
```

Both voice quest V2 and dialogue V2 continue to share the exact topic `lesson-graph-v2.voice`.

### 7.2 Outgoing DataPacket

```text
PublishDataV2
  -> LiveKitDataPacketTransport

SendActiveQuest / SendVerbalHint / SendOnReminder
  -> LegacyVoicePacketAdapter
  -> LiveKitDataPacketTransport
```

### 7.3 Remote Audio

```text
Room.TrackSubscribed
  -> capture handle and track metadata
  -> main-thread executor
  -> LiveKitNpcAudioRouter
       |-- active V2 route exists: bind stream
       |-- route absent: retain pending track by SID
       +-- no V2 routes: use legacy AudioSource fallback

Room.TrackUnsubscribed
  -> main-thread executor
  -> dispose matching active stream
  -> remove matching active and pending entries
```

### 7.4 Public API Routing

| Existing API | Internal target |
|---|---|
| `Connect`, `Disconnect` | `LiveKitLifecycleCoordinator` |
| `EnableMicrophone` | coordinator and `LiveKitMicrophonePublisher` |
| `EnablePOVCamera`, `DisablePOVCamera` | coordinator and `LiveKitPovVideoPublisher` |
| `PublishDataV2` | `LiveKitDataPacketTransport` |
| legacy send methods | `LegacyVoicePacketAdapter` |
| route and legacy audio-source methods | `LiveKitNpcAudioRouter` |

## 8. Concurrency Invariants

The implementation must preserve these invariants:

1. Unity API calls occur only on the Unity main thread.
2. There is one serialized lifecycle lane for connect, disconnect, and destroy.
3. No lock is held across an `await`.
4. No public event is raised while an internal lock is held.
5. A callback or continuation may mutate state only when its captured generation is current.
6. Media cleanup targets the exact connection handle that created the media.
7. Internal task exceptions are always observed.
8. A failed cleanup stage cannot suppress later cleanup stages.
9. Remote audio state is mutated only by the main-thread router.
10. The executor is the only component requiring cross-thread queue synchronization.
11. Public events are relayed on the Unity main thread.
12. POV frame production does not traverse the command queue; only lifecycle and SDK callbacks do.

Existing Unity-facing public calls, including synchronous router queries, are main-thread APIs. The façade records its owning thread during `Awake` and rejects or development-asserts invalid off-thread Unity-facing calls rather than mutating Unity state concurrently. Calls originating from LiveKit SDK threads are always marshalled through the executor. This makes the thread-affinity contract explicit without introducing a blocking cross-thread `Invoke` that could deadlock while Unity is not pumping `Update`.

The V2 gameplay transports may retain their existing main-thread queues during the compatibility phase. Removing those queues is a separate simplification after the new threading contract has been proven.

## 9. Resource and Error Policy

Media acquisition is transactional:

```text
create temporary resources
  -> publish track
       |-- success and current generation: commit active state
       +-- failure or stale generation: roll back every temporary resource
```

Required idempotent operations include:

- repeated disconnect;
- disabling POV before or after it is active;
- repeated microphone enable or mute requests;
- repeated subscribe for the same track SID;
- unsubscribe for an absent or already-cleaned track;
- re-registering the same route/source;
- `OnDestroy` following manual disconnect.

Error behavior:

- connect failure detaches callbacks, cleans the temporary SDK object, returns to `Disconnected`, and does not raise `ReconnectedV2`;
- media failure does not terminate the room connection or packet transport;
- packet parse failure drops only the invalid packet;
- stale-generation callbacks are intentionally ignored;
- logs include the generation and operation name where useful;
- public gameplay events preserve their current meaning.

## 10. Testing Strategy

Verification is intentionally split between a minimal automated suite owned by the implementation agent and final Real Room acceptance owned by the user. Automated tests cover deterministic concurrency and compatibility cases that are difficult to reproduce manually. They do not attempt to automate the full production workflow.

### 10.1 Agent-Owned Test Runner Gate

The project uses Unity `6000.3.13f1`. Before production refactoring, the agent must run one existing EditMode fixture to prove that Unity Test Runner, licensing, project locking, result-file generation, and command-line exit handling work in the current environment.

The most recent recorded batch run exited with code 1 without a useful test result. Therefore, a missing or unreadable result file is a failed gate, not a passing test. If Unity is already holding the project, licensing fails, or the runner cannot emit results, the agent must report the exact blocker and must not claim that tests passed.

### 10.2 Minimal New Automated Suite

The agent creates no more test surface than required to protect these eight behaviors:

1. `ConnectAThenConnectB_LatestGenerationWins`;
2. `DisconnectDuringConnect_InvalidatesOldContinuation`;
3. `StaleRoomCallback_DoesNotEmitEvents`;
4. `MainThreadExecutor_PreservesOrderAndDropsStaleCommands`;
5. `DisconnectRepeatedly_IsIdempotentAndKeepsTeardownOrder`;
6. `MediaPublishCompletesAfterDisconnect_RollsBackResources`, split into microphone and POV cases only if Unity Test Framework cannot parameterize the coroutine safely;
7. `V2Packet_PreservesBytesTopicAndReliability`;
8. `LegacyPacket_PreservesCurrentPayloadAndEventBehavior`.

These tests use a narrow SDK adapter/factory and deterministic fakes. The fake may pause and complete connect or publication tasks, trigger callbacks, and record operation order. It is a test seam, not a second lifecycle owner.

Unity-frame or task-completion tests use bounded `[UnityTest] IEnumerator` helpers. Every `TaskCompletionSource` is completed on all branches, and every timeout fails explicitly rather than waiting indefinitely.

### 10.3 Existing Regression Fixtures

The agent retains and runs the existing fixtures affected by this refactor:

- `NpcAudioRouteBindingV2Tests`;
- `VoiceQuestTransportV2Tests`.

The agent does not duplicate their assertions in new fixtures.

`LiveKitDialogueTransportV2Tests` is not a completion gate until Dialogue V2 has passed its own Real Room readiness acceptance.

### 10.4 Per-Slice Agent Verification

After each extraction slice, the agent runs only:

1. Unity compile/import;
2. the new fixture directly related to that slice;
3. the two existing regression fixtures above when their paths are affected;
4. `git diff --check`;
5. GitNexus `detect_changes`.

The agent does not automate or claim completion for a real LiveKit room, web dashboard, Python agent, physical microphone permission, Meta Quest, or HTC Vive workflow. Python and web test suites are outside this refactor unless a cross-stack contract changes unexpectedly.

### 10.5 User-Owned Real Room Acceptance

The user performs the final production-like workflow:

1. Create a live session and server-issued room token through the production web/backend path.
2. Pair the VR client and join the intended room.
3. Start the Python voice agent and verify that it joins the same room.
4. Verify web POV reception with exactly one video track at the configured 720p/30 FPS behavior.
5. Interrupt and restore the network; verify that the room connection and POV recover without duplicate participants or video tracks.
6. End the session and verify POV/connection cleanup plus the correct web/RTDB end state.
7. Start a second session in the same application run and verify that the first session left no callback, connection, or POV resource behind.

Immediate manual failure conditions are:

- duplicate participants or video tracks;
- `MissingReferenceException`, `ObjectDisposedException`, or an unobserved task exception;
- reconnect adding a second POV publication instead of restoring or replacing the previous publication;
- the second session receiving callbacks from the first;
- the first session retaining a room connection, camera, coroutine, video source, or render texture after shutdown.

The user records pass/fail for each step and provides the Unity, agent, or web log around any failed step. Connection generation, participant identity, and video track SID are the primary correlation fields.

### 10.6 Deferred Feature Prerequisites

The following are known pre-existing V2 readiness gaps and are not acceptance gates for this behavior-preserving refactor:

- **Voice Quest V2 microphone lifecycle:** `VoiceQuestSourceV2` sends activation through the V2 transport but does not acquire microphone capture on activation or release it on every terminal/cleanup path. Therefore, physical microphone publication and end-to-end `QUEST_MATCHED` are deferred to a separate story.
- **V2 verbal hint/reminder:** `VERBAL_HINT` and `ON_REMINDER` do not yet have an accepted V2 workflow.
- **Dialogue V2 Real Room readiness:** `SPEAK_SCRIPT` code and tests may exist, but production-like NPC playback is not a gate until that feature has completed its own Real Room acceptance.

`SET_ACTIVE_QUEST` packet compatibility remains protected by the automated transport tests. Microphone publisher lifecycle and rollback remain protected by isolated deterministic tests. Neither automated result is presented as proof that the current Voice Quest V2 workflow works end to end on physical hardware.

## 11. Incremental Migration Plan

The refactor must not be a big-bang rewrite.

1. Prove the Unity test runner gate, add the approved minimal automated suite, and establish the current dirty working tree as the behavioral baseline.
2. Add SDK adapter/factory seams and deterministic fakes without moving production behavior.
3. Extract `LiveKitNpcAudioRouter`; preserve façade delegation and all current routing tests.
4. Extract `LiveKitDataPacketTransport` and `LegacyVoicePacketAdapter`.
5. Extract `LiveKitMicrophonePublisher`.
6. Extract `LiveKitPovVideoPublisher`.
7. Add `RoomConnectionHandle`, `LiveKitMainThreadExecutor`, and `LiveKitRoomConnection`.
8. Move lifecycle orchestration into `LiveKitLifecycleCoordinator`.
9. Reduce `LiveKitService` to its approved façade and lifecycle-host responsibilities.
10. Run the approved targeted regression set and GitNexus change detection, then hand the build to the user for Real Room acceptance.

After every slice:

- compile the Unity project;
- run the approved targeted tests and affected existing fixtures;
- inspect GitNexus `detect_changes` output;
- stop if the affected flows exceed the expected slice.

## 12. Acceptance Criteria

The refactor is complete only when:

- existing scenes, prefabs, and consumers require no migration;
- public APIs, interfaces, events, and wire contracts remain unchanged;
- the eight approved automated behaviors and the two required existing fixtures pass;
- every Unity operation is main-thread confined;
- there are no unobserved task exceptions;
- stale generations cannot emit events or mutate current state;
- repeated connect/disconnect leaves no owned resource behind;
- the implementation preserves a single microphone-capture owner; physical V2 microphone behavior remains deferred as documented in section 10.6;
- GitNexus reports only expected affected flows;
- the user completes and approves the Real Room checklist;
- the scoped POV/connection checks required by the target device release pass under the user's Real Room workflow.

## 13. Explicit Non-Goals

This refactor does not:

- change server-side LiveKit room provisioning or token issuance;
- change Python agent or web-dashboard contracts;
- replace legacy packet JSON construction;
- remove legacy `ILiveKitRoomClient` or `SetAudioSource` behavior;
- redesign gameplay voice/dialogue transports;
- add the missing Voice Quest V2 microphone lifecycle;
- complete V2 verbal hint/reminder or Dialogue V2 Real Room readiness;
- introduce a global event bus or global main-thread dispatcher;
- add sibling `MonoBehaviour` components to scenes.
