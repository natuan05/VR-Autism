"""Single-activation state machine for correlated V2 voice quest work."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from voice_contract_v2 import CancelActiveQuest, SetActiveQuest


class ActivationDisposition(Enum):
    NEW = "new"
    REPLAY = "replay"


class ActivationStatus(Enum):
    ACTIVE = "active"
    CANCELLED = "cancelled"
    MATCHED = "matched"


@dataclass
class ActiveActivation:
    activation_id: str
    goal: str
    phrases: tuple[str, ...]
    status: ActivationStatus = ActivationStatus.ACTIVE
    opening_claimed: bool = False


class VoiceQuestRuntime:
    """Own exactly one activation and make stale completions harmless."""

    def __init__(self) -> None:
        self._active: ActiveActivation | None = None

    @property
    def active_activation_id(self) -> str | None:
        return self._active.activation_id if self._active else None

    @property
    def active_goal(self) -> str | None:
        return self._active.goal if self._active else None

    @property
    def active_phrases(self) -> tuple[str, ...]:
        return self._active.phrases if self._active else ()

    def activate(self, request: SetActiveQuest) -> ActivationDisposition:
        """Install a request atomically, or identify a reconnect replay."""
        if self._active and self._active.activation_id == request.activation_id:
            return ActivationDisposition.REPLAY
        self._active = ActiveActivation(
            activation_id=request.activation_id,
            goal=request.quest_goal,
            phrases=request.phrases,
        )
        return ActivationDisposition.NEW

    def claim_opening(self, activation_id: str) -> bool:
        """Allow the opening phrase once while the activation remains live."""
        if not self._is_active(activation_id) or self._active.opening_claimed:
            return False
        self._active.opening_claimed = True
        return True

    def cancel(self, request: CancelActiveQuest) -> bool:
        """Terminally cancel only the current live activation."""
        if not self._is_active(request.activation_id):
            return False
        self._active.status = ActivationStatus.CANCELLED
        return True

    def mark_matched(self, activation_id: str) -> bool:
        """Terminally match once, rejecting stale and duplicate evaluator results."""
        if not self._is_active(activation_id):
            return False
        self._active.status = ActivationStatus.MATCHED
        return True

    def can_continue(self, activation_id: str) -> bool:
        """Whether asynchronous opening/hint work still belongs to this activation."""
        return self._is_active(activation_id)

    def _is_active(self, activation_id: str) -> bool:
        return bool(
            self._active
            and self._active.activation_id == activation_id
            and self._active.status is ActivationStatus.ACTIVE
        )
