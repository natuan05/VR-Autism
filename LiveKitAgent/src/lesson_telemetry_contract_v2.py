"""Pure validation for observed Lesson Graph V2 telemetry documents."""

from __future__ import annotations

import math
import re
from collections.abc import Mapping
from datetime import datetime
from typing import TypeAlias

TelemetryDocument: TypeAlias = Mapping[str, object]

CONTRACT_VERSION = 2
LESSON_STATUSES = frozenset(
    {"running", "pausing", "paused", "completed", "failed", "cancelled"}
)
FIREBASE_PATH_FORBIDDEN_CHARS = frozenset("/.#$[]")

STATE_TEXT_FIELDS = (
    "session_id",
    "run_id",
    "graph_id",
    "lesson_id",
    "launch_token",
    "node_id",
    "node_type",
    "activation_id",
    "status",
    "checkpoint_id",
    "updated_at_utc",
    "parallel_group_id",
)
AUDIT_TEXT_FIELDS = (
    "event_id",
    "event_type",
    "session_id",
    "run_id",
    "graph_id",
    "lesson_id",
    "node_id",
    "node_type",
    "activation_id",
    "occurred_at_utc",
    "status",
    "command_id",
    "command",
    "binding_id",
    "reason",
    "launch_token",
)
NODE_LOG_TEXT_FIELDS = (
    "event_id",
    "session_id",
    "run_id",
    "graph_id",
    "lesson_id",
    "launch_token",
    "node_id",
    "node_type",
    "node_name",
    "activation_id",
    "entered_at_utc",
    "exited_at_utc",
    "status",
    "completion_channel",
)


class TelemetryContractError(ValueError):
    """Raised when an observed V2 telemetry document violates its contract."""


def validate_lesson_state_v2(
    value: object,
    *,
    expected_session_id: str | None = None,
    expected_run_id: str | None = None,
    expected_node_id: str | None = None,
    expected_activation_id: str | None = None,
) -> None:
    """Validate a state projection while ignoring additive future observation keys."""
    data = _document(value, "lesson state")
    _integer(data, "contract_version", minimum=CONTRACT_VERSION, exact=CONTRACT_VERSION)
    _text_fields(data, STATE_TEXT_FIELDS)
    _required_identity(data, "session_id", expected=expected_session_id)
    _firebase_path_identity(data["session_id"], "session_id")
    _required_identity(data, "run_id", expected=expected_run_id)
    _required_identity(data, "node_id", expected=expected_node_id)
    _required_identity(data, "activation_id", expected=expected_activation_id)
    for field in ("graph_id", "lesson_id", "launch_token"):
        _required_identity(data, field)
    if data["status"] not in LESSON_STATUSES:
        raise TelemetryContractError("status is not a supported LessonStateV2 status")
    for field in ("lesson_voice_revision", "child_phrase_revision", "state_revision"):
        _integer(data, field, minimum=0)
    _integer(data, "node_index", minimum=0)
    _utc_timestamp(data["updated_at_utc"], "updated_at_utc")
    active_node_ids = data.get("active_node_ids")
    if not isinstance(active_node_ids, list) or any(
        not isinstance(node_id, str) or not node_id.strip()
        for node_id in active_node_ids
    ):
        raise TelemetryContractError(
            "active_node_ids must be an array of non-empty text"
        )
    bindings = data.get("bindings")
    if not isinstance(bindings, list):
        raise TelemetryContractError("bindings must be an array")
    for index, binding in enumerate(bindings):
        binding_data = _document(binding, f"bindings[{index}]")
        _required_identity(binding_data, "binding_id")
        _text(binding_data, "npc_binding_id")
        for field in ("can_verbal_hint", "can_visual_hint"):
            if type(binding_data.get(field)) is not bool:
                raise TelemetryContractError(
                    f"bindings[{index}].{field} must be boolean"
                )


def validate_lesson_audit_event_v2(
    value: object,
    *,
    expected_session_id: str | None = None,
    expected_run_id: str | None = None,
    expected_node_id: str | None = None,
    expected_activation_id: str | None = None,
) -> None:
    """Validate one audit event, requiring activation correlation for node/command events."""
    data = _document(value, "lesson audit event")
    _text_fields(data, AUDIT_TEXT_FIELDS)
    _required_identity(data, "event_id")
    _required_identity(data, "event_type")
    _required_identity(data, "session_id", expected=expected_session_id)
    _firebase_path_identity(data["session_id"], "session_id")
    _required_identity(data, "run_id", expected=expected_run_id)
    if data["event_type"] in {
        "NODE_ENTERED",
        "NODE_COMPLETED",
        "NODE_CANCELLED",
        "COMMAND_ACCEPTED",
        "COMMAND_REJECTED",
    }:
        _required_identity(data, "node_id", expected=expected_node_id)
        _required_identity(data, "activation_id", expected=expected_activation_id)
    _integer(data, "node_index", minimum=-1)
    _integer(data, "lesson_voice_revision", minimum=0)
    _integer(data, "child_phrase_revision", minimum=0)
    _number(data, "elapsed_seconds", minimum=0.0)
    _utc_timestamp(data["occurred_at_utc"], "occurred_at_utc")


