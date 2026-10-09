"""Strict, versioned DTOs for LessonGraph V2 voice packets."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from typing import TypeAlias

CONTRACT_VERSION = 2
MAX_PHRASES_PER_QUEST = 50
MAX_SCRIPT_LENGTH = 500
DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS = 5.0
VOICE_TOPIC = "lesson-graph-v2.voice"
REMOTE_TOPIC = "lesson-graph-v2.remote"


class PacketValidationError(ValueError):
    """Raised when an inbound packet is not an exact supported V2 packet."""


@dataclass(frozen=True)
class SetActiveQuest:
    activation_id: str
    quest_goal: str
    phrases: tuple[str, ...]
    npc_binding_id: str = ""
    speech_silence_timeout_seconds: float = DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS


@dataclass(frozen=True)
class CancelActiveQuest:
    activation_id: str
    reason: str


@dataclass(frozen=True)
class CancelSpeakScriptV2:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    reason: str


@dataclass(frozen=True)
class SpeakScriptV2:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    text: str


@dataclass(frozen=True)
class VerbalHintV2:
    command_id: str
    activation_id: str
    npc_binding_id: str


@dataclass(frozen=True)
class SpeakScriptDoneV2:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    status: str = "SUCCESS"
    reason: str = ""


UnityPacket: TypeAlias = (
    SetActiveQuest
    | CancelActiveQuest
    | CancelSpeakScriptV2
    | SpeakScriptV2
    | VerbalHintV2
)


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
        base_keys = {
            "event",
            "contract_version",
            "activation_id",
            "quest_goal",
            "phrases",
        }
        allowed_optional = {"npc_binding_id", "speech_silence_timeout_seconds"}
        if not base_keys.issubset(keys) or not keys.issubset(
            base_keys | allowed_optional
        ):
            raise PacketValidationError("packet fields do not match its V2 event shape")
        npc_binding_id = data.get("npc_binding_id", "")
        if not isinstance(npc_binding_id, str):
            raise PacketValidationError("npc_binding_id must be text")
        timeout = data.get(
            "speech_silence_timeout_seconds", DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS
        )
        if isinstance(timeout, bool) or not isinstance(timeout, (int, float)):
            raise PacketValidationError(
                "speech_silence_timeout_seconds must be a finite non-negative number"
            )
        try:
            timeout = float(timeout)
        except OverflowError as exc:
            raise PacketValidationError(
                "speech_silence_timeout_seconds must be a finite non-negative number"
            ) from exc
        if not math.isfinite(timeout) or timeout < 0:
            raise PacketValidationError(
                "speech_silence_timeout_seconds must be a finite non-negative number"
            )
        return SetActiveQuest(
            activation_id=_required_text(data, "activation_id"),
            quest_goal=_required_text(data, "quest_goal"),
            phrases=_required_phrases(data),
            npc_binding_id=npc_binding_id.strip(),
            speech_silence_timeout_seconds=float(timeout),
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
    if event == "CANCEL_SPEAK_SCRIPT":
        _require_exact_keys(
            data,
            {
                "event",
                "contract_version",
                "activation_id",
                "sequence_id",
                "npc_binding_id",
                "reason",
            },
        )
        reason = _required_text(data, "reason")
        if reason not in {
            "lesson_scope_changed",
            "bridge_unload",
            "transport_reconfigured",
        }:
            raise PacketValidationError("unsupported script cancellation reason")
        return CancelSpeakScriptV2(
            activation_id=_required_text(data, "activation_id"),
            sequence_id=_required_text(data, "sequence_id"),
            npc_binding_id=_required_text(data, "npc_binding_id"),
            reason=reason,
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
        text = _required_text(data, "text")
        if len(text.encode("utf-16-le")) // 2 > MAX_SCRIPT_LENGTH:
            raise PacketValidationError(f"text exceeds {MAX_SCRIPT_LENGTH} characters")
        return SpeakScriptV2(
            activation_id=_required_text(data, "activation_id"),
            sequence_id=_required_text(data, "sequence_id"),
            npc_binding_id=_required_text(data, "npc_binding_id"),
            text=text,
        )
    if event == "VERBAL_HINT":
        _require_exact_keys(
            data,
            {
                "event",
                "contract_version",
                "command_id",
                "activation_id",
                "npc_binding_id",
            },
        )
        return VerbalHintV2(
            command_id=_required_text(data, "command_id"),
            activation_id=_required_text(data, "activation_id"),
            npc_binding_id=_required_text(data, "npc_binding_id"),
        )
    raise PacketValidationError("unsupported V2 event")


def quest_matched_packet(activation_id: str) -> dict[str, object]:
    """Build the single correlated success packet accepted by Unity V2."""
    return _outbound_packet("QUEST_MATCHED", activation_id)


def accepted_reminder_packet(
    activation_id: str, npc_binding_id: str, command_id: str
) -> dict[str, object]:
    """Build typed evidence after one activation-scoped reminder finished playing."""
    return {
        "contract_version": CONTRACT_VERSION,
        "event": "ON_REMINDER",
        "direction": "agent_to_unity",
        "result": "accepted",
        "activation_id": activation_id,
        "npc_binding_id": npc_binding_id,
        "command_id": command_id,
    }


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
    if (
        not isinstance(phrases, list)
        or len(phrases) > MAX_PHRASES_PER_QUEST
        or not all(
            isinstance(phrase, str) and phrase.strip() and len(phrase) <= 240
            for phrase in phrases
        )
    ):
        raise PacketValidationError("phrases must be an array of non-empty text")
    return tuple(phrases)
