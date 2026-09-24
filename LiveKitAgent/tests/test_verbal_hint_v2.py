import asyncio
import json
from contextlib import asynccontextmanager
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest

import agent_v2
from agent_v2 import JobRuntime, _process_v2_packet, _reset_activation
from voice_contract_v2 import SetActiveQuest, parse_unity_packet


def _runtime_with_active_quest(
    *,
    activation_id: str = "activation-1",
    npc_binding_id: str = "teacher-npc",
    phrases: tuple[str, ...] = ("first phrase", "second phrase"),
) -> JobRuntime:
    context = MagicMock()
    context.room = MagicMock()
    runtime = JobRuntime(context)
    runtime.voice_runtime.activate(
        SetActiveQuest(
            activation_id=activation_id,
            quest_goal="ask for water",
            phrases=phrases,
            npc_binding_id=npc_binding_id,
        )
    )
    return runtime


class _SpeechHandle:
    def __init__(self, *, wait_started: asyncio.Event | None = None) -> None:
        self.interrupted = False
        self.wait_started = wait_started
        self.release_playout = asyncio.Event()
        if wait_started is None:
            self.release_playout.set()
        self.interrupt = MagicMock(side_effect=self._interrupt)

    def _interrupt(self) -> None:
        self.interrupted = True
        self.release_playout.set()

    async def wait_for_playout(self) -> None:
        if self.wait_started:
            self.wait_started.set()
        await self.release_playout.wait()


class _FakeTTS:
    def __init__(self, *, synthesis_started: asyncio.Event | None = None) -> None:
        self.synthesis_started = synthesis_started
        self.synthesized: list[str] = []
        self.update_options = MagicMock()
        self.block_synthesis = False
        self.release_synthesis = asyncio.Event()

    def synthesize(self, text: str):
        self.synthesized.append(text)
        fake_tts = self

        @asynccontextmanager
        async def _stream():
            class Stream:
                def __aiter__(self):
                    async def _frames():
                        if fake_tts.synthesis_started:
                            fake_tts.synthesis_started.set()
                        if fake_tts.block_synthesis:
                            await fake_tts.release_synthesis.wait()
                        yield SimpleNamespace(frame=object())

                    return _frames()

            yield Stream()

        return _stream()


def _session(*, handle: _SpeechHandle | None = None):
    session = MagicMock()
    session.tts = _FakeTTS()
    session.say = AsyncMock(return_value=handle or _SpeechHandle())
    return session


def _verbal_hint_packet(
    *,
    command_id: str = "command-1",
    activation_id: str = "activation-1",
    npc_binding_id: str = "teacher-npc",
) -> str:
    return json.dumps(
        {
            "event": "VERBAL_HINT",
            "contract_version": 2,
            "command_id": command_id,
            "activation_id": activation_id,
            "npc_binding_id": npc_binding_id,
        }
    )


def test_parse_verbal_hint_v2_requires_its_exact_correlated_shape() -> None:
    parsed = parse_unity_packet(_verbal_hint_packet())

    assert type(parsed).__name__ == "VerbalHintV2"
    assert parsed.command_id == "command-1"
    assert parsed.activation_id == "activation-1"
    assert parsed.npc_binding_id == "teacher-npc"

    invalid = json.loads(_verbal_hint_packet())
    invalid["activation_id"] = " "
    with pytest.raises(ValueError):
        parse_unity_packet(json.dumps(invalid))

    invalid["activation_id"] = "activation-1"
    invalid["unexpected"] = "extra field"
    with pytest.raises(ValueError):
        parse_unity_packet(json.dumps(invalid))


@pytest.mark.asyncio
async def test_verbal_hint_uses_assigned_npc_and_snapshot_phrase_order() -> None:
    runtime = _runtime_with_active_quest(npc_binding_id="peer-npc")
    session = _session()
    runtime.tts_cache[
        ("teacher-npc", "vi-VN-Chirp3-HD-Aoede", 1.0, "first phrase")
    ] = [object()]

    await _process_v2_packet(
        MagicMock(), session, runtime, _verbal_hint_packet(npc_binding_id="peer-npc")
    )
    await _process_v2_packet(
        MagicMock(),
        session,
        runtime,
        _verbal_hint_packet(command_id="command-2", npc_binding_id="peer-npc"),
    )

    assert [call.args[0] for call in session.say.call_args_list] == [
        "first phrase",
        "second phrase",
    ]
    assert session.tts.update_options.call_args_list[0].kwargs["voice_name"] == (
        "vi-VN-Chirp3-HD-Puck"
    )
    assert session.tts.synthesized == ["first phrase", "second phrase"]


