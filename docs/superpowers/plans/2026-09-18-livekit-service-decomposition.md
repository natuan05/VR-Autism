# LiveKitService Decomposition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `LiveKitService` god object with focused internal services while retaining the existing singleton façade, public contracts, serialized configuration, wire behavior, and Unity scene compatibility.

**Architecture:** `LiveKitService` remains the sole `MonoBehaviour`, composition root, and compatibility façade. Plain C# services own room connection lifecycle, main-thread dispatch, DataPackets, legacy packet adaptation, microphone publication, POV publication, and NPC audio routing; immutable connection handles plus monotonically increasing generations prevent stale callbacks and continuations from mutating a newer connection.

**Tech Stack:** Unity 6000.3.13f1, C#, LiveKit Unity SDK, NUnit/Unity Test Framework EditMode tests, GitNexus.

**Spec:** `docs/superpowers/specs/2026-09-18-livekit-service-decomposition-design.md`

## Global Constraints

- Do not begin production edits until the owner has committed the current V2 working-tree changes or explicitly authorized a baseline commit. `LiveKitService.cs` already contains user-owned uncommitted routing work; do not stash, reset, overwrite, or silently absorb it.
- Run GitNexus `impact({target: "<symbol>", direction: "upstream", repo: "VR-Autism"})` before editing every existing class or method. Stop and warn the user before any HIGH or CRITICAL edit.
- Run GitNexus `detect_changes({scope: "staged", repo: "VR-Autism"})` before every commit. Stage exact paths only; never use `git add -A` or `git add .`.
- Keep `LiveKitService.Instance`, all current public methods/events/properties, `ILiveKitRoomClient`, `ILiveKitDataPacketClientV2`, `INpcAudioRouterV2`, serialized field names/defaults, and scene/prefab wiring unchanged.
- Keep `lesson-graph-v2.voice`, payload bytes, reliability flags, legacy event meanings, initial-connect `ReconnectedV2`, and the 50 × 200 ms POV wait behavior unchanged.
- Keep LiveKit as the only real-time transport. Do not add HTTP audio downloads or legacy peer-to-peer WebRTC.
- Preserve exclusive microphone ownership. Do not call `Microphone.Start()` from any new class; the extracted publisher owns the existing `MicrophoneSource` path.
- All Unity object operations execute on the Unity main thread. Do not hold locks across `await`, invoke public events under a lock, or block a worker thread waiting for `Update()`.
- Do not implement Voice Quest V2 microphone acquisition/release, V2 verbal hint/reminder, or Dialogue V2 Real Room readiness in this plan.
- Create no more than the eight approved automated behavior tests; behavior 6 may use separate microphone and POV test methods. Required existing regression fixtures are `NpcAudioRouteBindingV2Tests` and `VoiceQuestTransportV2Tests`.
- The user, not the implementation agent, owns final Real Room acceptance.

## File Structure

| Path | Responsibility |
|---|---|
| `Assets/Project/Scripts/Cloud/LiveKit/AssemblyInfo.cs` | Grants `Assembly-CSharp-Editor` access to internal test seams. |
| `Assets/Project/Scripts/Cloud/LiveKit/RoomConnectionHandle.cs` | Immutable generation plus SDK adapter reference. |
| `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitRoomAdapter.cs` | Narrow SDK boundary and factory used by production and deterministic fakes. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitRoomAdapter.cs` | Production wrapper around `LiveKit.Room` and `LocalParticipant`. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitMainThreadExecutor.cs` | Generation-aware FIFO queue drained by `LiveKitService.Update()`. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitRoomConnection.cs` | Creates, subscribes, commits, detaches, and disconnects client-side room connections. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitLifecycleCoordinator.cs` | Serializes connect/disconnect/destroy and enforces teardown order. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitNpcAudioRouter.cs` | Owns legacy/V2 remote audio route, pending, and active-stream state. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitDataPacketTransport.cs` | Publishes and classifies raw DataPackets without parsing gameplay JSON. |
| `Assets/Project/Scripts/Cloud/LiveKit/LegacyVoicePacketAdapter.cs` | Preserves legacy JSON construction/parsing and legacy public-event meanings. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitMicrophonePublisher.cs` | Owns microphone resource creation, publication, mute, rollback, and disposal. |
| `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitCoroutineHost.cs` | Narrow coroutine host supplied by the façade. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitPovVideoPublisher.cs` | Owns POV capture resources, publication, rollback, and GPU-safe disposal. |
| `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs` | Retained façade, serialized configuration, composition root, queue pump, and public event relay. |
| `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs` | Contains only the eight approved automated behaviors and bounded async helpers. |

