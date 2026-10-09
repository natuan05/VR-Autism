import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from agent_v2 import (
    JobRuntime,
    TeacherAgent,
    _play_silence_reminder,
    _process_data_packet,
    _process_v2_packet,
)
from voice_contract_v2 import SetActiveQuest


def _runtime_with_room() -> JobRuntime:
    job_ctx = MagicMock()
    room = MagicMock()
    room.local_participant.publish_data = AsyncMock()
    job_ctx.room = room
    return JobRuntime(job_ctx)


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

    runtime.silence_reminder.activate.assert_called_once_with("activation-1", 3.25)


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
    session.say = AsyncMock(return_value=handle)

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
    session.say = AsyncMock(return_value=handle)

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
async def test_legacy_on_reminder_packet_keeps_the_existing_hint_behavior() -> None:
    runtime = _runtime_with_room()
    session = MagicMock()
    with patch("agent_v2._handle_hint_reminder", new=AsyncMock()) as reminder:
        await _process_data_packet(
            MagicMock(),
            session,
            runtime,
            "legacy-topic",
            '{"event":"ON_REMINDER"}',
        )

    reminder.assert_awaited_once_with(session, runtime, "ON_REMINDER")


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
    session.say = AsyncMock()

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
