"""Strict, versioned DTOs for LessonGraph V2 voice packets."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import TypeAlias

CONTRACT_VERSION = 2
VOICE_TOPIC = "lesson-graph-v2.voice"


class PacketValidationError(ValueError):
    """Raised when an inbound packet is not an exact supported V2 packet."""


@dataclass(frozen=True)
class SetActiveQuest:
    activation_id: str
    quest_goal: str
    phrases: tuple[str, ...]


@dataclass(frozen=True)
class CancelActiveQuest:
    activation_id: str
    reason: str


UnityPacket: TypeAlias = SetActiveQuest | CancelActiveQuest


def parse_unity_packet(payload: bytes | str) -> UnityPacket:
    """Decode an inbound Unity packet after its LiveKit topic has been checked."""
    try:
        decoded = payload.decode("utf-8") if isinstance(payload, bytes) else payload
        data = json.loads(decoded)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PacketValidationError("packet must contain UTF-8 JSON") from exc

    if not isinstance(data, dict):
        raise PacketValidationError("packet must be a JSON object")
    if data.get("contract_version") != CONTRACT_VERSION:
        raise PacketValidationError("unsupported contract_version")

    event = data.get("event")
    if event == "SET_ACTIVE_QUEST":
        _require_exact_keys(
            data,
            {"event", "contract_version", "activation_id", "quest_goal", "phrases"},
        )
        return SetActiveQuest(
            activation_id=_required_text(data, "activation_id"),
            quest_goal=_required_text(data, "quest_goal"),
            phrases=_required_phrases(data),
        )
    if event == "CANCEL_ACTIVE_QUEST":
        _require_exact_keys(
            data,
            {"event", "contract_version", "activation_id", "reason"},
        )
        return CancelActiveQuest(
            activation_id=_required_text(data, "activation_id"),
            reason=_required_text(data, "reason"),
        )
    raise PacketValidationError("unsupported V2 event")


def quest_matched_packet(activation_id: str) -> dict[str, object]:
    """Build the single correlated success packet accepted by Unity V2."""
    return _outbound_packet("QUEST_MATCHED", activation_id)


def quest_status_packet(activation_id: str, status: str) -> dict[str, object]:
    """Build a correlated status acknowledgement or terminal status packet."""
    packet = _outbound_packet("QUEST_STATUS", activation_id)
    packet["status"] = status
    return packet


def _outbound_packet(event: str, activation_id: str) -> dict[str, object]:
    if not isinstance(activation_id, str) or not activation_id.strip():
        raise PacketValidationError("activation_id must be non-empty")
    return {
        "event": event,
        "contract_version": CONTRACT_VERSION,
        "activation_id": activation_id,
    }


def _require_exact_keys(data: dict[str, object], expected: set[str]) -> None:
    if set(data) != expected:
        raise PacketValidationError("packet fields do not match its V2 event shape")


def _required_text(data: dict[str, object], field: str) -> str:
    value = data.get(field)
    if not isinstance(value, str) or not value.strip():
        raise PacketValidationError(f"{field} must be non-empty text")
    return value


def _required_phrases(data: dict[str, object]) -> tuple[str, ...]:
    phrases = data.get("phrases")
    if not isinstance(phrases, list) or not all(
        isinstance(phrase, str) and phrase.strip() for phrase in phrases
    ):
        raise PacketValidationError("phrases must be an array of non-empty text")
    return tuple(phrases)
