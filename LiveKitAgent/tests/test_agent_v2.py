import asyncio
import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from livekit.agents.voice.speech_handle import SpeechHandle

from agent_v2 import (
    JobRuntime,
    TeacherAgent,
    _fixed_stream_activation_state,
    _handle_fixed_audio_stream_request,
    _handle_quest_activation,
    _play_silence_reminder,
    _process_cache_packet,
    _process_data_packet,
    _process_v2_packet,
    _reset_activation,
)
from fixed_audio_runtime_v1 import FixedAudioRuntimeV1
from silence_reminder_v2 import ActivationSilenceReminderV2
from tts_cache_client_v1 import SessionScopeV1
from tts_cache_contract_v1 import compute_key, default_config
from voice_contract_v2 import SetActiveQuest


@pytest.mark.asyncio
async def test_disabled_reminder_preference_reaches_timer_and_is_replay_stable() -> (
    None
):
    runtime = _runtime_with_room()
    runtime.silence_reminder = MagicMock()
    agent = MagicMock(spec=TeacherAgent)
    packet = {
        "event": "SET_ACTIVE_QUEST",
        "contract_version": 2,
        "activation_id": "disabled",
        "quest_goal": "Ask",
        "phrases": ["Water"],
        "speech_silence_timeout_seconds": 15,
        "enable_speech_silence_reminder": False,
    }
    with patch("agent_v2._handle_quest_activation", new=AsyncMock()):
        await _process_v2_packet(agent, MagicMock(), runtime, json.dumps(packet))
        await _process_v2_packet(agent, MagicMock(), runtime, json.dumps(packet))
    runtime.silence_reminder.activate.assert_called_once_with(
        "disabled", 15, enabled=False
    )
    packet["enable_speech_silence_reminder"] = True
    with patch("agent_v2._handle_quest_activation", new=AsyncMock()):
        await _process_v2_packet(agent, MagicMock(), runtime, json.dumps(packet))
    assert runtime.silence_reminder.activate.call_count == 1


def _runtime_with_room() -> JobRuntime:
    job_ctx = MagicMock()
    room = MagicMock()
    room.local_participant.publish_data = AsyncMock()
    job_ctx.room = room
    return JobRuntime(job_ctx)


@pytest.mark.asyncio
@pytest.mark.parametrize("reconcile_first", [True, False])
async def test_reconciled_opening_proof_unblocks_reminder_after_manual_terminal(
    reconcile_first: bool,
):
    runtime = _runtime_with_room()
    config = default_config()
    scope = SessionScopeV1("room-1", "1234", "child-1", "lesson-1", "host-1", config)
    fixed = FixedAudioRuntimeV1.from_scope(scope)
    runtime.cache_scope = scope
    runtime.cache_fixed_audio = fixed
    runtime.cache_ready = asyncio.Event()
    timer = ActivationSilenceReminderV2(AsyncMock(return_value=True))
    runtime.silence_reminder = timer
    agent = MagicMock(spec=TeacherAgent)
    session = MagicMock()
    activation = {
        "event": "SET_ACTIVE_QUEST",
        "contract_version": 2,
        "activation_id": "activation-1",
        "quest_goal": "Ask for water",
        "phrases": ["Water, please"],
        "npc_binding_id": "teacher-npc",
        "speech_silence_timeout_seconds": 30,
    }
    latest_activity = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_ACTIVITY",
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "activation-1",
        "invocation_id": "manual-hint-terminal",
        "npc_binding_id": "teacher-npc",
        "kind": "manual_hint",
        "owner_revision": 2,
        "activity_sequence": 3,
        "phase": "terminal",
        "source": "local",
        "status": "SUCCESS",
        "reason": "completed",
    }
    reconcile = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_RECONCILE",
        "session_id": "room-1",
        "run_id": "run-1",
        "agent_instance_id": fixed.agent_instance_id,
        "active_activation_ids": ["activation-1"],
        "current_activity": latest_activity,
        "completed_opening_activation_ids": ["activation-1"],
    }

    async def activate_quest():
        await _process_v2_packet(agent, session, runtime, json.dumps(activation))

    async def reconcile_runtime():
        await _process_cache_packet(session, runtime, json.dumps(reconcile))

    with patch("agent_v2._handle_quest_activation", new=AsyncMock()):
        if reconcile_first:
            await reconcile_runtime()
            assert fixed.completed_opening_activation_ids == {"activation-1"}
            await activate_quest()
        else:
            await activate_quest()
            assert not timer._opening_finished
            await reconcile_runtime()

    assert timer._opening_finished
    assert fixed.current_activity == latest_activity
    timer.dispose()