Unity must generate and stage the `.meta` file for every new `.cs` file after import. Do not reuse or hand-copy GUIDs from another asset.

## Execution Preflight — Hard Gate, No Commit

- [ ] **Step 1: Confirm the baseline is safe to edit**

Run:

```powershell
git status --short
git diff -- Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs
```

Expected: the owner has resolved the current dirty baseline before execution. If `LiveKitService.cs` remains modified, stop and ask the user whether to commit that baseline; do not continue with patch staging against overlapping user changes.

- [ ] **Step 2: Prove the Unity test runner works**

Run with the Unity Editor closed:

```powershell
New-Item -ItemType Directory -Force 'TestResults' | Out-Null
& 'D:\Program Files\Unity Editors\6000.3.13f1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath 'D:\Lab\VR-Autism' `
  -runTests -testPlatform EditMode `
  -testFilter 'VRAutism.Gameplay.LessonGraphV2.Tests.Editor.QuestSourceV2Tests' `
  -testResults 'D:\Lab\VR-Autism\TestResults\LiveKitPreflight.xml' `
  -logFile 'D:\Lab\VR-Autism\TestResults\LiveKitPreflight.log' `
  -quit
```

Expected: process exit code `0`, result XML exists, and its `<test-run>` reports `failed="0"`. Missing XML, a project-lock message, licensing failure, or exit code `1` fails the gate.

---

### Task 1: Add Connection and Main-Thread Execution Primitives

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/AssemblyInfo.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/RoomConnectionHandle.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitRoomAdapter.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitRoomAdapter.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitMainThreadExecutor.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`

**Interfaces:**
- Consumes: LiveKit SDK types `Room`, `Participant`, `IRemoteTrack`, `RemoteTrackPublication`, `RemoteParticipant`, `DataPacketKind`, local track types, and `TrackPublishOptions`.
- Produces: `RoomConnectionHandle(long generation, ILiveKitRoomAdapter adapter)`, `ILiveKitRoomAdapter`, `ILiveKitRoomAdapterFactory.Create()`, and `LiveKitMainThreadExecutor.AdvanceGeneration/Post/Drain/Close`.

- [ ] **Step 1: Add editor visibility for internal seams**

Create `AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
```

- [ ] **Step 2: Write the failing executor test**

Add the first approved behavior to `LiveKitServiceDecompositionTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace VRAutism.Cloud.LiveKit.Tests.Editor
{
    public sealed class LiveKitServiceDecompositionTests
    {
        [Test]
        public void MainThreadExecutor_PreservesOrderAndDropsStaleCommands()
        {
            var executor = new LiveKitMainThreadExecutor();
            var calls = new List<string>();

            executor.AdvanceGeneration(2);
            Assert.IsTrue(executor.Post(1, () => calls.Add("stale")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("first")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("second")));
            executor.Drain();

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);
            executor.Close();
            Assert.IsFalse(executor.Post(2, () => calls.Add("closed")));
        }
    }
}
```

- [ ] **Step 3: Run the test and confirm the red state**

Run the Unity command from preflight with this filter:

```text
VRAutism.Cloud.LiveKit.Tests.Editor.LiveKitServiceDecompositionTests.MainThreadExecutor_PreservesOrderAndDropsStaleCommands
```

Expected: compilation fails because `LiveKitMainThreadExecutor` does not exist.

- [ ] **Step 4: Implement the immutable handle and SDK boundary**

Use these exact contracts:

```csharp
internal sealed class RoomConnectionHandle
{
    public long Generation { get; }
    public ILiveKitRoomAdapter Adapter { get; }
    public Room SdkRoom => Adapter.SdkRoom;
    public bool IsConnected => Adapter.IsConnected;

    public RoomConnectionHandle(long generation, ILiveKitRoomAdapter adapter)
    {
        Generation = generation;
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }
}

internal interface ILiveKitRoomAdapter
{
    Room SdkRoom { get; }
    bool IsConnected { get; }
    string RoomName { get; }
    string LocalParticipantSid { get; }
    event Action<byte[], Participant, DataPacketKind, string> DataReceived;
    event Action<Room> Reconnected;
    event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
    event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;
    Task ConnectAsync(string roomUrl, string token);
    void PublishData(byte[] data, string topic, bool reliable);
    Task PublishAudioTrackAsync(LocalAudioTrack track, TrackPublishOptions options);
    Task PublishVideoTrackAsync(LocalVideoTrack track, TrackPublishOptions options);
    void UnpublishAudioTrack(LocalAudioTrack track);
    void UnpublishVideoTrack(LocalVideoTrack track);
    void Disconnect();
}

internal interface ILiveKitRoomAdapterFactory
{
    ILiveKitRoomAdapter Create();
}
```

