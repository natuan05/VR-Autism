import asyncio
import json
from unittest.mock import AsyncMock, MagicMock

import pytest

from agent_v2 import JobRuntime, VoiceProfile, VoiceProfileRegistry, _process_v2_packet
from voice_command_runtime_v2 import (
    CommandDisposition,
    CommandStatus,
    VoiceCommandRuntime,
)
from voice_contract_v2 import (
    CancelActiveQuest,
    PacketValidationError,
    SetActiveQuest,
    SpeakScriptDoneV2,
    SpeakScriptV2,
    parse_unity_packet,
    speak_script_done_packet,
)

SYNC_TIMEOUT_SECONDS = 1.0


def _runtime_with_room(
    identity: str = "teacher-npc",
    *,
    activation_id: str = "act-1",
    npc_binding_id: str = "teacher-npc",
) -> JobRuntime:
    job_ctx = MagicMock()
    room = MagicMock()
    room.local_participant.identity = identity
    room.local_participant.publish_data = AsyncMock()
    job_ctx.room = room
    runtime = JobRuntime(job_ctx)
    runtime.voice_runtime.activate(
        SetActiveQuest(activation_id, "Ask for help", ("Please",), npc_binding_id)
    )
    return runtime


def test_parse_speak_script_v2_valid() -> None:
    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "npc-1",
            "text": "Hello world!",
        }
    )
    parsed = parse_unity_packet(payload)
    assert isinstance(parsed, SpeakScriptV2)
    assert parsed.activation_id == "act-1"
    assert parsed.sequence_id == "seq-1"
    assert parsed.npc_binding_id == "npc-1"
    assert parsed.text == "Hello world!"


def test_parse_speak_script_v2_invalid() -> None:
    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            # missing sequence_id
            "activation_id": "act-1",
            "npc_binding_id": "npc-1",
            "text": "Hello world!",
        }
    )
    with pytest.raises(PacketValidationError):
        parse_unity_packet(payload)


def test_speak_script_done_packet_serialization() -> None:
    done = SpeakScriptDoneV2(
        activation_id="act-1",
        sequence_id="seq-1",
        npc_binding_id="npc-1",
        status="SUCCESS",
    )
    packet_dict = speak_script_done_packet(
        done.activation_id,
        done.sequence_id,
        done.npc_binding_id,
        done.status,
    )
    assert packet_dict == {
        "contract_version": 2,
        "event": "SPEAK_SCRIPT_DONE",
        "activation_id": "act-1",
        "sequence_id": "seq-1",
        "npc_binding_id": "npc-1",
        "status": "SUCCESS",
    }


def test_voice_command_runtime_lifecycle() -> None:
    cmd = VoiceCommandRuntime()
    req = SpeakScriptV2("act-1", "seq-1", "npc-1", "Hello")
    assert cmd.submit(req) == CommandDisposition.NEW
    assert cmd.start("act-1", "seq-1") is True

    # duplicate while active returns REPLAY
    assert cmd.submit(req) == CommandDisposition.REPLAY

    assert cmd.mark_completed("act-1", "seq-1", "SUCCESS") is True
    assert cmd.status("act-1", "seq-1") == CommandStatus.COMPLETED
    assert cmd.submit(req) == CommandDisposition.REPLAY


def test_voice_command_runtime_cancellation() -> None:
    cmd = VoiceCommandRuntime()
    req = SpeakScriptV2("act-1", "seq-1", "npc-1", "Hello")
    assert cmd.submit(req) == CommandDisposition.NEW
    assert cmd.cancel_active(reason="interrupted") is True
    assert cmd.status("act-1", "seq-1") == CommandStatus.CANCELLED
    assert cmd.active_command is None


