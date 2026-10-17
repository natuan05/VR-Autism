import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest
from livekit import rtc

from shared_teacher_speech import try_handle_teacher_script

SESSION_ID = "room-1"
HOST_ID = "host-1"
RUNTIME = "quiz"


def participant(metadata, *, kind=rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD):
    return SimpleNamespace(kind=kind, metadata=json.dumps(metadata))


def packet(data, *, topic="", lesson_runtime=RUNTIME):
    return SimpleNamespace(
        participant=participant(
            {
                "participant_role": "teacher",
                "lesson_runtime": lesson_runtime,
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            }
        ),
        topic=topic,
        data=data,
    )


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "lesson_runtime", ["quiz", "exploration", "legacy_hello_friend"]
)
async def test_authorized_teacher_script_uses_existing_session_speech(lesson_runtime):
    session = MagicMock()
    session.say = AsyncMock()
    message = packet(
        json.dumps({"event": "SPEAK_SCRIPT", "text": "  Hello there.  "}),
        lesson_runtime=lesson_runtime,
    )

    handled = await try_handle_teacher_script(
        session,
        message,
        session_id=SESSION_ID,
        host_id=HOST_ID,
        lesson_runtime=lesson_runtime,
    )

    assert handled is True
    session.say.assert_awaited_once_with("Hello there.", allow_interruptions=True)


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "data",
    [
        b"not json",
        b"[]",
        b'{"event":"SPEAK_SCRIPT","text":"  "}',
        b'{"event":"SPEAK_SCRIPT","text":42}',
        b'{"event":"VERBAL_HINT","text":"Hello"}',
        b'{"event":"SPEAK_SCRIPT","text":"Hello","extra":true}',
    ],
)
async def test_malformed_or_non_speech_payload_is_not_claimed(data):
    session = MagicMock()
    session.say = AsyncMock()

    handled = await try_handle_teacher_script(
        session,
        packet(data),
        session_id=SESSION_ID,
        host_id=HOST_ID,
        lesson_runtime=RUNTIME,
    )

    assert handled is False
    session.say.assert_not_awaited()


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "metadata,kind,topic,scope",
    [
        (
            {"participant_role": "vr"},
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": "other",
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": SESSION_ID,
                "host_id": "other",
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": "exploration",
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": "action",
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, "action"),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": SESSION_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": {"unexpected": True},
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "lesson-graph-v2.voice",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            "remote-command",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
        (
            {
                "participant_role": "teacher",
                "lesson_runtime": RUNTIME,
                "session_id": SESSION_ID,
                "host_id": HOST_ID,
            },
            rtc.ParticipantKind.PARTICIPANT_KIND_INGRESS,
            "",
            (SESSION_ID, HOST_ID, RUNTIME),
        ),
    ],
)
async def test_scope_kind_or_topic_mismatch_is_not_claimed(
    metadata, kind, topic, scope
):
    session = MagicMock()
    session.say = AsyncMock()
    incoming = SimpleNamespace(
        participant=participant(metadata, kind=kind),
        topic=topic,
        data=b'{"event":"SPEAK_SCRIPT","text":"Hello"}',
    )

    handled = await try_handle_teacher_script(
        session,
        incoming,
        session_id=scope[0],
        host_id=scope[1],
        lesson_runtime=scope[2],
    )

    assert handled is False
    session.say.assert_not_awaited()


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "metadata", [None, "", "not-json", "[]", {"participant_role": "teacher"}]
)
async def test_missing_or_invalid_participant_metadata_is_not_claimed(metadata):
    session = MagicMock()
    session.say = AsyncMock()
    incoming = SimpleNamespace(
        participant=SimpleNamespace(
            kind=rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD,
            metadata=metadata,
        ),
        topic="",
        data=b'{"event":"SPEAK_SCRIPT","text":"Hello"}',
    )

    handled = await try_handle_teacher_script(
        session,
        incoming,
        session_id=SESSION_ID,
        host_id=HOST_ID,
        lesson_runtime=RUNTIME,
    )

    assert handled is False
    session.say.assert_not_awaited()


@pytest.mark.asyncio
async def test_speech_error_is_contained_and_packet_is_claimed():
    session = MagicMock()
    session.say = AsyncMock(side_effect=RuntimeError("speech failed"))

    handled = await try_handle_teacher_script(
        session,
        packet(b'{"event":"SPEAK_SCRIPT","text":"Hello"}'),
        session_id=SESSION_ID,
        host_id=HOST_ID,
        lesson_runtime=RUNTIME,
    )

    assert handled is True
    session.say.assert_awaited_once_with("Hello", allow_interruptions=True)


@pytest.mark.asyncio
async def test_invalid_trusted_scope_is_not_claimed():
    session = MagicMock()
    session.say = AsyncMock()

    handled = await try_handle_teacher_script(
        session,
        packet(b'{"event":"SPEAK_SCRIPT","text":"Hello"}'),
        session_id=SESSION_ID,
        host_id=HOST_ID,
        lesson_runtime=[],
    )

    assert handled is False
    session.say.assert_not_awaited()