`LiveKitRoomAdapter` must forward SDK events with add/remove accessors, await `Room.Connect` and `LocalParticipant.PublishTrack`, preserve `PublishData(data, reliable: true)` when `topic` is null, use the topic overload otherwise, and call `UnpublishTrack(track, false)`.

- [ ] **Step 5: Implement the generation-aware FIFO executor**

Use a private `Queue<WorkItem>` protected by one lock. Enqueue and close under that lock; dequeue under the lock but invoke actions after releasing it. Record `Thread.CurrentThread.ManagedThreadId` in the constructor and throw `InvalidOperationException` if `Drain()` runs on another thread.

```csharp
internal bool Post(long generation, Action action)
internal void AdvanceGeneration(long generation)
internal void Drain()
internal void Close()
internal bool IsOwnerThread { get; }
```

`Drain()` discards any item whose generation differs from the current generation. `Close()` rejects future posts and clears queued work.

Expose `IsOwnerThread` and use it later as the façade's non-blocking thread-affinity guard. Do not add a synchronous cross-thread `Invoke` API.

- [ ] **Step 6: Run the test and confirm green**

Run the Task 1 test filter.

Expected: one test passes, zero failures, and Unity creates `.meta` files for all new source assets.

- [ ] **Step 7: Stage, inspect, and commit Task 1**

Stage only Task 1 files and their generated `.meta` files. Run GitNexus `detect_changes` with `scope: "staged"`; expected risk is LOW because these are new, unreferenced primitives.

Commit:

```powershell
git commit -m "refactor(livekit): add connection primitives"
```

---

### Task 2: Extract NPC Audio Routing

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitNpcAudioRouter.cs`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:55-151,216-260,600-944`
- Verify: `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/NpcAudioRouteBindingV2Tests.cs`

**Interfaces:**
- Consumes: `RemoteAudioTrack`, `AudioSource`, and injected `Func<RemoteAudioTrack, AudioSource, IDisposable>`.
- Produces: all existing `INpcAudioRouterV2` operations plus `HandleTrackSubscribed`, `HandleTrackUnsubscribed`, `Reset`, and the current public test seams delegated by the façade.

- [ ] **Step 1: Run mandatory impact analysis before editing**

Run GitNexus upstream impact for `LiveKitService`, `OnTrackSubscribed`, `OnTrackUnsubscribed`, `RegisterNpcAudioRoute`, and `SetActiveNpcRoute` using the exact file path. The class-level result is expected to remain HIGH; report the direct consumers and confirm that the edit retains the façade before proceeding.

- [ ] **Step 2: Run the existing router fixture before extraction**

Run:

```text
VRAutism.Gameplay.LessonGraphV2.Tests.Editor.NpcAudioRouteBindingV2Tests
```

Expected: all existing route registration, swap, isolation, pending-drain, and dynamic-switching tests pass. If this untracked fixture does not compile or has not been accepted into the baseline, stop and resolve the baseline instead of rewriting it.

- [ ] **Step 3: Create `LiveKitNpcAudioRouter` by moving the current behavior verbatim**

Use this public-to-façade internal surface:

```csharp
internal sealed class LiveKitNpcAudioRouter
{
    internal Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory { get; set; }
    internal int ActiveV2StreamCount { get; }
    internal int PendingV2TrackCount { get; }
    internal string ActiveNpcBindingId { get; }
    internal bool IsV2TrackActive(string trackSid);
    internal bool IsV2TrackPending(string trackSid);
    internal void SimulatePendingAudioTrack(string trackSid, string participantIdentity);
    internal void SimulateActiveAudioStream(string trackSid, AudioSource source, string npcBindingId);
    internal AudioSource GetActiveStreamSource(string trackSid);
    internal string GetActiveStreamRoute(string trackSid);
    internal void RegisterNpcAudioRoute(string npcBindingId, AudioSource source);
    internal void UnregisterNpcAudioRoute(string npcBindingId);
    internal bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source);
    internal bool SetActiveNpcRoute(string npcBindingId);
    internal void SetLegacyAudioSource(AudioSource source);
    internal void HandleTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant);
    internal void HandleTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant);
    internal void Reset();
}
```

Move the route dictionaries, pending/active entry types, legacy stream dictionary, legacy pending track, active route ID, stream creation, binding, duplicate SID replacement, route swap, and fallback rules without rewriting their decisions. `Reset()` disposes both legacy and V2 streams and clears pending/active collections; cleanup continues after individual disposal exceptions.