def test_cache_fixed_audio_accepts_dialogue_outside_voice_quest_and_rejects_stale_cancel():
    runtime = _runtime_with_room()
    fixed = FixedAudioRuntimeV1("room-1")
    runtime.cache_fixed_audio = fixed
    dialogue = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "node-activation",
        "invocation_id": "dialogue-invocation",
        "npc_binding_id": "teacher-npc",
        "kind": "dialogue",
        "owner_revision": 9,
    }
    fixed.active_activation_ids.add("node-activation")
    assert runtime.voice_runtime.active_activation_id is None
    assert _fixed_stream_activation_state(runtime, dialogue) == "active"
    assert (
        _fixed_stream_activation_state(
            runtime, {**dialogue, "activation_id": "stale-activation"}
        )
        == "stale"
    )

    fixed.cancelled_invocations[fixed.invocation_key(dialogue)] = 9
    assert _fixed_stream_activation_state(runtime, dialogue) == "active"


@pytest.mark.asyncio
async def test_opening_request_cross_topic_wait_and_prestart_cancel_emits_terminal():
    runtime = _runtime_with_room()
    config = default_config()
    profile = config.profile("teacher-npc")
    fixed = FixedAudioRuntimeV1("room-1")
    fixed.run_id = "run-1"
    fixed.active_activation_ids.add("activation-1")
    runtime.cache_fixed_audio = fixed
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    runtime.cache_ready = asyncio.Event()
    runtime.cache_ready.set()
    runtime.cache_packet_lock = asyncio.Lock()
    request = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "activation-1",
        "invocation_id": "opening-invocation",
        "npc_binding_id": "teacher-npc",
        "kind": "opening",
        "owner_revision": 9,
        "profile_id": profile.profile_id,
        "text": "Xin chào.",
        "key": compute_key("Xin chào.", profile),
        "rejection_epoch": 0,
    }
    task = asyncio.create_task(
        _handle_fixed_audio_stream_request(None, runtime, request)
    )
    await asyncio.sleep(0)
    assert not task.done()

    cancel = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_CANCEL",
        **{
            name: request[name]
            for name in (
                "session_id",
                "run_id",
                "activation_id",
                "invocation_id",
                "npc_binding_id",
                "kind",
            )
        },
        "owner_revision": 9,
        "reason": "skip",
    }
    await _process_cache_packet(None, runtime, cancel)
    await task

    packets = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert any(
        packet.get("event") == "FIXED_AUDIO_DONE"
        and packet.get("status") == "CANCELLED"
        and packet.get("invocation_id") == request["invocation_id"]
        for packet in packets
    )


@pytest.mark.asyncio
async def test_current_dialogue_request_with_invalid_profile_emits_failed_terminal():
    runtime = _runtime_with_room()
    config = default_config()
    fixed = FixedAudioRuntimeV1("room-1")
    fixed.active_activation_ids.add("node-activation")
    runtime.cache_fixed_audio = fixed
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    request = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "node-activation",
        "invocation_id": "bad-profile-invocation",
        "npc_binding_id": "teacher-npc",
        "kind": "dialogue",
        "owner_revision": 1,
        "profile_id": "missing-profile",
        "text": "Please continue.",
        "key": "0" * 64,
        "rejection_epoch": 0,
    }

    await _handle_fixed_audio_stream_request(None, runtime, request)

    packets = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert any(
        packet.get("event") == "FIXED_AUDIO_DONE"
        and packet.get("status") == "FAILED"
        and packet.get("invocation_id") == request["invocation_id"]
        for packet in packets
    )


