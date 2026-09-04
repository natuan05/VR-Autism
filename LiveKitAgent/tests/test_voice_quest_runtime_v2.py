from voice_contract_v2 import CancelActiveQuest, SetActiveQuest
from voice_quest_runtime_v2 import ActivationDisposition, VoiceQuestRuntime


def test_replay_acknowledges_without_reopening_the_active_quest() -> None:
    """A reconnect replay must not make the agent say the opening phrase twice."""
    runtime = VoiceQuestRuntime()
    request = SetActiveQuest("activation-1", "Ask for water", ("Water, please",))

    assert runtime.activate(request) is ActivationDisposition.NEW
    assert runtime.claim_opening("activation-1") is True
    assert runtime.activate(request) is ActivationDisposition.REPLAY
    assert runtime.claim_opening("activation-1") is False
    assert runtime.active_activation_id == "activation-1"


def test_replacement_and_cancellation_make_late_success_impossible() -> None:
    """A transcript from a cancelled or replaced activation cannot complete a quest."""
    runtime = VoiceQuestRuntime()
    runtime.activate(SetActiveQuest("activation-1", "First", ("First",)))
    runtime.activate(SetActiveQuest("activation-2", "Second", ("Second",)))

    assert runtime.mark_matched("activation-1") is False
    assert runtime.cancel(CancelActiveQuest("activation-2", "lost_race")) is True
    assert runtime.mark_matched("activation-2") is False


def test_match_is_emitted_once_for_the_current_activation() -> None:
    """Duplicate evaluator callbacks cannot emit duplicate terminal success signals."""
    runtime = VoiceQuestRuntime()
    runtime.activate(SetActiveQuest("activation-1", "Ask", ("Ask",)))

    assert runtime.mark_matched("activation-1") is True
    assert runtime.mark_matched("activation-1") is False