- [ ] **Step 4: Replace façade state with delegation**

Keep every existing public member on `LiveKitService` and delegate exactly:

```csharp
public Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory
{
    get => _audioRouter.StreamFactory;
    set => _audioRouter.StreamFactory = value;
}

public void RegisterNpcAudioRoute(string id, AudioSource source) =>
    _audioRouter.RegisterNpcAudioRoute(id, source);

private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant) =>
    _audioRouter.HandleTrackSubscribed(track, publication, participant);
```

Apply the same direct delegation to every existing route/query/test member. Replace the remote-audio portion of `Disconnect()` with `_audioRouter.Reset()`.

- [ ] **Step 5: Run router regression**

Run `NpcAudioRouteBindingV2Tests` again.

Expected: the same tests pass with no test modification and no changes to `INpcAudioRouterV2`.

- [ ] **Step 6: Stage, inspect, and commit Task 2**

Run `git diff --check`, stage only `LiveKitNpcAudioRouter.cs`, its `.meta`, and `LiveKitService.cs`, then run GitNexus staged change detection. Confirm affected scope is limited to current LiveKit/audio-routing consumers.

Commit:

```powershell
git commit -m "refactor(livekit): extract npc audio router"
```

---

### Task 3: Extract Raw DataPacket Transport and Legacy Packet Adapter

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitDataPacketTransport.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LegacyVoicePacketAdapter.cs`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:151-168,431-536`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`

**Interfaces:**
- Consumes: `Func<RoomConnectionHandle>` and `ILiveKitRoomAdapter.PublishData`.
- Produces: `LiveKitDataPacketTransport.PublishDataV2/PublishLegacy/HandleIncoming`, `DataReceivedV2`, `LegacyDataReceived`; `LegacyVoicePacketAdapter.SendActiveQuest/SendVerbalHint/SendOnReminder/HandleIncoming` and its three typed events.

- [ ] **Step 1: Run impact analysis before packet edits**

Run upstream impact for `PublishDataV2`, `SendActiveQuest`, `SendVerbalHint`, `SendOnReminder`, and `OnDataReceived`. Confirm `ILiveKitDataPacketClientV2` remains unchanged.

- [ ] **Step 2: Add the two approved packet tests in red state**

Add one V2 test that asserts outgoing bytes/topic/reliability and incoming raw forwarding, and one legacy test that asserts exact JSON plus legacy event mapping:

```csharp
[Test]
public void V2Packet_PreservesBytesTopicAndReliability()
{
    var adapter = new FakeRoomAdapter { Connected = true };
    var handle = new RoomConnectionHandle(3, adapter);
    var transport = new LiveKitDataPacketTransport(() => handle);
    var payload = new byte[] { 1, 2, 3 };
    byte[] received = null;
    string receivedTopic = null;
    transport.DataReceivedV2 += (data, topic) => { received = data; receivedTopic = topic; };

    transport.PublishDataV2(payload, "lesson-graph-v2.voice", true);
    transport.HandleIncoming(payload, null, "lesson-graph-v2.voice");

    CollectionAssert.AreEqual(payload, adapter.LastPublishedData);
    Assert.AreEqual("lesson-graph-v2.voice", adapter.LastPublishedTopic);
    Assert.IsTrue(adapter.LastPublishedReliable);
    CollectionAssert.AreEqual(payload, received);
    Assert.AreEqual("lesson-graph-v2.voice", receivedTopic);
}

