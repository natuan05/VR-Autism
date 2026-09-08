# V2 NPC Audio Routing Addendum

**Owner:** Epic 2, Story 2.1  
**Status:** Approved scope amendment  
**Related review finding:** ADV-01

## Decision

VoiceQuestSourceV2 remains independent of Unity AudioSource objects and room routing. It sends stable NPC audio context through the typed transport. LiveKitService owns remote-track binding, route lookup, stream rebind, and teardown.

The route identity is data (npc_binding_id or an equivalent stable audio_route_id), never a serialized AudioSource reference in a graph asset or packet. Epic 3 only supplies this identity if advanced lesson authoring needs to select an NPC; runtime routing remains Epic 2.

## Acceptance criteria for Story 2.1

**Given** a V2 dialogue or voice activation includes a valid NPC audio-route identity  
**When** a remote audio track is subscribed  
**Then** LiveKitService binds it to the matching scene AudioSource without requiring VoiceQuestSourceV2 to know Unity audio objects.

**Given** a track arrives before its AudioSource is registered  
**When** the route is registered later  
**Then** the pending track is bound exactly once; pending tracks are keyed by track identity and do not overwrite one another.

**Given** an AudioSource is registered before its track arrives  
**When** the matching track is subscribed  
**Then** playback starts on that source.

**Given** two active NPC routes exist  
**When** both tracks publish audio  
**Then** each stream remains on its own source and spatial route; no global-source cross-talk occurs.

**Given** a route source changes, an NPC despawns, or a scene unloads  
**When** rebinding or cleanup runs  
**Then** active streams are safely rebound or disposed, no stream writes to a destroyed Unity object, and teardown is idempotent.

**Given** a track references an unknown or stale route  
**When** the service receives it  
**Then** it rejects the binding safely and emits an observable reason without affecting another NPC stream.

**Given** automated integration tests run  
**When** track-before-source, source-before-track, multi-NPC routing, source swap, unsubscribe, despawn, and scene-unload paths are exercised  
**Then** focused coverage passes.

## Success signal

Story 2.1 integration evidence demonstrates independent per-NPC playback, correct deferred binding, safe source swap/despawn cleanup, and passing focused routing tests.

## Non-goals

- Microphone capture or EnableMicrophone ownership; only LiveKitService owns capture.
- Adding an AudioSource field or room-routing logic to VoiceQuestSourceV2.
- Changing legacy LiveKit or legacy lesson audio behavior.
- Making Epic 3 responsible for runtime audio binding.
