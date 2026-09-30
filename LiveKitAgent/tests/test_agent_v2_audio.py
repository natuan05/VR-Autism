import asyncio
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest
from livekit import rtc
from livekit.plugins import google

import agent_v2


class Room(rtc.EventEmitter):
    def __init__(self, *participants):
        super().__init__()
        self.name = "voice-test-room"
        self.remote_participants = {p.identity: p for p in participants}
        self.local_participant = SimpleNamespace(publish_data=AsyncMock())


def participant(identity, kind=rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD):
    return SimpleNamespace(identity=identity, kind=kind, attributes={})


@pytest.fixture
def session(monkeypatch):
    monkeypatch.setenv("GOOGLE_API_KEY", "test-key")
    monkeypatch.setenv("GOOGLE_APPLICATION_CREDENTIALS", str(Path(__file__)))
    for name in ("STT", "LLM", "TTS"):
        monkeypatch.setattr(agent_v2.google, name, MagicMock())
    value = MagicMock()
    value.start = AsyncMock()
    monkeypatch.setattr(agent_v2, "AgentSession", MagicMock(return_value=value))
    return value


def context(room):
    return SimpleNamespace(
        room=room,
        connect=AsyncMock(),
        proc=SimpleNamespace(userdata={"vad": object()}),
    )


@pytest.mark.asyncio
async def test_entrypoint_links_unity_when_dashboard_joined_first(session):
    room = Room(participant("expert_first"), participant("vr_509521"))

    await agent_v2.entrypoint(context(room))

    options = session.start.call_args.kwargs.get("room_options")
    assert options is not None, "The session must explicitly select Unity audio"
    assert options.participant_identity == "vr_509521"
    assert not room._events.get("participant_connected")


@pytest.mark.asyncio
async def test_entrypoint_waits_for_unity_instead_of_listening_to_dashboard(session):
    room = Room(participant("expert_first"))
    task = asyncio.create_task(agent_v2.entrypoint(context(room)))
    try:
        await asyncio.sleep(0)
        assert not task.done(), (
            "A dashboard participant must not start the voice session"
        )
        assert session.start.await_count == 0

        unity = participant("vr_later")
        room.remote_participants[unity.identity] = unity
        room.emit("participant_connected", unity)
        await asyncio.wait_for(task, timeout=1)

        assert (
            session.start.call_args.kwargs["room_options"].participant_identity
            == "vr_later"
        )
        assert not room._events.get("participant_connected")
    finally:
        if not task.done():
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)


@pytest.mark.asyncio
async def test_cancelling_unity_wait_removes_listener(session):
    room = Room(participant("expert_first"))
    task = asyncio.create_task(agent_v2.entrypoint(context(room)))
    await asyncio.sleep(0)
    task.cancel()

    with pytest.raises(asyncio.CancelledError):
        await task

    assert not room._events.get("participant_connected")
    assert session.start.await_count == 0


@pytest.mark.parametrize("npc_binding_id", ["teacher-npc", "peer-npc"])
def test_voice_switch_preserves_language_for_google_synthesis(npc_binding_id):
    # Use the actual plugin: a mock missed update_options replacing the voice params.
    tts = google.TTS(language="vi-VN", voice_name="vi-VN-Chirp3-HD-Aoede")

    agent_v2._apply_voice_profile(
        SimpleNamespace(tts=tts), agent_v2.DEFAULT_VOICE_PROFILES[npc_binding_id]
    )

    assert tts._opts.voice.language_code == "vi-VN"
    assert tts._opts.voice.name == (
        "vi-VN-Chirp3-HD-Aoede"
        if npc_binding_id == "teacher-npc"
        else "vi-VN-Chirp3-HD-Puck"
    )


@pytest.mark.asyncio
async def test_dashboard_packets_do_not_queue_while_waiting_for_unity(
    session, monkeypatch
):
    expert = participant("expert_first")
    room = Room(expert)
    ctx = context(room)
    runtime = agent_v2.JobRuntime(ctx)
    monkeypatch.setattr(agent_v2, "JobRuntime", lambda _: runtime)
    task = asyncio.create_task(agent_v2.entrypoint(ctx))
    try:
        await asyncio.sleep(0)
        room.emit(
            "data_received",
            SimpleNamespace(
                participant=expert, topic="", data=b'{"event":"VERBAL_HINT"}'
            ),
        )
        await asyncio.sleep(0)
        assert not runtime.background_tasks
    finally:
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
        pending = tuple(runtime.background_tasks)
        for packet_task in pending:
            packet_task.cancel()
        await asyncio.gather(*pending, return_exceptions=True)
    assert not room._events.get("data_received")


@pytest.mark.asyncio
async def test_failed_start_removes_data_listener_and_cancels_queued_unity_packets(
    session, monkeypatch
):
    unity = participant("vr_509521")
    room = Room(unity)
    ctx = context(room)
    runtime = agent_v2.JobRuntime(ctx)
    monkeypatch.setattr(agent_v2, "JobRuntime", lambda _: runtime)

    async def failed_start(**kwargs):
        room.emit(
            "data_received",
            SimpleNamespace(participant=unity, topic=agent_v2.VOICE_TOPIC, data=b"{}"),
        )
        await asyncio.sleep(0)
        raise RuntimeError("session startup failed")

    session.start.side_effect = failed_start
    try:
        with pytest.raises(RuntimeError, match="session startup failed"):
            await agent_v2.entrypoint(ctx)
        assert not room._events.get("data_received")
        assert not runtime.background_tasks
    finally:
        pending = tuple(runtime.background_tasks)
        for packet_task in pending:
            packet_task.cancel()
        await asyncio.gather(*pending, return_exceptions=True)


@pytest.mark.asyncio
async def test_unity_quest_packet_received_during_start_is_processed_after_ready(
    session, monkeypatch
):
    unity = participant("vr_509521")
    room = Room(unity)
    process_packet = AsyncMock()
    monkeypatch.setattr(agent_v2, "_process_data_packet", process_packet)
    payload = b'{"event":"SET_ACTIVE_QUEST"}'

    async def start_with_packet(**kwargs):
        room.emit(
            "data_received",
            SimpleNamespace(
                participant=unity, topic=agent_v2.VOICE_TOPIC, data=payload
            ),
        )
        await asyncio.sleep(0)
        assert process_packet.await_count == 0

    session.start.side_effect = start_with_packet
    await agent_v2.entrypoint(context(room))
    await asyncio.sleep(0)

    assert process_packet.await_count == 1
    assert process_packet.call_args.args[-2:] == (agent_v2.VOICE_TOPIC, payload)