[Test]
public void LegacyPacket_PreservesCurrentPayloadAndEventBehavior()
{
    var adapter = new FakeRoomAdapter { Connected = true };
    var transport = new LiveKitDataPacketTransport(() => new RoomConnectionHandle(4, adapter));
    var legacy = new LegacyVoicePacketAdapter(transport);
    var matched = 0;
    legacy.SpeechMatched += () => matched++;

    legacy.SendActiveQuest("Wash Hands", new[] { "soap", "rinse" });
    Assert.AreEqual(
        "{\"event\":\"SET_ACTIVE_QUEST\",\"quest_name\":\"Wash Hands\",\"default_phrases\":[\"soap\",\"rinse\"]}",
        Encoding.UTF8.GetString(adapter.LastPublishedData));
    Assert.IsNull(adapter.LastPublishedTopic);
    Assert.IsTrue(adapter.LastPublishedReliable);

    legacy.HandleIncoming(Encoding.UTF8.GetBytes("{\"event\":\"QUEST_MATCHED\"}"), null);
    Assert.AreEqual(1, matched);
}
```

Expected red state: the two new service types do not exist.

- [ ] **Step 3: Implement `LiveKitDataPacketTransport`**

`PublishDataV2` silently returns for null data, missing handle, disconnected handle, or missing adapter. `PublishLegacy` uses a null topic. `HandleIncoming` copies the byte array before raising events, raises only `DataReceivedV2` for exact ordinal topic `lesson-graph-v2.voice`, returns immediately for that topic, and raises `LegacyDataReceived` for every other topic.

- [ ] **Step 4: Implement `LegacyVoicePacketAdapter`**

Move the nested packet DTO and current string-built legacy payloads without changing casing, field names, quoting, or reliability. Preserve the substring fallback for `QUEST_MATCHED` when JSON parsing produces no event. Expose:

```csharp
internal event Action SpeechMatched;
internal event Action<string> AgentError;
internal event Action<string, string> QuestStatusUpdated;
```

- [ ] **Step 5: Wire façade event relay**

In `Awake`, subscribe transport legacy bytes to the adapter and adapter events to private relay methods. Keep `DataReceivedV2`, `OnSpeechMatched`, `OnAgentError`, and `OnQuestStatusUpdate` as the existing public events. Unsubscribe during destruction through one private `UnwireServices()` method.

- [ ] **Step 6: Run packet and existing transport regressions**

Run the two new packet methods and `VoiceQuestTransportV2Tests`.

Expected: all pass; no Python or web files change.

- [ ] **Step 7: Stage, inspect, and commit Task 3**

Run diff check and staged GitNexus detection. Stage only Task 3 files plus `.meta` files.

Commit:

```powershell
git commit -m "refactor(livekit): extract data packet services"
```

---

### Task 4: Extract Transactional Microphone Publication

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitMicrophonePublisher.cs`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:47-51,216-240,539-598`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`

**Interfaces:**
- Consumes: `RoomConnectionHandle`, a main-thread `Transform` parent, and `Func<long, bool>` generation validation.
- Produces: `LiveKitMicrophonePublisher.SetEnabledAsync(bool, RoomConnectionHandle, Func<long,bool>)` and `Stop()`.

- [ ] **Step 1: Run impact analysis before microphone edits**

Run upstream impact for `EnableMicrophone` and `Disconnect`. Current indexed impact for `EnableMicrophone` is LOW with two direct consumers, but class-level impact remains HIGH; preserve the public façade.

- [ ] **Step 2: Add the bounded stale-publication test**

Add test seams in the same production file:

```csharp
internal interface ILiveKitMicrophonePublication : IDisposable
{
    Task PublishAsync(RoomConnectionHandle handle);
    void Start();
    void SetMuted(bool muted);
    void Unpublish(RoomConnectionHandle handle);
}

internal interface ILiveKitMicrophonePublicationFactory
{
    bool TryCreate(Transform parent, out ILiveKitMicrophonePublication publication);
}
```

Add the approved microphone branch of behavior 6:

```csharp
[UnityTest]
public IEnumerator MediaPublishCompletesAfterDisconnect_RollsBackResources_Microphone()
{
    var publication = new FakeMicrophonePublication();
    var publisher = new LiveKitMicrophonePublisher(new FakeMicrophoneFactory(publication), null);
    var generation = 7L;
    var handle = new RoomConnectionHandle(7, new FakeRoomAdapter { Connected = true });

    var task = publisher.SetEnabledAsync(true, handle, value => value == generation);
    generation = 8;
    publication.CompletePublish();
    yield return CompleteWithinFrames(task, 60);

    Assert.IsTrue(publication.Unpublished);
    Assert.IsTrue(publication.Disposed);
    Assert.IsFalse(publication.Started);
}
```

`CompleteWithinFrames` must iterate at most the supplied frame count, rethrow task faults, and call `Assert.Fail` on timeout.

- [ ] **Step 3: Run the microphone test and confirm red**

Expected: compilation fails because `LiveKitMicrophonePublisher` does not exist.

- [ ] **Step 4: Implement publication commit/rollback**

`SetEnabledAsync(false, ...)` mutes an existing publication and returns. Enabling an existing publication unmutes it. Enabling without an active publication creates a candidate, awaits publish, validates the captured generation, then starts/unmutes and commits the candidate. A stale or failed candidate unpublishes using its captured handle, disposes, and never becomes active.

The production factory moves the current first-device selection, microphone GameObject parenting, `MicrophoneSource`, `LocalAudioTrack.CreateAudioTrack`, 64 kbps encoding, publish-before-start order, and log behavior. It must not call `Microphone.Start()` directly.

- [ ] **Step 5: Delegate the public API and cleanup**