def test_voice_command_runtime_cancels_only_exact_activation_sequence_and_npc() -> None:
    cmd = VoiceCommandRuntime()
    req = SpeakScriptV2("act-1", "seq-1", "npc-1", "Hello")
    assert cmd.submit(req) == CommandDisposition.NEW
    assert cmd.start("act-1", "seq-1") is True

    assert cmd.cancel("act-old", "seq-1", "npc-1", "lesson_scope_changed") is False
    assert cmd.cancel("act-1", "seq-old", "npc-1", "lesson_scope_changed") is False
    assert cmd.cancel("act-1", "seq-1", "npc-other", "lesson_scope_changed") is False
    assert cmd.active_command is not None

    assert cmd.cancel("act-1", "seq-1", "npc-1", "lesson_scope_changed") is True
    assert cmd.status("act-1", "seq-1") == CommandStatus.CANCELLED
    assert cmd.active_command is None


@pytest.mark.asyncio
async def test_cancel_speak_script_packet_interrupts_only_the_matching_active_playout() -> (
    None
):
    runtime = _runtime_with_room(identity="teacher-npc")
    session = MagicMock()
    started = asyncio.Event()
    released = asyncio.Event()
    handle = MagicMock()
    handle.interrupted = False
    handle.done.return_value = False

    def interrupt() -> None:
        handle.interrupted = True
        released.set()

    handle.interrupt.side_effect = interrupt

    async def wait_for_playout() -> None:
        started.set()
        await asyncio.wait_for(released.wait(), timeout=SYNC_TIMEOUT_SECONDS)

    handle.wait_for_playout = AsyncMock(side_effect=wait_for_playout)
    session.say = AsyncMock(return_value=handle)
    script = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "text": "This must stop on node change.",
        }
    )

    script_task = asyncio.create_task(
        _process_v2_packet(MagicMock(), session, runtime, script)
    )
    await asyncio.wait_for(started.wait(), timeout=SYNC_TIMEOUT_SECONDS)
    assert runtime.command_runtime.active_sequence_id == "seq-1"

    cancel = json.dumps(
        {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
        }
    )
    await _process_v2_packet(MagicMock(), session, runtime, cancel)
    await asyncio.wait_for(script_task, timeout=SYNC_TIMEOUT_SECONDS)

    handle.interrupt.assert_called_once_with()
    messages = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages == [
        {
            "contract_version": 2,
            "event": "SPEAK_SCRIPT_DONE",
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "status": "CANCELLED",
            "reason": "lesson_scope_changed",
        }
    ]
    assert runtime.voice_runtime.status("act-1").name == "ACTIVE"


@pytest.mark.asyncio
async def test_cancel_during_session_say_start_interrupts_the_late_handle() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    session = MagicMock()
    say_started = asyncio.Event()
    finish_say = asyncio.Event()
    handle = MagicMock()
    handle.done.return_value = False
    handle.wait_for_playout = AsyncMock()
    handle.interrupted = True
    handle.interrupt = MagicMock()

    async def delayed_say(*_args, **_kwargs):
        say_started.set()
        await asyncio.wait_for(finish_say.wait(), timeout=SYNC_TIMEOUT_SECONDS)
        return handle

    session.say = AsyncMock(side_effect=delayed_say)
    script = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "text": "Cancel before the speech handle is ready.",
        }
    )
    script_task = asyncio.create_task(
        _process_v2_packet(MagicMock(), session, runtime, script)
    )
    await asyncio.wait_for(say_started.wait(), timeout=SYNC_TIMEOUT_SECONDS)

    cancel = json.dumps(
        {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
        }
    )
    await _process_v2_packet(MagicMock(), session, runtime, cancel)
    finish_say.set()
    await asyncio.wait_for(script_task, timeout=SYNC_TIMEOUT_SECONDS)

    handle.interrupt.assert_called_once_with()
    handle.wait_for_playout.assert_not_awaited()
    assert runtime.command_runtime.active_command is None
    assert runtime.voice_runtime.status("act-1").name == "ACTIVE"
    assert runtime.job_ctx.room.local_participant.publish_data.await_count == 1


