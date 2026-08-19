"""
Tests for VR Autism LiveKit Agent (Giáo viên AI)
Validates Quest completion logic, DataPacket handlers, and prompt engineering.
"""

import asyncio
import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from livekit import rtc
from livekit.agents import llm

from agent import (
    BASE_INSTRUCTIONS,
    JobRuntime,
    QuestState,
    TeacherAgent,
    _handle_hint_reminder,
    _handle_speak_script,
    _tts_cache,
    build_quest_instructions,
    make_complete_quest_tool,
    send_rtc_event,
)


# ---------------------------------------------------------------------------
# Unit tests — Prompt & State
# ---------------------------------------------------------------------------


def test_build_quest_instructions_contains_quest_name() -> None:
    """build_quest_instructions must contain quest goal and phrases."""
    instructions = build_quest_instructions(
        quest_name="Báo cáo đã rửa tay xong",
        phrases=["Con đã rửa tay xong chưa?", "Báo cáo cho cô nghe nào!"],
    )
    assert "Báo cáo đã rửa tay xong" in instructions
    assert "Con đã rửa tay xong chưa?" in instructions
    assert "complete_quest()" in instructions


def test_quest_state_lifecycle() -> None:
    """QuestState properly manages active state and reset."""
    qs = QuestState()
    assert qs.active is False
    assert qs.name == ""
    assert qs.phrases == []

    qs.set_active_quest("Rửa tay", ["Con xong chưa?"])
    assert qs.active is True
    assert qs.name == "Rửa tay"
    assert qs.phrases == ["Con xong chưa?"]

    qs.reset()
    assert qs.active is False
    assert qs.name == ""
    assert qs.phrases == []


@pytest.mark.asyncio
async def test_complete_quest_tool_resets_and_notifies() -> None:
    """complete_quest tool resets quest state and sends QUEST_MATCHED + QUEST_STATUS."""
    mock_job_ctx = MagicMock()
    mock_room = MagicMock()
    mock_local_participant = MagicMock()
    mock_local_participant.publish_data = AsyncMock()
    mock_room.local_participant = mock_local_participant
    mock_job_ctx.room = mock_room

    runtime = JobRuntime(mock_job_ctx)
    runtime.quest_state.set_active_quest("Quest1", ["Phrase1"])

    complete_quest = make_complete_quest_tool(runtime)
    result = await complete_quest()

    assert runtime.quest_state.active is False
    assert "Quest1" in result
    assert mock_local_participant.publish_data.call_count == 2

    # Check published payloads
    calls = mock_local_participant.publish_data.call_args_list
    events = [json.loads(c[0][0].decode("utf-8"))["event"] for c in calls]
    assert "QUEST_MATCHED" in events
    assert "QUEST_STATUS" in events


@pytest.mark.asyncio
async def test_complete_quest_when_not_active() -> None:
    """complete_quest returns warning when no quest is active."""
    mock_job_ctx = MagicMock()
    runtime = JobRuntime(mock_job_ctx)

    complete_quest = make_complete_quest_tool(runtime)
    result = await complete_quest()

    assert "chưa có Quest nào được kích hoạt" in result


@pytest.mark.asyncio
async def test_handle_hint_reminder_with_cached_audio() -> None:
    """_handle_hint_reminder plays cached audio frame generator when cache hits."""
    mock_job_ctx = MagicMock()
    runtime = JobRuntime(mock_job_ctx)
    runtime.quest_state.set_active_quest("Quest1", ["CachedPhrase1"])

    # Populate TTS cache with a dummy AudioFrame
    mock_frame = MagicMock(spec=rtc.AudioFrame)
    _tts_cache["CachedPhrase1"] = [mock_frame]

    mock_session = MagicMock()
    mock_session.say = AsyncMock()

    await _handle_hint_reminder(mock_session, runtime, "VERBAL_HINT")

    mock_session.say.assert_called_once()
    called_args, called_kwargs = mock_session.say.call_args
    assert called_args[0] == "CachedPhrase1"
    assert called_kwargs.get("audio") is not None
    assert called_kwargs.get("allow_interruptions") is True


@pytest.mark.asyncio
async def test_handle_hint_reminder_without_cache() -> None:
    """_handle_hint_reminder falls back to live TTS (audio=None) on cache miss."""
    mock_job_ctx = MagicMock()
    runtime = JobRuntime(mock_job_ctx)
    runtime.quest_state.set_active_quest("Quest1", ["UncachedPhrase"])

    mock_session = MagicMock()
    mock_session.say = AsyncMock()

    await _handle_hint_reminder(mock_session, runtime, "ON_REMINDER")

    mock_session.say.assert_called_once()
    called_args, called_kwargs = mock_session.say.call_args
    assert called_args[0] == "UncachedPhrase"
    assert called_kwargs.get("audio") is None


@pytest.mark.asyncio
async def test_handle_speak_script() -> None:
    """_handle_speak_script calls session.say with custom text."""
    mock_session = MagicMock()
    mock_session.say = AsyncMock()

    await _handle_speak_script(mock_session, "Nam ơi, hãy vặn vòi nước nào!")

    mock_session.say.assert_called_once_with(
        "Nam ơi, hãy vặn vòi nước nào!",
        allow_interruptions=True,
    )