Keep `public void EnableMicrophone(bool enable)`. It obtains the current handle, starts the publisher task through the façade's observed-task helper, and preserves the current disconnected warning/no-op. Replace microphone cleanup inside `Disconnect()` with `_microphonePublisher.Stop()`.

- [ ] **Step 6: Run the microphone and packet tests**

Expected: the stale-publication test passes; packet tests remain green.

- [ ] **Step 7: Stage, inspect, and commit Task 4**

Run diff check and staged GitNexus detection. Stage only the microphone publisher, `.meta`, façade, and test file.

Commit:

```powershell
git commit -m "refactor(livekit): extract microphone publisher"
```

---

### Task 5: Extract Transactional POV Video Publication

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitCoroutineHost.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitPovVideoPublisher.cs`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:39-43,153-160,216-218,270-428`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`

**Interfaces:**
- Consumes: `ILiveKitCoroutineHost`, video settings, a current-handle provider, and generation validation.
- Produces: `EnableAsync(Camera, Func<RoomConnectionHandle>, Func<long,bool>, CancellationToken)` and `Disable()`.

- [ ] **Step 1: Run impact analysis before POV edits**

Run upstream impact for `EnablePOVCamera` and `DisablePOVCamera`. The indexed direct consumer is `LiveSessionReporter`; retain both public methods unchanged.

- [ ] **Step 2: Add the POV branch of approved behavior 6**

Define focused seams:

```csharp
internal interface ILiveKitCoroutineHost
{
    Coroutine StartLiveKitCoroutine(IEnumerator routine);
    void StopLiveKitCoroutine(Coroutine coroutine);
}

internal interface ILiveKitPovPublication : IDisposable
{
    Task PublishAsync(RoomConnectionHandle handle);
    void BeginFrames();
    void Unpublish(RoomConnectionHandle handle);
}

internal interface ILiveKitPovPublicationFactory
{
    ILiveKitPovPublication Create(Camera camera, int width, int height, int frameRate);
}
```

Add `MediaPublishCompletesAfterDisconnect_RollsBackResources_Pov` using a pending fake publication, advance the generation before completing publish, and assert `Unpublish` plus `Dispose` occurred while `BeginFrames` did not.

- [ ] **Step 3: Run the POV test and confirm red**

Expected: compilation fails because the POV publisher does not exist.

- [ ] **Step 4: Move the current POV resource construction and cleanup**

The production publication owns the capture camera, URP additional camera data, render texture, texture source, update coroutine, and local video track. Preserve width `1280`, height `720`, frame rate `30`, maximum bitrate `1500000`, camera-copy settings, disabled HDR/MSAA/shadows/post-processing, and publish-before-active commit.

Cleanup order is fixed:

```text
stop coroutine -> unpublish captured track -> stop source -> wait GPU requests
-> dispose source -> detach/destroy camera -> release/destroy render texture
```

`EnableAsync` preserves the 50 attempts × 200 ms connection wait and performs generation validation after every await. `Disable()` is idempotent.

- [ ] **Step 5: Make the façade the coroutine host and delegate public methods**

Implement `ILiveKitCoroutineHost` explicitly on `LiveKitService`:

```csharp
Coroutine ILiveKitCoroutineHost.StartLiveKitCoroutine(IEnumerator routine) => StartCoroutine(routine);
void ILiveKitCoroutineHost.StopLiveKitCoroutine(Coroutine coroutine) => StopCoroutine(coroutine);
```

Keep public `EnablePOVCamera(Camera)` and `DisablePOVCamera()` unchanged and delegate them to the publisher. Replace the POV cleanup body in `Disconnect()` with `_povPublisher.Disable()`.

- [ ] **Step 6: Run both media rollback tests**

Expected: microphone and POV stale-publication tests pass with bounded completion and no real device/network access.

- [ ] **Step 7: Stage, inspect, and commit Task 5**

Run diff check and staged GitNexus detection. Stage only Task 5 files, `.meta` files, façade, and the test file.

Commit:

```powershell
git commit -m "refactor(livekit): extract pov video publisher"
```

---

### Task 6: Serialize Room Lifecycle and Finalize the Compatibility Façade

**Files:**
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitRoomConnection.cs`
- Create: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitLifecycleCoordinator.cs`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:9-38,170-268,946-949`
- Modify: `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`
- Verify: `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/NpcAudioRouteBindingV2Tests.cs`
- Verify: `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/VoiceQuestTransportV2Tests.cs`

**Interfaces:**
- Consumes: the adapter factory, executor, packet transport, router, microphone publisher, POV publisher, and ordered teardown delegates.
- Produces: `ConnectAsync`, `DisconnectAsync`, `Destroy`, `CurrentHandle`, `IsConnected`, connection callbacks carrying their captured handle, and observed public façade operations.

