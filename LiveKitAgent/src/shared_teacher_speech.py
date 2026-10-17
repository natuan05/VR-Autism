"""Narrow compatibility handler for authenticated teacher free-text speech."""

import json
import logging
from typing import Any

from livekit import rtc
from livekit.agents.voice import AgentSession

logger = logging.getLogger(__name__)

_COMPATIBLE_RUNTIMES = {"quiz", "exploration", "legacy_hello_friend"}


def _participant_metadata(participant: Any) -> dict[str, Any] | None:
    metadata = getattr(participant, "metadata", None)
    if isinstance(metadata, dict):
        return metadata
    if not isinstance(metadata, str) or not metadata:
        return None
    try:
        value = json.loads(metadata)
    except (TypeError, json.JSONDecodeError):
        return None
    return value if isinstance(value, dict) else None


async def try_handle_teacher_script(
    session: AgentSession,
    data_packet: Any,
    *,
    session_id: str,
    host_id: str,
    lesson_runtime: str,
) -> bool:
    """Speak one scoped teacher script; leave all other packets untouched."""
    if (
        not isinstance(session_id, str)
        or not session_id
        or not isinstance(host_id, str)
        or not host_id
        or not isinstance(lesson_runtime, str)
        or lesson_runtime not in _COMPATIBLE_RUNTIMES
        or getattr(data_packet, "topic", None) != ""
    ):
        return False

    participant = getattr(data_packet, "participant", None)
    if (
        participant is None
        or getattr(participant, "kind", None)
        != rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
    ):
        return False
    metadata = _participant_metadata(participant)
    if metadata is None or any(
        metadata.get(key) != expected
        for key, expected in (
            ("participant_role", "teacher"),
            ("lesson_runtime", lesson_runtime),
            ("session_id", session_id),
            ("host_id", host_id),
        )
    ):
        return False

    try:
        raw_data = getattr(data_packet, "data", None)
        if isinstance(raw_data, bytes):
            raw_data = raw_data.decode("utf-8")
        if not isinstance(raw_data, str):
            return False
        payload = json.loads(raw_data)
    except (UnicodeDecodeError, json.JSONDecodeError, TypeError):
        return False
    if (
        not isinstance(payload, dict)
        or set(payload) != {"event", "text"}
        or payload.get("event") != "SPEAK_SCRIPT"
        or not isinstance(payload.get("text"), str)
    ):
        return False

    text = payload["text"].strip()
    if not text:
        return False
    try:
        await session.say(text, allow_interruptions=True)
    except Exception as err:
        logger.error("[TEACHER] Error handling SPEAK_SCRIPT: %s", err)
    return True