def validate_node_log_v2(
    value: object,
    *,
    expected_session_id: str | None = None,
    expected_run_id: str | None = None,
    expected_node_id: str | None = None,
    expected_activation_id: str | None = None,
) -> None:
    """Validate one Firestore activation log and its session/run/node correlation."""
    data = _document(value, "node log")
    _text_fields(data, NODE_LOG_TEXT_FIELDS)
    for field in ("event_id", "session_id", "run_id", "node_id", "activation_id"):
        expected = {
            "session_id": expected_session_id,
            "run_id": expected_run_id,
            "node_id": expected_node_id,
            "activation_id": expected_activation_id,
        }.get(field)
        _required_identity(data, field, expected=expected)
    _firebase_path_identity(data["session_id"], "session_id")
    for field in ("graph_id", "lesson_id", "launch_token", "node_type", "node_name"):
        _text(data, field)
    _integer(data, "lesson_voice_revision", minimum=0)
    _integer(data, "child_phrase_revision", minimum=0)
    _integer(data, "node_index", minimum=0)
    _utc_timestamp(data["entered_at_utc"], "entered_at_utc")
    _utc_timestamp(data["exited_at_utc"], "exited_at_utc")
    _number(data, "duration_seconds", minimum=0.0)
    _number(data, "elapsed_seconds", minimum=0.0)


def _document(value: object, name: str) -> TelemetryDocument:
    if not isinstance(value, Mapping):
        raise TelemetryContractError(f"{name} must be an object")
    return value


def _text_fields(data: TelemetryDocument, fields: tuple[str, ...]) -> None:
    for field in fields:
        _text(data, field)


def _text(data: TelemetryDocument, field: str) -> str:
    value = data.get(field)
    if not isinstance(value, str):
        raise TelemetryContractError(f"{field} must be text")
    return value


def _required_identity(
    data: TelemetryDocument,
    field: str,
    *,
    expected: str | None = None,
) -> str:
    value = _text(data, field)
    if not value.strip():
        raise TelemetryContractError(f"{field} must be non-empty text")
    if expected is not None:
        _compare_identity(value, expected, field)
    return value


def _compare_identity(value: str, expected: str, field: str) -> None:
    if not isinstance(expected, str) or not expected.strip():
        raise TelemetryContractError(f"expected {field} must be non-empty text")
    if value != expected:
        raise TelemetryContractError(f"{field} does not match the expected correlation")


def _firebase_path_identity(value: str, field: str) -> None:
    if any(character in FIREBASE_PATH_FORBIDDEN_CHARS for character in value):
        raise TelemetryContractError(
            f"{field} contains a Firebase path-forbidden character"
        )


def _integer(
    data: TelemetryDocument,
    field: str,
    *,
    minimum: int,
    exact: int | None = None,
) -> int:
    value = data.get(field)
    if (
        type(value) is not int
        or value < minimum
        or (exact is not None and value != exact)
    ):
        if exact is not None:
            raise TelemetryContractError(f"{field} must be {exact}")
        raise TelemetryContractError(f"{field} must be an integer at least {minimum}")
    return value


def _number(data: TelemetryDocument, field: str, *, minimum: float) -> float:
    value = data.get(field)
    if type(value) not in (int, float) or not math.isfinite(value) or value < minimum:
        raise TelemetryContractError(
            f"{field} must be a finite number at least {minimum}"
        )
    return float(value)


def _utc_timestamp(value: object, field: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise TelemetryContractError(f"{field} must be a UTC ISO 8601 timestamp")
    normalized = value[:-1] + "+00:00" if value.endswith("Z") else value
    # .NET "O" timestamps have seven fractional digits; datetime supports six.
    normalized = re.sub(r"(\.\d{6})\d+(?=[+-]\d{2}:\d{2}$)", r"\1", normalized)
    try:
        timestamp = datetime.fromisoformat(normalized)
    except ValueError as exc:
        raise TelemetryContractError(
            f"{field} must be a UTC ISO 8601 timestamp"
        ) from exc
    if timestamp.utcoffset() is None or timestamp.utcoffset().total_seconds() != 0:
        raise TelemetryContractError(f"{field} must use UTC")