- [ ] **Step 1: Run impact analysis and report the HIGH façade risk**

Run upstream impact for `LiveKitService`, `Connect`, `Disconnect`, `Awake`, `Start`, and `OnDestroy`. Record the eight known direct class consumers and verify no consumer migration is planned. Stop if any new HIGH/CRITICAL process appears outside the approved LiveKit/voice/gameplay scope.

- [ ] **Step 2: Add the four lifecycle tests in red state**

Add exactly these approved methods:

```csharp
[UnityTest] public IEnumerator ConnectAThenConnectB_LatestGenerationWins();
[UnityTest] public IEnumerator DisconnectDuringConnect_InvalidatesOldContinuation();
[UnityTest] public IEnumerator StaleRoomCallback_DoesNotEmitEvents();
[UnityTest] public IEnumerator DisconnectRepeatedly_IsIdempotentAndKeepsTeardownOrder();
```

Use a `FakeRoomAdapterFactory` that returns queued adapters, each with a bounded `TaskCompletionSource<bool>` connect gate. The first test completes A after B has been requested, then completes B, and asserts only B is current and only B raises the façade reconnect signal. The second asserts A disconnects and never commits. The third raises `DataReceived` on A after generation rollover, drains the executor, and asserts no public packet callback. The fourth records the exact single sequence `pov`, `microphone`, `audio`, `room` across two disconnect calls.

- [ ] **Step 3: Implement `LiveKitRoomConnection`**

Use these signatures:

```csharp
internal Task<RoomConnectionHandle> CreateConnectedAsync(
    long generation, string roomUrl, string token, CancellationToken cancellationToken);
internal void Commit(RoomConnectionHandle handle);
internal void DetachAndDisconnect(RoomConnectionHandle handle);
internal RoomConnectionHandle CurrentHandle { get; }
internal bool IsCurrent(long generation);
internal event Action<RoomConnectionHandle, byte[], Participant, DataPacketKind, string> DataReceived;
internal event Action<RoomConnectionHandle> Reconnected;
internal event Action<RoomConnectionHandle, IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
internal event Action<RoomConnectionHandle, IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;
```

Create the handle before attaching delegates so every delegate captures that exact handle. `CreateConnectedAsync` checks cancellation before and after the SDK await. `DetachAndDisconnect` removes the exact delegates registered for that handle, calls disconnect once, and clears current only when reference-equal.

- [ ] **Step 4: Implement `LiveKitLifecycleCoordinator`**

Use one `SemaphoreSlim(1, 1)`, one monotonic `long` generation, and one lifetime `CancellationTokenSource`. Public internal operations return `Task`; only the façade converts them to observed fire-and-forget work.

```csharp
internal Task ConnectAsync(string roomUrl, string token);
internal Task DisconnectAsync();
internal void Destroy();
internal RoomConnectionHandle CurrentHandle { get; }
internal bool IsConnected { get; }
internal long CurrentGeneration { get; }
internal LiveKitLifecycleState State { get; }
internal event Action ConnectedOrReconnected;
```

Define `LiveKitLifecycleState` in the coordinator file with `Disconnected`, `Connecting`, `Connected`, `Disconnecting`, and `Destroyed`. State transitions follow the state machine in section 6 of the spec; `IsConnected` is true only for a current connected handle in `Connected` state.

Maintain a per-generation `CancellationTokenSource` in addition to the lifetime token. `ConnectAsync` increments generation before waiting for the semaphore, advances the executor generation immediately, cancels and replaces the prior generation token, disconnects the prior handle, connects a candidate, and commits only if its generation and linked token remain current. The LiveKit SDK await may not itself be cancellable, so a stale candidate is detached and disconnected immediately when that await completes.

`DisconnectAsync` increments generation immediately, cancels the per-generation token, advances the executor, enters `Disconnecting`, then under the semaphore runs teardown in this exact order while continuing after individual exceptions:

```text
pov.Disable -> microphone.Stop -> audioRouter.Reset
-> roomConnection.DetachAndDisconnect
```

`Destroy()` sets `Destroyed` and invalidates/cancels the active generation before any cleanup, closes the executor, performs idempotent best-effort teardown without waiting indefinitely, and prevents future requests. Any lifecycle operation already holding the semaphore may only clean its captured handle after observing cancellation; it cannot publish state, events, or resources after `Destroyed`.

- [ ] **Step 5: Rebuild `LiveKitService` as composition root and façade**