@pytest.mark.asyncio
async def test_verbal_hint_rejects_wrong_activation_or_npc_without_speaking() -> None:
    runtime = _runtime_with_active_quest()
    session = _session()

    await _process_v2_packet(
        MagicMock(),
        session,
        runtime,
        _verbal_hint_packet(activation_id="stale-activation"),
    )
    await _process_v2_packet(
        MagicMock(),
        session,
        runtime,
        _verbal_hint_packet(command_id="wrong-npc", npc_binding_id="peer-npc"),
    )

    session.say.assert_not_awaited()


@pytest.mark.asyncio
async def test_verbal_hint_duplicate_does_not_speak_or_advance_phrase_order() -> None:
    runtime = _runtime_with_active_quest()
    session = _session()
    agent = MagicMock()

    await _process_v2_packet(agent, session, runtime, _verbal_hint_packet())
    await _process_v2_packet(agent, session, runtime, _verbal_hint_packet())
    await _process_v2_packet(
        agent, session, runtime, _verbal_hint_packet(command_id="command-2")
    )

    assert [call.args[0] for call in session.say.call_args_list] == [
        "first phrase",
        "second phrase",
    ]


@pytest.mark.asyncio
async def test_remote_topic_verbal_hint_is_ignored() -> None:
    process_packet = getattr(agent_v2, "_process_data_packet", None)
    assert callable(process_packet)

    runtime = _runtime_with_active_quest()
    session = _session()
    await process_packet(
        MagicMock(),
        session,
        runtime,
        "lesson-graph-v2.remote",
        _verbal_hint_packet(),
    )

    session.say.assert_not_awaited()


@pytest.mark.asyncio
async def test_activation_reset_cancels_hint_during_synthesis_before_speech() -> None:
    synthesis_started = asyncio.Event()
    runtime = _runtime_with_active_quest()
    session = _session()
    session.tts = _FakeTTS(synthesis_started=synthesis_started)
    session.tts.block_synthesis = True
    hint_task = asyncio.create_task(
        _process_v2_packet(
            MagicMock(), session, runtime, _verbal_hint_packet()
        )
    )
    await asyncio.wait_for(synthesis_started.wait(), timeout=1)

    agent = MagicMock()
    agent.update_tools = AsyncMock()
    agent.update_chat_ctx = AsyncMock()
    agent.update_instructions = AsyncMock()
    with pytest.MonkeyPatch.context() as monkeypatch:
        monkeypatch.setattr(agent_v2.llm.ChatContext, "empty", MagicMock(return_value=None))
        await _reset_activation(agent, session, runtime)

    await asyncio.gather(hint_task, return_exceptions=True)
    session.say.assert_not_awaited()


@pytest.mark.asyncio
async def test_activation_reset_interrupts_owned_hint_playback() -> None:
    playback_started = asyncio.Event()
    handle = _SpeechHandle(wait_started=playback_started)
    runtime = _runtime_with_active_quest()
    session = _session(handle=handle)
    hint_task = asyncio.create_task(
        _process_v2_packet(MagicMock(), session, runtime, _verbal_hint_packet())
    )
    await asyncio.wait_for(playback_started.wait(), timeout=1)

    agent = MagicMock()
    agent.update_tools = AsyncMock()
    agent.update_chat_ctx = AsyncMock()
    agent.update_instructions = AsyncMock()
    with pytest.MonkeyPatch.context() as monkeypatch:
        monkeypatch.setattr(agent_v2.llm.ChatContext, "empty", MagicMock(return_value=None))
        await _reset_activation(agent, session, runtime)

    await asyncio.gather(hint_task, return_exceptions=True)
    handle.interrupt.assert_called_once()
    assert handle.interrupted
