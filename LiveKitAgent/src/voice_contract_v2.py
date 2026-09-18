"""Strict, versioned DTOs for LessonGraph V2 voice packets."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import TypeAlias

CONTRACT_VERSION = 2
MAX_PHRASES_PER_QUEST = 50
VOICE_TOPIC = "lesson-graph-v2.voice"


class PacketValidationError(ValueError):
    """Raised when an inbound packet is not an exact supported V2 packet."""


@dataclass(frozen=True)
class SetActiveQuest:
    activation_id: str
    quest_goal: str
    phrases: tuple[str, ...]
    npc_binding_id: str = ""


@dataclass(frozen=True)
class CancelActiveQuest:
    activation_id: str
    reason: str


@dataclass(frozen=True)
class SpeakScriptV2:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    text: str


@dataclass(frozen=True)
class SpeakScriptDoneV2:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    status: str = "SUCCESS"
    reason: str = ""


UnityPacket: TypeAlias = SetActiveQuest | CancelActiveQuest | SpeakScriptV2


def parse_unity_packet(payload: bytes | str) -> UnityPacket:
    """Decode an inbound Unity packet after its LiveKit topic has been checked."""
    try:
        decoded = payload.decode("utf-8") if isinstance(payload, bytes) else payload
        data = json.loads(decoded)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PacketValidationError("packet must contain UTF-8 JSON") from exc

    if not isinstance(data, dict):
        raise PacketValidationError("packet must be a JSON object")
    if (
        type(data.get("contract_version")) is not int
        or data["contract_version"] != CONTRACT_VERSION
    ):
        raise PacketValidationError("unsupported contract_version")

    event = data.get("event")
    if event == "SET_ACTIVE_QUEST":
        keys = set(data)
        base_keys = {"event", "contract_version", "activation_id", "quest_goal", "phrases"}
        if keys not in (base_keys, base_keys | {"npc_binding_id"}):
            raise PacketValidationError("packet fields do not match its V2 event shape")
        npc_binding_id = data.get("npc_binding_id", "")
        if not isinstance(npc_binding_id, str):
            raise PacketValidationError("npc_binding_id must be text")
        return SetActiveQuest(
            activation_id=_required_text(data, "activation_id"),
            quest_goal=_required_text(data, "quest_goal"),
            phrases=_required_phrases(data),
            npc_binding_id=npc_binding_id.strip(),
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
    if event == "SPEAK_SCRIPT":
        _require_exact_keys(
            data,
            {
                "event",
                "contract_version",
                "activation_id",
                "sequence_id",
                "npc_binding_id",
                "text",
            },
        )
        return SpeakScriptV2(
            activation_id=_required_text(data, "activation_id"),
            sequence_id=_required_text(data, "sequence_id"),
            npc_binding_id=_required_text(data, "npc_binding_id"),
            text=_required_text(data, "text"),
        )
    raise PacketValidationError("unsupported V2 event")


def quest_matched_packet(activation_id: str) -> dict[str, object]:
    """Build the single correlated success packet accepted by Unity V2."""
    return _outbound_packet("QUEST_MATCHED", activation_id)


def quest_status_packet(
    activation_id: str, status: str, reason: str | None = None
) -> dict[str, object]:
    """Build a correlated status acknowledgement or terminal status packet."""
    if status not in {"ACTIVE", "MATCHED", "CANCELLED", "FAILED"}:
        raise PacketValidationError("unsupported activation status")
    packet = _outbound_packet("QUEST_STATUS", activation_id)
    packet["status"] = status
    if reason:
        packet["reason"] = reason
    return packet


def speak_script_done_packet(
    activation_id: str,
    sequence_id: str,
    npc_binding_id: str,
    status: str = "SUCCESS",
    reason: str | None = None,
) -> dict[str, object]:
    """Build the correlated dialogue completion packet for Unity V2."""
    norm_status = status.upper() if isinstance(status, str) else ""
    if norm_status not in {"SUCCESS", "CANCELLED", "FAILED"}:
        raise PacketValidationError("unsupported dialogue completion status")
    if not isinstance(sequence_id, str) or not sequence_id.strip():
        raise PacketValidationError("sequence_id must be non-empty")
    if not isinstance(npc_binding_id, str) or not npc_binding_id.strip():
        raise PacketValidationError("npc_binding_id must be non-empty")
    packet = _outbound_packet("SPEAK_SCRIPT_DONE", activation_id)
    packet["sequence_id"] = sequence_id
    packet["npc_binding_id"] = npc_binding_id
    packet["status"] = norm_status
    if reason:
        packet["reason"] = reason
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
    if not isinstance(phrases, list) or len(phrases) > MAX_PHRASES_PER_QUEST or not all(
        isinstance(phrase, str) and phrase.strip() and len(phrase) <= 240
        for phrase in phrases
    ):
        raise PacketValidationError("phrases must be an array of non-empty text")
    return tuple(phrases)