@pytest.mark.asyncio
async def test_stale_cancel_speak_script_packet_cannot_interrupt_the_current_script() -> (
    None
):
    runtime = _runtime_with_room(identity="teacher-npc")
    session = MagicMock()
    handle = MagicMock()
    handle.interrupted = False
    handle.done.return_value = False
    assert (
        runtime.command_runtime.submit(
            SpeakScriptV2("act-2", "seq-2", "teacher-npc", "The current script.")
        )
        == CommandDisposition.NEW
    )
    assert runtime.command_runtime.start("act-2", "seq-2") is True
    runtime.active_speech_handle = handle

    stale_cancel = json.dumps(
        {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
        }
    )
    await _process_v2_packet(MagicMock(), session, runtime, stale_cancel)

    handle.interrupt.assert_not_called()
    assert runtime.command_runtime.active_sequence_id == "seq-2"
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()


@pytest.mark.asyncio
async def test_handle_speak_script_v2_success() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    agent = MagicMock()
    session = MagicMock()

    mock_speech_handle = MagicMock()
    mock_speech_handle.interrupted = False
    mock_speech_handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=mock_speech_handle)

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "text": "Hello, welcome to the lesson!",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)

    session.say.assert_awaited_once_with(
        "Hello, welcome to the lesson!",
        allow_interruptions=True,
    )
    mock_speech_handle.wait_for_playout.assert_awaited_once()

    calls = runtime.job_ctx.room.local_participant.publish_data.call_args_list
    assert len(calls) == 1
    msg = json.loads(calls[0].args[0].decode("utf-8"))
    assert msg == {
        "contract_version": 2,
        "event": "SPEAK_SCRIPT_DONE",
        "activation_id": "act-1",
        "sequence_id": "seq-1",
        "npc_binding_id": "teacher-npc",
        "status": "SUCCESS",
    }


@pytest.mark.asyncio
async def test_standalone_dialogue_script_speaks_without_a_voice_quest() -> None:
    job_ctx = MagicMock()
    job_ctx.room.local_participant.identity = "teacher-npc"
    job_ctx.room.local_participant.publish_data = AsyncMock()
    runtime = JobRuntime(job_ctx)
    session = MagicMock()
    handle = MagicMock()
    handle.interrupted = False
    handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=handle)
    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "text": "Wash your hands with soap.",
        }
    )

    await _process_v2_packet(MagicMock(), session, runtime, payload)

    session.say.assert_awaited_once_with(
        "Wash your hands with soap.", allow_interruptions=True
    )
    handle.wait_for_playout.assert_awaited_once()
    messages = [
        json.loads(call.args[0].decode("utf-8"))
        for call in job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages == [
        {
            "contract_version": 2,
            "event": "SPEAK_SCRIPT_DONE",
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "status": "SUCCESS",
        }
    ]
    assert runtime.voice_runtime.active_activation_id is None
    assert runtime.matched_sent == set()


@pytest.mark.asyncio
async def test_dialogue_script_is_independent_of_a_cancelled_voice_activation() -> None:
    runtime = _runtime_with_room(
        identity="teacher-npc", activation_id="voice-first-win"
    )
    assert runtime.voice_runtime.cancel(
        CancelActiveQuest("voice-first-win", "first_win")
    )
    session = MagicMock()
    handle = MagicMock()
    handle.interrupted = False
    handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=handle)
    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "soap-dispense",
            "npc_binding_id": "teacher-npc",
            "text": "Dispense soap.",
        }
    )

    await _process_v2_packet(MagicMock(), session, runtime, payload)

    session.say.assert_awaited_once_with("Dispense soap.", allow_interruptions=True)
    handle.wait_for_playout.assert_awaited_once()
    assert runtime.voice_runtime.active_activation_id == "voice-first-win"
    assert runtime.voice_runtime.status("voice-first-win").name == "CANCELLED"
    assert runtime.matched_sent == set()


