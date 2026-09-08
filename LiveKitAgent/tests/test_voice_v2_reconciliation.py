import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from agent_v2 import (
    JobRuntime,
    TeacherAgent,
    _handle_quest_activation,
    _process_v2_packet,
    _publish_activation_status,
)
from voice_contract_v2 import CancelActiveQuest, SetActiveQuest
from voice_quest_runtime_v2 import VoiceQuestRuntime


def test_changed_replay_and_tombstoned_activation_are_rejected():
    runtime = VoiceQuestRuntime()
    first = SetActiveQuest("a", "Ask", ("Please",))
    runtime.activate(first)
    with pytest.raises(ValueError):
        runtime.activate(SetActiveQuest("a", "Different", ("Please",)))
    runtime.activate(SetActiveQuest("b", "Ask", ()))
    with pytest.raises(ValueError):
        runtime.activate(first)


def test_tombstones_are_bounded_and_terminal_state_survives_replay():
    runtime = VoiceQuestRuntime()
    for i in range(70):
        runtime.activate(SetActiveQuest(str(i), "Ask", ()))
    assert runtime.tombstone_count == 64
    assert runtime.mark_matched("69")
    runtime.activate(SetActiveQuest("69", "Ask", ()))
    assert runtime.status("69").name == "MATCHED"


def make_runtime():
    ctx = MagicMock()
    ctx.room.local_participant.publish_data = AsyncMock()
    return JobRuntime(ctx)


def set_packet(activation_id="a"):
    return json.dumps(
        {
            "event": "SET_ACTIVE_QUEST",
            "contract_version": 2,
            "activation_id": activation_id,
            "quest_goal": "Ask",
            "phrases": ["Please"],
        }
    )


@pytest.mark.asyncio
async def test_cancel_reason_is_preserved_in_correlated_status_packet():
    runtime = make_runtime()
    runtime.voice_runtime.activate(SetActiveQuest("a", "Ask", ("Please",)))
    assert runtime.voice_runtime.cancel(CancelActiveQuest("a", "lost_race"))

    await _publish_activation_status(runtime, "a")

    message = json.loads(
        runtime.job_ctx.room.local_participant.publish_data.call_args.args[0]
    )
    assert message["event"] == "QUEST_STATUS"
    assert message["status"] == "CANCELLED"
    assert message["reason"] == "lost_race"


@pytest.mark.asyncio
async def test_activation_setup_failure_marks_failed_and_publishes_status():
    runtime = make_runtime()
    runtime.voice_runtime.activate(SetActiveQuest("a", "Ask", ("Please",)))
    runtime.quest_state.set_active_quest("Ask", ["Please"])
    agent = TeacherAgent(runtime)
    agent.update_chat_ctx = AsyncMock()
    agent.update_tools = AsyncMock()
    agent.update_instructions = AsyncMock()
    session = MagicMock()
    with patch(
        "agent_v2._synthesize_phrases",
        new=AsyncMock(side_effect=RuntimeError("tts unavailable")),
    ):
        await _handle_quest_activation(
            agent, session, runtime, "a", "Ask", ["Please"]
        )

    assert runtime.voice_runtime.status("a").name == "FAILED"
    messages = [
        json.loads(c.args[0])
        for c in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert messages[-1]["event"] == "QUEST_STATUS"
    assert messages[-1]["status"] == "FAILED"


@pytest.mark.asyncio
async def test_cancel_resets_state_and_blocks_late_callback():
    runtime = make_runtime()
    agent = TeacherAgent(runtime)
    agent.update_tools = AsyncMock()
    agent.update_chat_ctx = AsyncMock()
    agent.update_instructions = AsyncMock()
    session = MagicMock()
    with patch("agent_v2._handle_quest_activation", new=AsyncMock()):
        await _process_v2_packet(agent, session, runtime, set_packet())
        agent.bind_evaluation_activation("a")
        await _process_v2_packet(
            agent,
            session,
            runtime,
            json.dumps(
                {
                    "event": "CANCEL_ACTIVE_QUEST",
                    "contract_version": 2,
                    "activation_id": "a",
                    "reason": "lost_race",
                }
            ),
        )
        await agent._complete_activation("a")
    assert runtime.voice_runtime.status("a").name == "CANCELLED"
    assert agent._evaluation_activation_id is None
    session.interrupt.assert_called()
    session.clear_user_turn.assert_called()
    messages = [
        json.loads(c.args[0])
        for c in runtime.job_ctx.room.local_participant.publish_data.call_args_list
    ]
    assert not any(m["event"] == "QUEST_MATCHED" for m in messages)
    assert messages[-1]["status"] == "CANCELLED"


@pytest.mark.asyncio
async def test_failed_terminal_publish_recovers_on_replay_without_reopening():
    runtime = make_runtime()
    runtime.voice_runtime.activate(SetActiveQuest("a", "Ask", ("Please",)))
    agent = TeacherAgent(runtime)
    agent.bind_evaluation_activation("a")
    publish = runtime.job_ctx.room.local_participant.publish_data
    publish.side_effect = [RuntimeError("offline"), None, None, None]
    await agent.complete_quest(MagicMock())
    with patch("agent_v2._handle_quest_activation", new=AsyncMock()) as opening:
        await _process_v2_packet(agent, MagicMock(), runtime, set_packet())
        await _process_v2_packet(agent, MagicMock(), runtime, set_packet())
        opening.assert_not_called()
    events = [json.loads(c.args[0]) for c in publish.call_args_list]
    assert [m["event"] for m in events] == [
        "QUEST_MATCHED",
        "QUEST_MATCHED",
        "QUEST_STATUS",
        "QUEST_STATUS",
    ]
    assert events[-1]["status"] == "MATCHED"


@pytest.mark.asyncio
async def test_replaced_tool_callable_cannot_complete_new_activation():
    runtime = make_runtime()
    agent = TeacherAgent(runtime)
    agent.update_tools = AsyncMock()
    runtime.voice_runtime.activate(SetActiveQuest("a", "Ask", ()))
    await agent.install_evaluation_tool("a")
    old_tool = agent.update_tools.call_args.args[0][0]
    runtime.voice_runtime.activate(SetActiveQuest("b", "Other", ()))
    await agent.install_evaluation_tool("b")
    await old_tool(MagicMock())
    assert runtime.voice_runtime.can_continue("b")