@pytest.mark.asyncio
async def test_dialogue_playout_remains_current_without_voice_quest_runtime():
    from agent_v2 import SpeechOutcome

    runtime = _runtime_with_room()
    config = default_config()
    profile = config.profile("teacher-npc")
    fixed = FixedAudioRuntimeV1("room-1")
    fixed.run_id = "run-1"
    fixed.active_activation_ids.add("node-activation")
    runtime.cache_fixed_audio = fixed
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    text = "The dialogue node asks for water."
    request = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "node-activation",
        "invocation_id": "dialogue-success",
        "npc_binding_id": "teacher-npc",
        "kind": "dialogue",
        "owner_revision": 1,
        "profile_id": profile.profile_id,
        "text": text,
        "key": compute_key(text, profile),
        "rejection_epoch": 0,
    }

    async def finish(_handle, is_current):
        return SpeechOutcome.COMPLETED if is_current() else SpeechOutcome.CANCELLED

    with (
        patch("agent_v2.start_captured_owned_speech", return_value=object()),
        patch("agent_v2.finish_owned_speech", side_effect=finish),
    ):
        await _handle_fixed_audio_stream_request(None, runtime, request)

    packets = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert any(
        packet.get("event") == "FIXED_AUDIO_DONE"
        and packet.get("status") == "SUCCESS"
        and packet.get("invocation_id") == request["invocation_id"]
        for packet in packets
    )


@pytest.mark.asyncio
async def test_late_lower_revision_does_not_cancel_current_fixed_audio_owner():
    runtime = _runtime_with_room()
    config = default_config()
    profile = config.profile("teacher-npc")
    fixed = FixedAudioRuntimeV1("room-1")
    fixed.active_activation_ids.add("activation-1")
    runtime.cache_fixed_audio = fixed
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    runtime.voice_runtime.activate(
        SetActiveQuest("activation-1", "goal", ("Hello",), npc_binding_id="teacher-npc")
    )
    current = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "activation-1",
        "invocation_id": "current-invocation",
        "npc_binding_id": "teacher-npc",
        "kind": "dialogue",
        "owner_revision": 2,
    }
    fixed.current_invocation = current
    fixed.owner_revision = 2
    owner_gate = asyncio.Event()
    current_task = asyncio.create_task(owner_gate.wait())
    fixed.current_task = current_task
    late = {
        **current,
        "invocation_id": "late-old-invocation",
        "owner_revision": 1,
        "profile_id": profile.profile_id,
        "text": "Late text.",
        "key": compute_key("Late text.", profile),
        "rejection_epoch": 0,
    }

    await _handle_fixed_audio_stream_request(None, runtime, late)

    assert not current_task.cancelled()
    assert fixed.current_task is current_task
    assert fixed.current_invocation is current
    assert fixed.owner_revision == 2
    assert not runtime.job_ctx.room.local_participant.publish_data.await_args_list
    current_task.cancel()
    await asyncio.gather(current_task, return_exceptions=True)


@pytest.mark.asyncio
async def test_activation_change_during_owner_cancellation_prevents_new_stream_start():
    runtime = _runtime_with_room()
    config = default_config()
    profile = config.profile("teacher-npc")
    fixed = FixedAudioRuntimeV1("room-1")
    fixed.run_id = "run-1"
    fixed.active_activation_ids.add("activation-1")
    runtime.cache_fixed_audio = fixed
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    current = {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "activation-1",
        "invocation_id": "current-owner",
        "npc_binding_id": "teacher-npc",
        "kind": "dialogue",
        "owner_revision": 2,
    }
    fixed.current_invocation = current
    fixed.owner_revision = 2

    old_owner_started = asyncio.Event()

    async def old_owner():
        old_owner_started.set()
        try:
            await asyncio.Event().wait()
        except asyncio.CancelledError:
            fixed.active_activation_ids.discard("activation-1")
            raise

    previous_task = asyncio.create_task(old_owner())
    await old_owner_started.wait()
    fixed.current_task = previous_task
    text = "Replacement dialogue."
    replacement = {
        **current,
        "invocation_id": "replacement-owner",
        "owner_revision": 3,
        "profile_id": profile.profile_id,
        "text": text,
        "key": compute_key(text, profile),
        "rejection_epoch": 0,
    }

    with patch("agent_v2.start_captured_owned_speech") as start_speech:
        await _handle_fixed_audio_stream_request(None, runtime, replacement)

    start_speech.assert_not_called()
    assert fixed.current_invocation is current
    assert fixed.owner_revision == 2
    assert fixed.current_task is previous_task