@pytest.mark.asyncio
async def test_cancel_before_delayed_script_tombstones_exact_dialogue_scope() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    session = MagicMock()
    session.say = AsyncMock()
    cancel = json.dumps(
        {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
        }
    )
    delayed_script = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "text": "This must stay cancelled.",
        }
    )

    await _process_v2_packet(MagicMock(), session, runtime, cancel)
    await _process_v2_packet(MagicMock(), session, runtime, delayed_script)

    session.say.assert_not_awaited()
    assert runtime.command_runtime.active_command is None
    messages = [
        json.loads(call.args[0].decode("utf-8"))
        for call in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages == [
        {
            "contract_version": 2,
            "event": "SPEAK_SCRIPT_DONE",
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "status": "CANCELLED",
            "reason": "lesson_scope_changed",
        }
    ]


@pytest.mark.asyncio
async def test_cancel_tombstone_does_not_authorize_a_different_npc() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    session = MagicMock()
    session.say = AsyncMock()
    cancel = json.dumps(
        {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
        }
    )
    wrong_npc_script = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "dialogue-activation",
            "sequence_id": "dialogue-node-1",
            "npc_binding_id": "peer-npc",
            "text": "This has a different NPC owner.",
        }
    )

    await _process_v2_packet(MagicMock(), session, runtime, cancel)
    await _process_v2_packet(MagicMock(), session, runtime, wrong_npc_script)

    session.say.assert_not_awaited()
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()
    assert runtime.command_runtime.active_command is None


def test_voice_profile_registry() -> None:
    registry = VoiceProfileRegistry()
    profile, is_fallback = registry.get("teacher-npc")
    assert not is_fallback
    assert profile.voice_name == "vi-VN-Chirp3-HD-Aoede"

    profile, is_fallback = registry.get("peer-npc")
    assert not is_fallback
    assert profile.voice_name == "vi-VN-Chirp3-HD-Puck"

    # Unknown NPC falls back to default
    profile, is_fallback = registry.get("unknown-npc")
    assert is_fallback
    assert profile.voice_name == "vi-VN-Chirp3-HD-Aoede"

    # Custom registration
    custom = VoiceProfile(voice_name="custom-voice", speaking_rate=1.2)
    registry.register("custom-npc", custom)
    profile, is_fallback = registry.get("custom-npc")
    assert not is_fallback
    assert profile.voice_name == "custom-voice"
    assert profile.speaking_rate == 1.2


@pytest.mark.asyncio
async def test_handle_speak_script_v2_selects_voice_profile() -> None:
    runtime = _runtime_with_room(identity="agent-single", npc_binding_id="peer-npc")
    agent = MagicMock()
    session = MagicMock()
    session.tts.update_options = MagicMock()

    mock_speech_handle = MagicMock()
    mock_speech_handle.interrupted = False
    mock_speech_handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=mock_speech_handle)

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "peer-npc",
            "text": "Hello friend!",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)

    session.tts.update_options.assert_called_once_with(
        language="vi-VN",
        voice_name="vi-VN-Chirp3-HD-Puck",
        speaking_rate=1.0,
    )
    session.say.assert_awaited_once_with(
        "Hello friend!",
        allow_interruptions=True,
    )
    mock_speech_handle.wait_for_playout.assert_awaited_once()


@pytest.mark.asyncio
async def test_handle_speak_script_v2_unknown_npc_falls_back_to_default() -> None:
    runtime = _runtime_with_room(
        identity="agent-single", npc_binding_id="nonexistent-npc"
    )
    agent = MagicMock()
    session = MagicMock()
    session.tts.update_options = MagicMock()

    mock_speech_handle = MagicMock()
    mock_speech_handle.interrupted = False
    mock_speech_handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=mock_speech_handle)

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "nonexistent-npc",
            "text": "Fallback greeting!",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)

    session.tts.update_options.assert_called_once_with(
        language="vi-VN",
        voice_name="vi-VN-Chirp3-HD-Aoede",
        speaking_rate=1.0,
    )
    session.say.assert_awaited_once_with(
        "Fallback greeting!",
        allow_interruptions=True,
    )
    calls = runtime.job_ctx.room.local_participant.publish_data.call_args_list
    assert len(calls) == 1
    msg = json.loads(calls[0].args[0].decode("utf-8"))
    assert msg["status"] == "SUCCESS"


