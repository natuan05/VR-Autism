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
    PacketValidationError,
    SpeakScriptDoneV2,
    SpeakScriptV2,
    parse_unity_packet,
    speak_script_done_packet,
)


def _runtime_with_room(identity: str = "npc-1") -> JobRuntime:
    job_ctx = MagicMock()
    room = MagicMock()
    room.local_participant.identity = identity
    room.local_participant.publish_data = AsyncMock()
    job_ctx.room = room
    return JobRuntime(job_ctx)


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
    runtime = _runtime_with_room(identity="agent-single")
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
    runtime = _runtime_with_room(identity="agent-single")
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
    runtime = _runtime_with_room(identity="teacher-npc")
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

