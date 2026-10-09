"""Activation-scoped scheduler for V2 voice silence reminders."""

from __future__ import annotations

import asyncio
import math
from collections.abc import Awaitable, Callable
from typing import Any, ClassVar

ReminderCallback = Callable[[str], Awaitable[bool]]
SleepCallback = Callable[[float], Awaitable[Any]]


class ActivationSilenceReminderV2:
    """Schedule one reminder per timeout while an activation is idle.

    Positive timeouts repeat after a successfully played reminder. A zero timeout
    fires once per user-activity window to avoid a zero-delay speech loop.
    """

    _BUSY_AGENT_STATES: ClassVar[set[str]] = {"initializing", "thinking", "speaking"}

    def __init__(
        self,
        remind: ReminderCallback,
        *,
        sleep: SleepCallback = asyncio.sleep,
    ) -> None:
        self._remind = remind
        self._sleep = sleep
        self._activation_id: str | None = None
        self._timeout_seconds = 5.0
        self._opening_finished = False
        self._user_speaking = False
        self._agent_state = "idle"
        self._reconnecting = False
        self._zero_window_fired = False
        self._generation = 0
        self._task: asyncio.Task[None] | None = None
        self._reminder_in_progress = False

    @property
    def activation_id(self) -> str | None:
        return self._activation_id

    @property
    def is_reminder_playing(self) -> bool:
        return self._reminder_in_progress

    def activate(self, activation_id: str, timeout_seconds: float) -> bool:
        """Install a timer once; an identical activation replay leaves it untouched."""
        if (
            not isinstance(activation_id, str)
            or not activation_id.strip()
            or isinstance(timeout_seconds, bool)
            or not isinstance(timeout_seconds, (int, float))
        ):
            raise ValueError("activation and silence timeout must be valid")
        try:
            timeout_seconds = float(timeout_seconds)
        except OverflowError as exc:
            raise ValueError("activation and silence timeout must be valid") from exc
        if not math.isfinite(timeout_seconds) or timeout_seconds < 0:
            raise ValueError("activation and silence timeout must be valid")
        if self._activation_id == activation_id:
            if self._timeout_seconds != timeout_seconds:
                raise ValueError("activation silence timeout changed")
            return False

        self.cancel()
        self._activation_id = activation_id
        self._timeout_seconds = timeout_seconds
        self._opening_finished = False
        self._zero_window_fired = False
        return True

    def opening_finished(self, activation_id: str | None = None) -> None:
        if not self._matches(activation_id):
            return
        self._opening_finished = True
        self._schedule()

    def note_user_activity(self) -> None:
        if self._activation_id is None:
            return
        self._zero_window_fired = False
        self._restart_window(cancel_in_progress=True)

    def set_user_speaking(self, speaking: bool) -> None:
        speaking = bool(speaking)
        if self._user_speaking == speaking:
            return
        self._user_speaking = speaking
        if speaking:
            self._zero_window_fired = False
            self._restart_window(cancel_in_progress=True)
        else:
            if self._reminder_in_progress:
                return
            self._restart_window()

    def set_agent_state(self, state: str) -> None:
        previous_busy = self._agent_state in self._BUSY_AGENT_STATES
        self._agent_state = state
        if self._reminder_in_progress:
            if state in {"initializing", "thinking"}:
                self._restart_window(cancel_in_progress=True)
            return
        busy = state in self._BUSY_AGENT_STATES
        if busy or (previous_busy and not busy):
            self._restart_window()

    def reconnecting(self) -> None:
        self._reconnecting = True
        self._restart_window(cancel_in_progress=True)

    def reconnected(self) -> None:
        self._reconnecting = False
        self._restart_window()

    def cancel(self, activation_id: str | None = None) -> None:
        if activation_id is not None and activation_id != self._activation_id:
            return
        self._generation += 1
        task = self._task
        self._task = None
        self._activation_id = None
        self._opening_finished = False
        self._zero_window_fired = False
        self._reminder_in_progress = False
        if task is not None and not task.done() and task is not asyncio.current_task():
            task.cancel()

    def dispose(self) -> None:
        self.cancel()

    def _matches(self, activation_id: str | None) -> bool:
        return bool(
            self._activation_id
            and (activation_id is None or activation_id == self._activation_id)
        )

    def _is_busy(self) -> bool:
        return (
            self._user_speaking
            or self._agent_state in self._BUSY_AGENT_STATES
            or self._reconnecting
        )

    def _restart_window(self, *, cancel_in_progress: bool = False) -> None:
        self._generation += 1
        task = self._task
        if (
            task is not None
            and not task.done()
            and task is not asyncio.current_task()
            and (not self._reminder_in_progress or cancel_in_progress)
        ):
            task.cancel()
            self._task = None
        self._schedule()

    def _schedule(self) -> None:
        if (
            self._activation_id is None
            or not self._opening_finished
            or self._is_busy()
            or (self._task is not None and not self._task.done())
            or (self._timeout_seconds == 0 and self._zero_window_fired)
        ):
            return

        activation_id = self._activation_id
        generation = self._generation
        self._task = asyncio.create_task(
            self._wait_and_remind(activation_id, generation)
        )

    async def _wait_and_remind(self, activation_id: str, generation: int) -> None:
        current = asyncio.current_task()
        try:
            await self._sleep(self._timeout_seconds)
            if not self._is_current(activation_id, generation) or self._is_busy():
                return
            if self._timeout_seconds == 0:
                self._zero_window_fired = True
            self._reminder_in_progress = True
            played = await self._remind(activation_id)
            self._reminder_in_progress = False
            if (
                played
                and self._is_current(activation_id, generation)
                and not self._is_busy()
                and self._timeout_seconds > 0
            ):
                self._generation += 1
                self._task = None
                self._schedule()
        except asyncio.CancelledError:
            raise
        finally:
            self._reminder_in_progress = False
            if self._task is current:
                self._task = None

    def _is_current(self, activation_id: str, generation: int) -> bool:
        return self._activation_id == activation_id and self._generation == generation
