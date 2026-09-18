import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from agent_v2 import JobRuntime, TeacherAgent, _process_v2_packet
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
        SetActiveQuest("activation-1", "Ask for water", ("Water, please",), npc_binding_id="peer-npc")
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
        voice_name="vi-VN-Chirp3-HD-Puck",
        speaking_rate=1.0,
    )
    assert mock_synth.await_count == 1
