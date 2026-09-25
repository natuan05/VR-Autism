from __future__ import annotations

from copy import deepcopy

import pytest

from lesson_telemetry_contract_v2 import (
    TelemetryContractError,
    validate_lesson_audit_event_v2,
    validate_lesson_state_v2,
    validate_node_log_v2,
)

# These literal values mirror LessonTelemetryReducerV2Tests.State/Context/Node and
# LessonTelemetryContractsV2's Firestore DTO fields.
STATE_FIXTURE: dict[str, object] = {
    "contract_version": 2,
    "session_id": "session-1",
    "run_id": "run-1",
    "graph_id": "graph-1",
    "lesson_id": "lesson-1",
    "launch_token": "launch-1",
    "lesson_voice_revision": 4,
    "child_phrase_revision": 7,
    "node_id": "quest-1",
    "node_type": "Quest",
    "node_index": 2,
    "activation_id": "activation-1",
    "status": "running",
    "checkpoint_id": "",
    "updated_at_utc": "2026-09-24T08:00:00.0000000+00:00",
    "state_revision": 1,
    "active_node_ids": ["quest-1"],
    "parallel_group_id": "",
    "bindings": [],
}

AUDIT_FIXTURE: dict[str, object] = {
    "event_id": "event-1",
    "event_type": "NODE_COMPLETED",
    "session_id": "session-1",
    "run_id": "run-1",
    "graph_id": "graph-1",
    "lesson_id": "lesson-1",
    "node_id": "quest-1",
    "node_type": "Quest",
    "node_index": 2,
    "activation_id": "activation-1",
    "occurred_at_utc": "2026-09-24T08:00:09.0000000+00:00",
    "elapsed_seconds": 14.0,
    "status": "success",
    "command_id": "",
    "command": "",
    "binding_id": "",
    "reason": "",
    "launch_token": "launch-1",
    "lesson_voice_revision": 4,
    "child_phrase_revision": 7,
}

NODE_LOG_FIXTURE: dict[str, object] = {
    "event_id": "node-event-1",
    "session_id": "session-1",
    "run_id": "run-1",
    "graph_id": "graph-1",
    "lesson_id": "lesson-1",
    "launch_token": "launch-1",
    "lesson_voice_revision": 4,
    "child_phrase_revision": 7,
    "node_id": "quest-1",
    "node_type": "Quest",
    "node_name": "Wash hands",
    "node_index": 2,
    "activation_id": "activation-1",
    "entered_at_utc": "2026-09-24T08:00:00.0000000+00:00",
    "exited_at_utc": "2026-09-24T08:00:09.0000000+00:00",
    "duration_seconds": 4.0,
    "elapsed_seconds": 14.0,
    "status": "success",
    "completion_channel": "touch",
}


def test_validate_csharp_state_audit_and_node_log_fixtures() -> None:
    validate_lesson_state_v2(
        STATE_FIXTURE,
        expected_session_id="session-1",
        expected_run_id="run-1",
        expected_node_id="quest-1",
        expected_activation_id="activation-1",
    )
    validate_lesson_audit_event_v2(AUDIT_FIXTURE, expected_session_id="session-1")
    validate_node_log_v2(NODE_LOG_FIXTURE, expected_session_id="session-1")


def test_state_and_nested_bindings_accept_additive_observation_fields() -> None:
    state = deepcopy(STATE_FIXTURE)
    state["future_observation"] = {"source": "rtdb"}
    state["active_node_ids"] = ["quest-1", "future-parallel-node"]
    state["bindings"] = [
        {
            "binding_id": "soap-touch",
            "npc_binding_id": "teacher-npc",
            "can_verbal_hint": True,
            "can_visual_hint": True,
            "future_capability": "gesture",
        }
    ]

    validate_lesson_state_v2(state, expected_session_id="session-1")


def test_state_allows_empty_npc_binding_for_non_voice_sources() -> None:
    state = {
        **STATE_FIXTURE,
        "bindings": [
            {
                "binding_id": "soap-touch",
                "npc_binding_id": "",
                "can_verbal_hint": False,
                "can_visual_hint": True,
            }
        ],
    }

    validate_lesson_state_v2(state)


def test_node_log_accepts_additive_observation_fields() -> None:
    log = {**NODE_LOG_FIXTURE, "future_sensor_summary": {"count": 3}}

    validate_node_log_v2(log, expected_session_id="session-1")


