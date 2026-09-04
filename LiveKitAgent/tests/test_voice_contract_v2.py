import pytest

from voice_contract_v2 import (
    CONTRACT_VERSION,
    CancelActiveQuest,
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
    )
    assert CONTRACT_VERSION == 2

    with pytest.raises(PacketValidationError):
        parse_unity_packet(
            '{"event":"SET_ACTIVE_QUEST","contract_version":1,'
            '"activation_id":"activation-1","quest_goal":"Ask for water",'
            '"phrases":[]}'
        )


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