In `Awake`, preserve singleton logic, record the Unity thread through executor construction, create all services, subscribe internal events, and keep `DontDestroyOnLoad`. Add `Update()` containing only `_mainThreadExecutor?.Drain()`.

Add one private `EnsureMainThread(string operation)` helper backed by `LiveKitMainThreadExecutor.IsOwnerThread`. Every existing public API that touches Unity objects or synchronous router state calls it first and returns without mutation after a development assertion/error when invoked off-thread. Never block the caller waiting for `Update()`.

Public methods become direct delegation/observed-task wrappers. Use one helper:

```csharp
private async void Observe(Task task, string operation)
{
    try { await task; }
    catch (Exception exception)
    {
        Debug.LogError($"[LiveKitService] {operation} failed: {exception.Message}");
    }
}
```

This is the only permitted `async void`; it is a Unity-facing exception-observation boundary, contains no state machine logic, and observes every task. `IsConnectedV2` delegates to coordinator state. Initial successful connect and same-handle SDK reconnect both relay `ReconnectedV2` on the main thread.

Wire room callbacks only through the executor:

```csharp
_roomConnection.DataReceived += (handle, data, participant, kind, topic) =>
    _mainThreadExecutor.Post(handle.Generation,
        () => _dataPackets.HandleIncoming(data, participant, topic));

_roomConnection.TrackSubscribed += (handle, track, publication, participant) =>
    _mainThreadExecutor.Post(handle.Generation,
        () => _audioRouter.HandleTrackSubscribed(track, publication, participant));
```

Copy incoming bytes before posting. Apply the same captured-handle pattern to unsubscribe and reconnect.

- [ ] **Step 6: Run the new decomposition fixture**

Run:

```text
VRAutism.Cloud.LiveKit.Tests.Editor.LiveKitServiceDecompositionTests
```

Expected: eight approved behaviors pass; nine test methods are acceptable only because media behavior 6 is split into microphone and POV methods.

- [ ] **Step 7: Run required existing regressions**

Run these filters separately so a missing result cannot mask another fixture:

```text
VRAutism.Gameplay.LessonGraphV2.Tests.Editor.NpcAudioRouteBindingV2Tests
VRAutism.Gameplay.LessonGraphV2.Tests.Editor.VoiceQuestTransportV2Tests
```

Expected: all pass. Do not require `LiveKitDialogueTransportV2Tests` for this refactor gate.

- [ ] **Step 8: Verify façade and contract preservation**

Run:

```powershell
rg -n 'public (async )?void Connect|public void Disconnect|public (async )?void EnablePOVCamera|public void DisablePOVCamera|public (async )?void EnableMicrophone|public void PublishDataV2|public void SendActiveQuest|public void SendVerbalHint|public void SendOnReminder|public void RegisterNpcAudioRoute|public bool SetActiveNpcRoute' 'Assets\Project\Scripts\Cloud\LiveKit\LiveKitService.cs'
git diff --check
```

Expected: every compatibility entry point remains on `LiveKitService`; the file contains no route dictionaries, packet DTO, microphone resource fields, or POV resource fields.

- [ ] **Step 9: Refresh GitNexus and inspect final affected flows**

Run from repository root:

```powershell
node .gitnexus/run.cjs analyze
```

Then run staged `detect_changes`. Confirm the affected flows are the approved LiveKit connection, RTDB handshake/POV, legacy VoiceQuest, V2 packet transport, and NPC audio-routing flows. Any cross-stack schema or RTDB-key impact is a failure because this plan changes neither.

- [ ] **Step 10: Stage, inspect, and commit Task 6**

Stage only Task 6 files, façade, tests, and generated `.meta` files. Review `git diff --cached --stat`, `git diff --cached --check`, and GitNexus staged change detection.

Commit:

```powershell
git commit -m "refactor(livekit): serialize room lifecycle"
```

---

## Final Handoff Gate — User-Owned Real Room Acceptance

The implementation agent stops after automated verification and provides the commit list, test-result XML paths, GitNexus affected-flow report, and a build ready for manual validation. The agent does not claim production completion before the user performs section 10.5 of the spec:

1. create the production-path live session and token;
2. pair VR and join the room;
3. verify the Python agent joins the same room;
4. verify exactly one POV video track at configured 720p/30 FPS behavior;
5. interrupt and restore the network with no duplicate participant/video publication;
6. end the session and verify POV/connection plus web/RTDB cleanup;
7. start a second session in the same application run and verify no callback, connection, or POV resource remains from the first.

Voice Quest V2 physical microphone, end-to-end `QUEST_MATCHED`, V2 hint/reminder, and Dialogue V2 Real Room playback remain explicitly deferred and do not block this refactor acceptance.
