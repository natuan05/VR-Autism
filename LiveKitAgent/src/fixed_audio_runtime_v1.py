"""Small job-local state for cache-aware fixed-audio ownership and correlation."""

from __future__ import annotations

import asyncio
import uuid
from dataclasses import dataclass, field
from typing import Any

from tts_cache_client_v1 import SessionScopeV1


@dataclass
class FixedAudioRuntimeV1:
    session_id: str
    agent_instance_id: str = field(default_factory=lambda: uuid.uuid4().hex)
    run_id: str | None = None
    retired_run_ids: dict[str, None] = field(default_factory=dict)
    owner_revision: int = -1
    activity_sequence: int = 0
    current_invocation: dict[str, Any] | None = None
    current_activity: dict[str, Any] | None = None
    external_activity: dict[str, Any] | None = None
    external_activity_owner_revision: int = -1
    current_task: asyncio.Task[Any] | None = None
    active_activation_ids: set[str] = field(default_factory=set)
    completed_opening_activation_ids: set[str] = field(default_factory=set)
    reminder_waiters: dict[tuple[str, ...], asyncio.Future[bool]] = field(
        default_factory=dict
    )
    reminder_owner_revisions: dict[tuple[str, ...], int] = field(default_factory=dict)
    reminder_terminal_status: dict[tuple[str, ...], str] = field(default_factory=dict)
    terminal_status: dict[tuple[str, ...], str] = field(default_factory=dict)
    terminal_details: dict[tuple[str, ...], dict[str, Any]] = field(
        default_factory=dict
    )
    cancelled_invocations: dict[tuple[str, ...], int] = field(default_factory=dict)

    @classmethod
    def from_scope(cls, scope: SessionScopeV1) -> FixedAudioRuntimeV1:
        return cls(session_id=scope.session_id)

    def accept_run(self, run_id: str) -> bool:
        if not isinstance(run_id, str) or not run_id.strip():
            return False
        if run_id == self.run_id:
            return True
        if run_id in self.retired_run_ids:
            return False
        if self.run_id is not None:
            self.retired_run_ids[self.run_id] = None
            while len(self.retired_run_ids) > 1024:
                retired = next(
                    (
                        candidate
                        for candidate in self.retired_run_ids
                        if candidate != self.run_id
                    ),
                    None,
                )
                if retired is None:
                    break
                self.retired_run_ids.pop(retired)
            self.external_activity = None
            self.external_activity_owner_revision = -1
            self.completed_opening_activation_ids.clear()
        self.run_id = run_id
        return True

    def accept_external_activity(
        self, packet: dict[str, Any], *, authoritative: bool = False
    ) -> bool:
        """Accept only monotonic activity for a currently reconciled invocation."""
        if (
            packet.get("event") != "FIXED_AUDIO_ACTIVITY"
            or packet.get("session_id") != self.session_id
            or packet.get("run_id") != self.run_id
            or packet.get("activation_id") not in self.active_activation_ids
        ):
            return False
        previous = self.external_activity
        if previous is not None:
            if self.invocation_key(previous) == self.invocation_key(packet):
                if (
                    previous["phase"] == "terminal"
                    or packet["owner_revision"] != previous["owner_revision"]
                    or packet["activity_sequence"] <= previous["activity_sequence"]
                ):
                    return False
            elif packet["owner_revision"] <= self.external_activity_owner_revision:
                return False
        elif (
            not authoritative
            and packet["owner_revision"] <= self.external_activity_owner_revision
        ):
            return False
        self.external_activity = dict(packet)
        self.external_activity_owner_revision = max(
            self.external_activity_owner_revision, packet["owner_revision"]
        )
        return True

    def is_current(self, invocation: dict[str, Any]) -> bool:
        return bool(
            self.current_invocation
            and self.invocation_key(self.current_invocation)
            == self.invocation_key(invocation)
            and self.current_invocation.get("owner_revision")
            == invocation.get("owner_revision")
        )

    @staticmethod
    def invocation_key(invocation: dict[str, Any]) -> tuple[str, ...]:
        return tuple(
            invocation.get(name, "")
            for name in (
                "session_id",
                "run_id",
                "activation_id",
                "invocation_id",
                "npc_binding_id",
                "kind",
            )
        )

    def terminal_for(self, invocation: dict[str, Any]) -> dict[str, Any] | None:
        return self.terminal_details.get(self.invocation_key(invocation))

    def remember_terminal(
        self, invocation: dict[str, Any], status: str, owner_revision: int, reason: str
    ) -> None:
        key = self.invocation_key(invocation)
        self.terminal_status[key] = status
        self.terminal_details[key] = {
            "status": status,
            "owner_revision": owner_revision,
            "reason": reason,
        }
        while len(self.terminal_status) > 1024:
            current_key = (
                self.invocation_key(self.current_invocation)
                if self.current_invocation is not None
                else None
            )
            oldest = next(
                (
                    candidate
                    for candidate in self.terminal_status
                    if candidate != current_key
                ),
                None,
            )
            if oldest is None:
                break
            self.terminal_status.pop(oldest)
            self.terminal_details.pop(oldest, None)


def invocation_fields(packet: dict[str, Any]) -> dict[str, Any]:
    return {
        key: packet[key]
        for key in (
            "session_id",
            "run_id",
            "activation_id",
            "invocation_id",
            "npc_binding_id",
            "kind",
        )
    }


def activity_packet(
    invocation: dict[str, Any],
    *,
    owner_revision: int,
    activity_sequence: int,
    phase: str,
    source: str,
    status: str = "PENDING",
    reason: str = "",
) -> dict[str, Any]:
    return {
        "contract_version": 1,
        "event": "FIXED_AUDIO_ACTIVITY",
        **invocation_fields(invocation),
        "owner_revision": owner_revision,
        "activity_sequence": activity_sequence,
        "phase": phase,
        "source": source,
        "status": status,
        "reason": reason,
    }


def done_packet(
    invocation: dict[str, Any],
    agent_instance_id: str,
    owner_revision: int,
    status: str,
    reason: str = "",
) -> dict[str, Any]:
    return {
        "contract_version": 1,
        "event": "FIXED_AUDIO_DONE",
        **invocation_fields(invocation),
        "owner_revision": owner_revision,
        "agent_instance_id": agent_instance_id,
        "status": status,
        "reason": reason,
    }


def reconcile_packet(
    session_id: str,
    run_id: str,
    agent_instance_id: str,
    active_activation_ids: list[str],
    current_activity: dict[str, Any] | None,
) -> dict[str, Any]:
    return {
        "contract_version": 1,
        "event": "FIXED_AUDIO_RECONCILE",
        "session_id": session_id,
        "run_id": run_id,
        "agent_instance_id": agent_instance_id,
        "active_activation_ids": active_activation_ids,
        "current_activity": current_activity,
    }
