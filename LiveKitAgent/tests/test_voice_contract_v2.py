import json

import pytest

from voice_contract_v2 import (
    CONTRACT_VERSION,
    CancelActiveQuest,
    CancelSpeakScriptV2,
    PacketValidationError,
    SetActiveQuest,
    parse_unity_packet,
)


def test_parse_set_active_quest_requires_the_versioned_v2_shape() -> None:
    """Rejecting missing or wrong V2 fields prevents a legacy packet becoming active."""
    packet = parse_unity_packet(
        b'{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        b'"activation_id":"activation-1","quest_goal":"Ask for water",'
        b'"phrases":["Please give me water"]}'
    )

    assert packet == SetActiveQuest(
        activation_id="activation-1",
        quest_goal="Ask for water",
        phrases=("Please give me water",),
        npc_binding_id="",
    )
    assert CONTRACT_VERSION == 2

    with pytest.raises(PacketValidationError):
        parse_unity_packet(
            '{"event":"SET_ACTIVE_QUEST","contract_version":1,'
            '"activation_id":"activation-1","quest_goal":"Ask for water",'
            '"phrases":[]}'
        )


def test_parse_set_active_quest_with_npc_binding_id() -> None:
    packet = parse_unity_packet(
        '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-2","quest_goal":"Ask for water",'
        '"phrases":["Please give me water"],"npc_binding_id":"teacher-npc"}'
    )
    assert packet == SetActiveQuest(
        activation_id="activation-2",
        quest_goal="Ask for water",
        phrases=("Please give me water",),
        npc_binding_id="teacher-npc",
    )

    with pytest.raises(PacketValidationError):
        parse_unity_packet(
            '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
            '"activation_id":"activation-2","quest_goal":"Ask for water",'
            '"phrases":["Please give me water"],"npc_binding_id":123}'
        )


def test_parse_set_active_quest_optional_silence_timeout_uses_strict_numeric_value() -> (
    None
):
    packet = parse_unity_packet(
        '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-3","quest_goal":"Ask for water",'
        '"phrases":["Water, please"],"npc_binding_id":"teacher-npc",'
        '"speech_silence_timeout_seconds":2.5}'
    )

    assert packet == SetActiveQuest(
        activation_id="activation-3",
        quest_goal="Ask for water",
        phrases=("Water, please",),
        npc_binding_id="teacher-npc",
        speech_silence_timeout_seconds=2.5,
    )

    # Older V2 activations remain valid and receive the legacy five-second default.
    legacy_v2 = parse_unity_packet(
        '{"event":"SET_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-4","quest_goal":"Ask",'
        '"phrases":["Water"]}'
    )
    assert legacy_v2.speech_silence_timeout_seconds == 5.0

    for invalid in (True, -0.1, float("inf"), 10**1000):
        payload = {
            "event": "SET_ACTIVE_QUEST",
            "contract_version": 2,
            "activation_id": "activation-5",
            "quest_goal": "Ask",
            "phrases": ["Water"],
            "speech_silence_timeout_seconds": invalid,
        }
        with pytest.raises(
            PacketValidationError, match="speech_silence_timeout_seconds"
        ):
            parse_unity_packet(json.dumps(payload, allow_nan=True))


def test_parse_cancel_requires_a_non_empty_reason_and_activation() -> None:
    """Malformed cancellation must not cancel an unrelated active activation."""
    packet = parse_unity_packet(
        '{"event":"CANCEL_ACTIVE_QUEST","contract_version":2,'
        '"activation_id":"activation-1","reason":"lost_race"}'
    )

    assert packet == CancelActiveQuest("activation-1", "lost_race")

    with pytest.raises(PacketValidationError):
        parse_unity_packet(
            '{"event":"CANCEL_ACTIVE_QUEST","contract_version":2,'
            '"activation_id":"","reason":"lost_race"}'
        )


def test_parse_cancel_speak_script_requires_exact_correlated_scope() -> None:
    packet = parse_unity_packet(
        '{"event":"CANCEL_SPEAK_SCRIPT","contract_version":2,'
        '"activation_id":"activation-1","sequence_id":"script-1",'
        '"npc_binding_id":"teacher-npc","reason":"lesson_scope_changed"}'
    )
    assert packet == CancelSpeakScriptV2(
        "activation-1", "script-1", "teacher-npc", "lesson_scope_changed"
    )

    for changed in (
        {"activation_id": ""},
        {"sequence_id": ""},
        {"npc_binding_id": ""},
        {"reason": "anything"},
        {"unexpected": "field"},
    ):
        data = {
            "event": "CANCEL_SPEAK_SCRIPT",
            "contract_version": 2,
            "activation_id": "activation-1",
            "sequence_id": "script-1",
            "npc_binding_id": "teacher-npc",
            "reason": "lesson_scope_changed",
            **changed,
        }
        with pytest.raises(PacketValidationError):
            parse_unity_packet(json.dumps(data))


def test_parse_set_active_quest_rejects_excessive_phrase_count() -> None:
    payload = {
        "event": "SET_ACTIVE_QUEST",
        "contract_version": 2,
        "activation_id": "activation-1",
        "quest_goal": "Ask for water",
        "phrases": [f"phrase-{index}" for index in range(51)],
    }

    with pytest.raises(PacketValidationError, match="phrases"):
        parse_unity_packet(json.dumps(payload))
