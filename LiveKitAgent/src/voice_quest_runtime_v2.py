"""Single-activation state machine for correlated V2 voice quest work."""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass, field
from enum import Enum

from voice_contract_v2 import (
    DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS,
    CancelActiveQuest,
    SetActiveQuest,
    VerbalHintV2,
)


class ActivationDisposition(Enum):
    NEW = "new"
    REPLAY = "replay"


class ActivationStatus(Enum):
    ACTIVE = "active"
    CANCELLED = "cancelled"
    MATCHED = "matched"
    FAILED = "failed"


@dataclass
class ActiveActivation:
    activation_id: str
    goal: str
    phrases: tuple[str, ...]
    npc_binding_id: str = ""
    speech_silence_timeout_seconds: float = DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS
    status: ActivationStatus = ActivationStatus.ACTIVE
    opening_claimed: bool = False
    cancellation_reason: str = ""
    verbal_hint_command_ids: set[str] = field(default_factory=set)
    next_hint_phrase_index: int = 0


class VoiceQuestRuntime:
    """Own exactly one activation and make stale completions harmless."""

    def __init__(self) -> None:
        self._active: ActiveActivation | None = None
        self._tombstones: OrderedDict[str, ActiveActivation] = OrderedDict()

    @property
    def active_activation_id(self) -> str | None:
        return self._active.activation_id if self._active else None

    @property
    def active_goal(self) -> str | None:
        return self._active.goal if self._active else None

    @property
    def active_phrases(self) -> tuple[str, ...]:
        return self._active.phrases if self._active else ()

    @property
    def active_npc_binding_id(self) -> str | None:
        return self._active.npc_binding_id if self._active else None

    @property
    def active_speech_silence_timeout_seconds(self) -> float:
        return (
            self._active.speech_silence_timeout_seconds
            if self._active
            else DEFAULT_SPEECH_SILENCE_TIMEOUT_SECONDS
        )

    def activate(self, request: SetActiveQuest) -> ActivationDisposition:
        """Install a request atomically, or identify a reconnect replay."""
        if self._active and self._active.activation_id == request.activation_id:
            if (
                self._active.goal,
                self._active.phrases,
                self._active.npc_binding_id,
                self._active.speech_silence_timeout_seconds,
            ) != (
                request.quest_goal,
                request.phrases,
                request.npc_binding_id,
                request.speech_silence_timeout_seconds,
            ):
                raise ValueError("activation payload changed")
            return ActivationDisposition.REPLAY
        if request.activation_id in self._tombstones:
            raise ValueError("activation is tombstoned")
        if self._active:
            if self._active.status is ActivationStatus.ACTIVE:
                self._active.status = ActivationStatus.CANCELLED
            self._tombstones[self._active.activation_id] = self._active
            while len(self._tombstones) > 64:
                self._tombstones.popitem(last=False)
        self._active = ActiveActivation(
            activation_id=request.activation_id,
            goal=request.quest_goal,
            phrases=request.phrases,
            npc_binding_id=request.npc_binding_id,
            speech_silence_timeout_seconds=request.speech_silence_timeout_seconds,
        )
        return ActivationDisposition.NEW

    def claim_verbal_hint(self, request: VerbalHintV2) -> str | None:
        """Claim one unique hint for the matching live activation and NPC."""
        if not self._is_active(request.activation_id):
            return None
        activation = self._active
        if (
            not request.command_id.strip()
            or not activation.npc_binding_id
            or request.npc_binding_id != activation.npc_binding_id
            or request.command_id in activation.verbal_hint_command_ids
            or not activation.phrases
        ):
            return None

        activation.verbal_hint_command_ids.add(request.command_id)
        phrase = activation.phrases[activation.next_hint_phrase_index]
        activation.next_hint_phrase_index = (
            activation.next_hint_phrase_index + 1
        ) % len(activation.phrases)
        return phrase

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
        self._active.cancellation_reason = request.reason
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

    @property
    def tombstone_count(self) -> int:
        return len(self._tombstones)

    def status(self, activation_id: str) -> ActivationStatus | None:
        activation = (
            self._active
            if self.active_activation_id == activation_id
            else self._tombstones.get(activation_id)
        )
        return activation.status if activation else None

    def reason(self, activation_id: str) -> str | None:
        activation = (
            self._active
            if self.active_activation_id == activation_id
            else self._tombstones.get(activation_id)
        )
        return activation.cancellation_reason if activation else None

    def mark_failed(self, activation_id: str) -> bool:
        if not self._is_active(activation_id):
            return False
        self._active.status = ActivationStatus.FAILED
        return True