@pytest.mark.asyncio
async def test_handle_speak_script_v2_interrupted_emits_cancelled() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    agent = MagicMock()
    session = MagicMock()

    mock_speech_handle = MagicMock()
    mock_speech_handle.interrupted = True
    mock_speech_handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=mock_speech_handle)

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "text": "Hello!",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)

    calls = runtime.job_ctx.room.local_participant.publish_data.call_args_list
    assert len(calls) == 1
    msg = json.loads(calls[0].args[0].decode("utf-8"))
    assert msg["status"] == "CANCELLED"


@pytest.mark.asyncio
async def test_handle_speak_script_v2_replay_uses_cached_result() -> None:
    runtime = _runtime_with_room(identity="teacher-npc")
    agent = MagicMock()
    session = MagicMock()

    mock_speech_handle = MagicMock()
    mock_speech_handle.interrupted = False
    mock_speech_handle.wait_for_playout = AsyncMock()
    session.say = AsyncMock(return_value=mock_speech_handle)

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "seq-1",
            "npc_binding_id": "teacher-npc",
            "text": "Hello once!",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)
    await _process_v2_packet(agent, session, runtime, payload)

    # session.say must be awaited only ONCE
    session.say.assert_awaited_once_with(
        "Hello once!",
        allow_interruptions=True,
    )

    calls = runtime.job_ctx.room.local_participant.publish_data.call_args_list
    assert len(calls) == 2
    msg1 = json.loads(calls[0].args[0].decode("utf-8"))
    msg2 = json.loads(calls[1].args[0].decode("utf-8"))
    assert msg1["status"] == "SUCCESS"
    assert msg2["status"] == "SUCCESS"


@pytest.mark.asyncio
async def test_handle_speak_script_v2_exception_emits_failed() -> None:
    runtime = _runtime_with_room(identity="teacher-npc", activation_id="act-fail")
    agent = MagicMock()
    session = MagicMock()

    session.say = AsyncMock(side_effect=RuntimeError("TTS engine failure"))

    payload = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-fail",
            "sequence_id": "seq-fail",
            "npc_binding_id": "teacher-npc",
            "text": "Failing speech",
        }
    )

    await _process_v2_packet(agent, session, runtime, payload)

    calls = runtime.job_ctx.room.local_participant.publish_data.call_args_list
    assert len(calls) == 1
    msg = json.loads(calls[0].args[0].decode("utf-8"))
    assert msg["status"] == "FAILED"
    assert "TTS engine failure" in msg["reason"]


@pytest.mark.asyncio
async def test_delayed_script_from_previous_activation_is_rejected_without_playout() -> (
    None
):
    runtime = _runtime_with_room(identity="teacher-npc", activation_id="act-1")
    runtime.voice_runtime.activate(
        SetActiveQuest("act-2", "Current quest", ("Please help.",), "teacher-npc")
    )
    session = MagicMock()
    session.say = AsyncMock()
    stale = json.dumps(
        {
            "event": "SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "act-1",
            "sequence_id": "old-seq",
            "npc_binding_id": "teacher-npc",
            "text": "This belongs to the previous lesson run.",
        }
    )

    await _process_v2_packet(MagicMock(), session, runtime, stale)

    session.say.assert_not_awaited()
    assert runtime.command_runtime.active_command is None
    runtime.job_ctx.room.local_participant.publish_data.assert_not_awaited()
