"""Single-command state machine for correlated V2 dialogue speech execution."""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from enum import Enum

from voice_contract_v2 import SpeakScriptV2


class CommandDisposition(Enum):
    NEW = "new"
    REPLAY = "replay"
    REJECTED = "rejected"


class CommandStatus(Enum):
    PENDING = "pending"
    PLAYING = "playing"
    COMPLETED = "completed"
    CANCELLED = "cancelled"
    FAILED = "failed"


@dataclass
class ActiveCommand:
    activation_id: str
    sequence_id: str
    npc_binding_id: str
    text: str
    status: CommandStatus = CommandStatus.PENDING
    reason: str = ""


class VoiceCommandRuntime:
    """Serialize dialogue commands, track active playback, and cache results for idempotency."""

    def __init__(self, max_cache_size: int = 64) -> None:
        self._active: ActiveCommand | None = None
        self._cache: OrderedDict[tuple[str, str], ActiveCommand] = OrderedDict()
        self._max_cache_size = max_cache_size

    @property
    def active_command(self) -> ActiveCommand | None:
        return self._active

    @property
    def active_sequence_id(self) -> str | None:
        return self._active.sequence_id if self._active else None

    def submit(self, request: SpeakScriptV2) -> CommandDisposition:
        """Submit a new speech request or detect an idempotent replay."""
        key = (request.activation_id, request.sequence_id)

        # Check if identical to active command
        if self._active and (self._active.activation_id, self._active.sequence_id) == key:
            if (self._active.text, self._active.npc_binding_id) != (request.text, request.npc_binding_id):
                return CommandDisposition.REJECTED
            return CommandDisposition.REPLAY

        # Check if already completed in cache
        if key in self._cache:
            cached = self._cache[key]
            if (cached.text, cached.npc_binding_id) != (request.text, request.npc_binding_id):
                return CommandDisposition.REJECTED
            return CommandDisposition.REPLAY

        # If another command is currently active, supersede it
        if self._active:
            self._active.status = CommandStatus.CANCELLED
            self._active.reason = "superseded"
            self._archive(self._active)

        self._active = ActiveCommand(
            activation_id=request.activation_id,
            sequence_id=request.sequence_id,
            npc_binding_id=request.npc_binding_id,
            text=request.text,
            status=CommandStatus.PENDING,
        )
        return CommandDisposition.NEW

    def start(self, activation_id: str, sequence_id: str) -> bool:
        """Mark the pending command as currently playing."""
        if not self._is_target(activation_id, sequence_id):
            return False
        self._active.status = CommandStatus.PLAYING
        return True

    def mark_completed(
        self,
        activation_id: str,
        sequence_id: str,
        status: str = "COMPLETED",
        reason: str = "",
    ) -> bool:
        """Mark command complete and cache result."""
        if not self._is_target(activation_id, sequence_id):
            return False

        norm_status = status.upper()
        if norm_status == "SUCCESS" or norm_status == "COMPLETED":
            self._active.status = CommandStatus.COMPLETED
        elif norm_status == "CANCELLED":
            self._active.status = CommandStatus.CANCELLED
        elif norm_status == "FAILED":
            self._active.status = CommandStatus.FAILED
        else:
            self._active.status = CommandStatus.COMPLETED

        self._active.reason = reason
        self._archive(self._active)
        self._active = None
        return True

    def mark_failed(self, activation_id: str, sequence_id: str, reason: str = "") -> bool:
        return self.mark_completed(activation_id, sequence_id, status="FAILED", reason=reason)

    def cancel_active(self, reason: str = "cancelled") -> bool:
        if not self._active:
            return False
        self._active.status = CommandStatus.CANCELLED
        self._active.reason = reason
        self._archive(self._active)
        self._active = None
        return True

    def can_continue(self, activation_id: str, sequence_id: str) -> bool:
        return bool(
            self._active
            and self._active.activation_id == activation_id
            and self._active.sequence_id == sequence_id
            and self._active.status in (CommandStatus.PENDING, CommandStatus.PLAYING)
        )

    def status(self, activation_id: str, sequence_id: str) -> CommandStatus | None:
        key = (activation_id, sequence_id)
        if self._active and (self._active.activation_id, self._active.sequence_id) == key:
            return self._active.status
        cached = self._cache.get(key)
        return cached.status if cached else None

    def reason(self, activation_id: str, sequence_id: str) -> str:
        key = (activation_id, sequence_id)
        if self._active and (self._active.activation_id, self._active.sequence_id) == key:
            return self._active.reason
        cached = self._cache.get(key)
        return cached.reason if cached else ""

    def _is_target(self, activation_id: str, sequence_id: str) -> bool:
        return bool(
            self._active
            and self._active.activation_id == activation_id
            and self._active.sequence_id == sequence_id
        )

    def _archive(self, entry: ActiveCommand) -> None:
        key = (entry.activation_id, entry.sequence_id)
        self._cache[key] = entry
        while len(self._cache) > self._max_cache_size:
            self._cache.popitem(last=False)