@pytest.mark.asyncio
async def test_same_activation_replay_acknowledges_without_repeating_opening() -> None:
    """A reconnect SET packet publishes ACTIVE but calls the opening flow only once."""
    runtime = _runtime_with_room()
    agent = MagicMock(spec=TeacherAgent)
    session = MagicMock()
    payload = (
        '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-1","quest_goal":"Ask for water",'
        '"phrases":["Water, please"]}'
    )

    with patch("agent_v2._handle_quest_activation", new=AsyncMock()) as opening:
        await _process_v2_packet(agent, session, runtime, payload)
        await _process_v2_packet(agent, session, runtime, payload)

    assert opening.await_count == 1
    messages = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages == [
        {
            "event": "QUEST_STATUS",
            "contract_version": 2,
            "activation_id": "activation-1",
            "status": "ACTIVE",
        },
        {
            "event": "QUEST_STATUS",
            "contract_version": 2,
            "activation_id": "activation-1",
            "status": "ACTIVE",
        },
    ]


@pytest.mark.asyncio
async def test_replayed_activation_does_not_reconfigure_reminder_timer() -> None:
    runtime = _runtime_with_room()
    runtime.silence_reminder = MagicMock()
    agent = MagicMock(spec=TeacherAgent)
    session = MagicMock()
    payload = (
        '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-1","quest_goal":"Ask for water",'
        '"phrases":["Water, please"],"speech_silence_timeout_seconds":3.25}'
    )

    with patch("agent_v2._handle_quest_activation", new=AsyncMock()):
        await _process_v2_packet(agent, session, runtime, payload)
        await _process_v2_packet(agent, session, runtime, payload)

    runtime.silence_reminder.activate.assert_called_once_with(
        "activation-1", 3.25, enabled=True
    )


@pytest.mark.asyncio
async def test_silence_reminder_sends_correlated_evidence_only_after_playout() -> None:
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest(
            "activation-1",
            "Ask for water",
            ("Water, please",),
            npc_binding_id="teacher-npc",
        )
    )
    runtime.tts_cache[
        ("teacher-npc", "vi-VN-Chirp3-HD-Aoede", 1.0, "Water, please")
    ] = []
    session = MagicMock()
    handle = MagicMock()
    handle.interrupted = False
    handle.wait_for_playout = AsyncMock()
    session.say = MagicMock(return_value=handle)

    with patch("agent_v2.random.choice", return_value="Water, please"):
        played = await _play_silence_reminder(session, runtime, "activation-1")

    assert played is True
    handle.wait_for_playout.assert_awaited_once()
    call = runtime.job_ctx.room.local_participant.publish_data.await_args
    packet = json.loads(call.args[0].decode("utf-8"))
    assert packet == {
        "contract_version": 2,
        "event": "ON_REMINDER",
        "direction": "agent_to_unity",
        "result": "accepted",
        "activation_id": "activation-1",
        "npc_binding_id": "teacher-npc",
        "command_id": packet["command_id"],
    }
    assert packet["command_id"]
    assert call.kwargs == {"reliable": True, "topic": "lesson-graph-v2.voice"}


@pytest.mark.asyncio
async def test_silence_reminder_does_not_publish_after_activation_changes() -> None:
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest(
            "activation-1",
            "Ask for water",
            ("Water, please",),
            npc_binding_id="teacher-npc",
        )
    )
    handle = MagicMock()
    handle.interrupted = False
    handle.wait_for_playout = AsyncMock()
    session = MagicMock()
    session.say = MagicMock(return_value=handle)

    async def replace_during_playout() -> None:
        runtime.voice_runtime.activate(
            SetActiveQuest(
                "activation-2",
                "Wash hands",
                ("Wash your hands",),
                npc_binding_id="teacher-npc",
            )
        )

    handle.wait_for_playout.side_effect = replace_during_playout
    with patch("agent_v2.random.choice", return_value="Water, please"):
        played = await _play_silence_reminder(session, runtime, "activation-1")

    assert played is False
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()


@pytest.mark.asyncio
async def test_silence_reminder_cancellation_interrupts_its_owned_sdk_handle() -> None:
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest(
            "activation-1",
            "Ask for water",
            ("Water, please",),
            npc_binding_id="teacher-npc",
        )
    )
    handle = SpeechHandle.create()
    session = MagicMock()
    session.say = MagicMock(return_value=handle)
    runtime.silence_reminder = MagicMock()
    runtime.silence_reminder.is_reminder_playing = True

    task = asyncio.create_task(_play_silence_reminder(session, runtime, "activation-1"))
    await asyncio.sleep(0)
    assert runtime.active_silence_reminder_handle is handle
    runtime.interrupt_active_silence_reminder()
    assert handle.interrupted
    handle._mark_done()

    assert await asyncio.wait_for(task, timeout=1) is False
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()


