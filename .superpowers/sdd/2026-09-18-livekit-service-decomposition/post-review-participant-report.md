# Post-review participant-boundary corrective report

## Scope and root cause

This corrective task addressed only the two findings in `post-review-participant-brief.md`.

- `LiveKitDataPacketTransport` checks the current SID and then calls the adapter. During reconnect/disconnect, `LiveKitRoomAdapter.PublishData` could observe `_room.LocalParticipant == null` after that check and throw. The real adapter boundary therefore did not safely drop outgoing legacy or V2 packets.
- `LiveKitRoomAdapter.PublishVideoTrackAsync` returned a completed task when `_room.LocalParticipant` was null. `LiveKitPovVideoPublisher` consequently treated publication as successful and could begin frames/commit an active POV without an SDK track.

The smallest fix changes only `LiveKitRoomAdapter`: DataPacket publication now returns without publishing when the captured participant is absent; video publication now faults with the existing `InvalidOperationException` used by audio publication. Existing payload bytes, topic, reliability, POV wait bound, and public contracts are unchanged.

## GitNexus pre-edit impact

The worktree index was stale (three commits behind) and initially returned `UNKNOWN/not found` for the adapter, transport, and decomposition-test symbols. A fresh `node .gitnexus/run.cjs analyze` completed successfully (26,887 nodes, 44,792 edges, 300 flows), but the refreshed graph still had no symbol nodes for `LiveKitRoomAdapter`, `PublishData`, `PublishVideoTrackAsync`, or the test methods. This is documented index/parser coverage loss, not evidence of zero impact.

The indexed `LiveKitService` façade impact was HIGH (8 direct consumers, 2 affected flows, 3 modules), with the known lower-bound interface caveat. The façade was not edited. The intended affected flow scope is limited to LiveKit outbound legacy/V2 DataPackets and transactional POV media publication/rollback; no RTDB key, cross-stack packet schema, Python, or web-dashboard files were changed.

## TDD evidence

Before production edits, existing approved methods were strengthened (no new test method):

- `V2Packet_PreservesBytesTopicAndReliability` directly exercises a real `LiveKitRoomAdapter(new Room())` with no local participant and asserts no exception.
- `MediaPublishCompletesAfterDisconnect_RollsBackResources_Pov` directly exercises the real adapter boundary and asserts absent-participant video publication returns a faulted task with `InvalidOperationException`, while retaining the existing fake-publication rollback assertions.

RED command:

```text
D:\Program Files\Unity Editors\6000.3.13f1\Editor\Unity.exe -batchmode -nographics -projectPath C:\Users\Admin\.codex\worktrees\livekit-service-decomposition\VR-Autism -runTests -testPlatform EditMode -testFilter VRAutism.Cloud.LiveKit.Tests.Editor.LiveKitServiceDecompositionTests -testResults TestResults-participant-red.xml -logFile Unity-participant-red.log
```

Result: XML `TestResults-participant-red.xml` reported 9 total, 7 passed, 2 failed. The packet assertion failed on `InvalidOperationException: LiveKit local participant is unavailable`; the POV assertion failed because the publish task was not faulted.

GREEN command:

```text
D:\Program Files\Unity Editors\6000.3.13f1\Editor\Unity.exe -batchmode -nographics -projectPath C:\Users\Admin\.codex\worktrees\livekit-service-decomposition\VR-Autism -runTests -testPlatform EditMode -testFilter VRAutism.Cloud.LiveKit.Tests.Editor.LiveKitServiceDecompositionTests -testResults TestResults-participant-green.xml -logFile Unity-participant-green.log
```

Result: XML `TestResults-participant-green.xml` reported 9 total, 9 passed, 0 failed.

## Verification

- `TestResults-participant-green.xml`: decomposition 9/9 passed.
- `TestResults-participant-npc.xml`: `NpcAudioRouteBindingV2Tests` 8/8 passed.
- `TestResults-participant-voicequest.xml`: `VoiceQuestTransportV2Tests` 3/3 passed.
- Broader `TestResults-participant-all.xml`: 366/411 passed, 45 failed. None of the decomposition, NPC, or VoiceQuest tests failed. The failures are baseline/environmental and outside this two-file change: DialogueNodeExecutor timing (5), HoldTouch/LessonGraph expected-log/timing (8), LessonNodeDataDrawer headless graphics (3), Dialogue V2 argument validation (7), UniGLTF texture/device (12), and VRM sample/model fixtures (10). Representative causes include `Dialogue text must be non-empty`, `No graphic device is available`, null texture references, and missing `Tests/Models/AliciaSolid_vrm-0.51.vrm`. Because there is no pre-change all-suite XML in this task, attribution of those unrelated failures to baseline is necessarily uncertain.

## Changed files/symbols

- `Assets/Project/Scripts/Cloud/LiveKit/LiveKitRoomAdapter.cs`: `LiveKitRoomAdapter.PublishData`; `LiveKitRoomAdapter.PublishVideoTrackAsync`.
- `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`: strengthened existing `V2Packet_PreservesBytesTopicAndReliability` and `MediaPublishCompletesAfterDisconnect_RollsBackResources_Pov` methods.
- This report file.

## Staged GitNexus scope

Staged GitNexus `detect_changes` completed immediately before commit: `changed_files=3`, `changed_count=0`, `affected_count=0`, `risk_level=low`, and no affected processes. The zero symbol/process count is consistent with the documented index omission for the edited adapter/test files, not evidence that the behavior has no callers. Manual scope remains the two adapter-boundary symbols and two strengthened test methods, with LiveKit outbound DataPacket and POV publication flows only. No unrelated user files or generated XML/logs were staged.

## Commit

Commit: `fix(livekit): guard local participant publication` (the final hash is the commit recorded by `git log -1`; a self-referential hash is intentionally omitted from this report).

## Remaining concerns

- GitNexus does not index the edited adapter/test symbols in this Unity worktree even after a successful full rebuild; symbol-level blast-radius evidence is therefore limited to the indexed HIGH façade result and manual source tracing.
- The broader EditMode suite remains red for the unrelated baseline/environment categories listed above.
- Real-room reconnect/media acceptance remains user-owned and was not claimed here.