def test_audit_event_accepts_additive_observation_fields() -> None:
    audit = {**AUDIT_FIXTURE, "future_trace": {"source": "runner"}}

    validate_lesson_audit_event_v2(audit, expected_session_id="session-1")


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("session_id", "other-session"),
        ("run_id", "other-run"),
        ("node_id", ""),
        ("node_id", "other-node"),
        ("activation_id", ""),
        ("activation_id", "other-activation"),
    ],
)
def test_node_log_rejects_missing_or_mismatched_correlation(
    field: str, value: str
) -> None:
    log = {**NODE_LOG_FIXTURE, field: value}

    with pytest.raises(TelemetryContractError):
        validate_node_log_v2(
            log,
            expected_session_id="session-1",
            expected_run_id="run-1",
            expected_node_id="quest-1",
            expected_activation_id="activation-1",
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("session_id", "other-session"),
        ("run_id", "other-run"),
        ("node_id", ""),
        ("node_id", "other-node"),
        ("activation_id", ""),
        ("activation_id", "other-activation"),
    ],
)
def test_node_audit_rejects_missing_or_mismatched_correlation(
    field: str, value: str
) -> None:
    audit = {**AUDIT_FIXTURE, field: value}

    with pytest.raises(TelemetryContractError):
        validate_lesson_audit_event_v2(
            audit,
            expected_session_id="session-1",
            expected_run_id="run-1",
            expected_node_id="quest-1",
            expected_activation_id="activation-1",
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("contract_version", True),
        ("state_revision", "1"),
        ("node_index", 2.0),
        ("active_node_ids", "quest-1"),
        ("bindings", [{}]),
        ("status", "future-status"),
    ],
)
def test_state_rejects_malformed_required_types(field: str, value: object) -> None:
    state = {**STATE_FIXTURE, field: value}

    with pytest.raises(TelemetryContractError):
        validate_lesson_state_v2(state)


@pytest.mark.parametrize("field", ["graph_id", "lesson_id", "launch_token"])
def test_state_requires_non_empty_launch_context(field: str) -> None:
    state = {**STATE_FIXTURE, field: "  "}

    with pytest.raises(TelemetryContractError):
        validate_lesson_state_v2(state)


@pytest.mark.parametrize("character", ["/", ".", "#", "$", "[", "]"])
def test_state_rejects_firebase_forbidden_session_id_characters(
    character: str,
) -> None:
    state = {**STATE_FIXTURE, "session_id": f"session{character}1"}

    with pytest.raises(TelemetryContractError, match="Firebase"):
        validate_lesson_state_v2(state)


@pytest.mark.parametrize(
    ("field", "expected"),
    [
        ("session_id", "session-1"),
        ("run_id", "run-1"),
        ("node_id", "quest-1"),
        ("activation_id", "activation-1"),
    ],
)
def test_state_rejects_missing_required_correlation(field: str, expected: str) -> None:
    state = dict(STATE_FIXTURE)
    del state[field]

    with pytest.raises(TelemetryContractError):
        validate_lesson_state_v2(state)


@pytest.mark.parametrize(
    ("field", "expected"),
    [
        ("session_id", "session-1"),
        ("run_id", "run-1"),
        ("node_id", "quest-1"),
        ("activation_id", "activation-1"),
    ],
)
def test_state_rejects_wrong_expected_correlation(field: str, expected: str) -> None:
    wrong_value = f"wrong-{expected}"
    state = {**STATE_FIXTURE, field: wrong_value}
    expected_args = {
        "expected_session_id": "session-1",
        "expected_run_id": "run-1",
        "expected_node_id": "quest-1",
        "expected_activation_id": "activation-1",
    }

    with pytest.raises(TelemetryContractError):
        validate_lesson_state_v2(state, **expected_args)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("node_index", 2.0),
        ("elapsed_seconds", True),
        ("occurred_at_utc", "yesterday"),
        ("lesson_voice_revision", -1),
    ],
)
def test_audit_rejects_malformed_required_types(field: str, value: object) -> None:
    audit = {**AUDIT_FIXTURE, field: value}

    with pytest.raises(TelemetryContractError):
        validate_lesson_audit_event_v2(audit)