@pytest.mark.asyncio
async def test_activation_reset_interrupts_owned_opening_handle() -> None:
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest(
            "activation-1",
            "Ask for water",
            ("Water, please",),
            npc_binding_id="teacher-npc",
        )
    )
    handle = SpeechHandle.create()
    speech_started = asyncio.Event()
    agent = MagicMock(spec=TeacherAgent)
    session = MagicMock()
    session.tts = MagicMock()

    def say_opening(*_args, **_kwargs):
        speech_started.set()
        return handle

    session.say = MagicMock(side_effect=say_opening)
    with patch("agent_v2._synthesize_phrases", new=AsyncMock()):
        task = asyncio.create_task(
            _handle_quest_activation(
                agent,
                session,
                runtime,
                "activation-1",
                "Ask for water",
                [],
            )
        )
        runtime.activation_task = task
        await asyncio.wait_for(speech_started.wait(), timeout=1)
        assert runtime.active_opening_handle is handle

        await _reset_activation(agent, session, runtime)

    assert handle.interrupted
    assert runtime.active_opening_handle is None
    assert task.done()


@pytest.mark.asyncio
@pytest.mark.asyncio
@pytest.mark.parametrize(
    "event",
    [
        "VERBAL_HINT",
        "ON_REMINDER",
        "SET_ACTIVE_QUEST",
        "QUEST_MATCHED",
        "QUEST_STATUS",
    ],
)
async def test_arbitrary_topic_action_events_have_no_speech_or_state_effect(event):
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest("activation-1", "Ask for water", ("Water, please",))
    )
    session = MagicMock()
    runtime.silence_reminder = MagicMock()
    runtime.silence_reminder.is_reminder_playing = True
    runtime.silence_reminder_interrupt = MagicMock()
    payload = json.dumps({"event": event, "text": "untrusted"})
    before_status = runtime.voice_runtime.status("activation-1")

    await _process_data_packet(
        MagicMock(), session, runtime, "arbitrary-topic", payload
    )

    session.say.assert_not_called()
    runtime.silence_reminder_interrupt.assert_not_called()
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()
    assert runtime.voice_runtime.active_activation_id == "activation-1"
    assert runtime.voice_runtime.status("activation-1") == before_status


@pytest.mark.asyncio
async def test_completion_publishes_one_correlated_match_and_terminal_status() -> None:
    """A second LLM tool callback must not publish a second match for the activation."""
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest("activation-1", "Ask for water", ("Water, please",))
    )
    agent = TeacherAgent(runtime)
    agent.bind_evaluation_activation("activation-1")

    await agent.complete_quest(MagicMock())
    await agent.complete_quest(MagicMock())

    messages = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages == [
        {
            "event": "QUEST_MATCHED",
            "contract_version": 2,
            "activation_id": "activation-1",
        },
        {
            "event": "QUEST_STATUS",
            "contract_version": 2,
            "activation_id": "activation-1",
            "status": "MATCHED",
        },
    ]


@pytest.mark.asyncio
async def test_quest_activation_applies_npc_voice_profile() -> None:
    """Activating a quest with npc_binding_id updates TTS options before phrase synthesis."""
    runtime = _runtime_with_room()
    runtime.voice_runtime.activate(
        SetActiveQuest(
            "activation-1",
            "Ask for water",
            ("Water, please",),
            npc_binding_id="peer-npc",
        )
    )
    agent = MagicMock(spec=TeacherAgent)
    session = MagicMock()
    session.tts = MagicMock()
    session.tts.update_options = MagicMock()
    speech_handle = SpeechHandle.create()
    speech_handle._mark_done()
    session.say = MagicMock(return_value=speech_handle)

    with patch("agent_v2._synthesize_phrases", new=AsyncMock()) as mock_synth:
        from agent_v2 import _handle_quest_activation

        await _handle_quest_activation(
            agent,
            session,
            runtime,
            "activation-1",
            "Ask for water",
            ["Water, please"],
            npc_binding_id="peer-npc",
        )

    session.tts.update_options.assert_called_once_with(
        language="vi-VN",
        voice_name="vi-VN-Chirp3-HD-Puck",
        speaking_rate=1.0,
    )
    assert mock_synth.await_count == 1
