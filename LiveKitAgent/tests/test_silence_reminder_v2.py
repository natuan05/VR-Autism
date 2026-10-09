import asyncio

import pytest

from silence_reminder_v2 import ActivationSilenceReminderV2

SYNC_TIMEOUT_SECONDS = 1.0


class ManualClock:
    def __init__(self) -> None:
        self.waiters: list[tuple[float, asyncio.Future[None]]] = []

    async def sleep(self, delay: float) -> None:
        future = asyncio.get_running_loop().create_future()
        self.waiters.append((delay, future))
        try:
            await future
        finally:
            self.waiters = [
                (value, item) for value, item in self.waiters if item is not future
            ]

    async def tick(self) -> float:
        waiters, self.waiters = self.waiters, []
        for _, future in waiters:
            if not future.done():
                future.set_result(None)
        await asyncio.sleep(0)
        await asyncio.sleep(0)
        return waiters[0][0] if waiters else 0.0


@pytest.mark.asyncio
async def test_timer_starts_only_after_opening_and_replay_does_not_reset_it() -> None:
    clock = ManualClock()
    reminders: list[str] = []

    async def remind(activation_id: str) -> bool:
        reminders.append(activation_id)
        return True

    timer = ActivationSilenceReminderV2(remind, sleep=clock.sleep)
    assert timer.activate("activation-1", 4.0) is True
    assert timer.activate("activation-1", 4.0) is False
    assert clock.waiters == []

    timer.opening_finished()
    await asyncio.sleep(0)
    assert [delay for delay, _ in clock.waiters] == [4.0]
    await clock.tick()

    assert reminders == ["activation-1"]
    assert [delay for delay, _ in clock.waiters] == [4.0]
    timer.cancel("activation-1")


def test_scheduler_rejects_invalid_activation_timeout() -> None:
    timer = ActivationSilenceReminderV2(lambda _activation_id: _record([], _activation_id))
    for timeout in (True, -1.0, float("inf"), 10**1000):
        with pytest.raises(ValueError):
            timer.activate("activation-1", timeout)


@pytest.mark.asyncio
async def test_speech_and_busy_states_reset_or_suppress_the_countdown() -> None:
    clock = ManualClock()
    reminders: list[str] = []
    timer = ActivationSilenceReminderV2(
        lambda activation_id: _record(reminders, activation_id), sleep=clock.sleep
    )
    timer.activate("activation-1", 2.0)
    timer.opening_finished()
    await asyncio.sleep(0)

    timer.set_user_speaking(True)
    assert all(future.cancelled() for _, future in clock.waiters)
    timer.set_user_speaking(False)
    await asyncio.sleep(0)
    timer.set_agent_state("thinking")
    assert all(future.cancelled() for _, future in clock.waiters)
    timer.set_agent_state("listening")
    await asyncio.sleep(0)
    timer.note_user_activity()
    await asyncio.sleep(0)
    assert [delay for delay, _ in clock.waiters] == [2.0]
    await clock.tick()

    assert reminders == ["activation-1"]
    timer.cancel()


@pytest.mark.asyncio
async def test_reconnect_rearms_and_cancelled_activation_cannot_fire() -> None:
    clock = ManualClock()
    reminders: list[str] = []
    timer = ActivationSilenceReminderV2(
        lambda activation_id: _record(reminders, activation_id), sleep=clock.sleep
    )
    timer.activate("activation-old", 1.0)
    timer.opening_finished()
    await asyncio.sleep(0)
    timer.reconnecting()
    assert all(future.cancelled() for _, future in clock.waiters)
    timer.reconnected()
    await asyncio.sleep(0)
    assert [delay for delay, _ in clock.waiters] == [1.0]

    timer.activate("activation-new", 1.0)
    timer.opening_finished()
    await asyncio.sleep(0)
    timer.cancel("activation-old")
    await clock.tick()

    assert reminders == ["activation-new"]
    timer.dispose()


@pytest.mark.asyncio
async def test_agent_evaluation_interrupts_reminder_and_rearms_after_idle() -> None:
    clock = ManualClock()
    started = asyncio.Event()

    async def remind(_activation_id: str) -> bool:
        started.set()
        await asyncio.Future()
        return True

    timer = ActivationSilenceReminderV2(remind, sleep=clock.sleep)
    timer.activate("activation-1", 2.0)
    timer.opening_finished()
    await asyncio.sleep(0)
    await clock.tick()
    await asyncio.wait_for(started.wait(), timeout=SYNC_TIMEOUT_SECONDS)

    timer.set_agent_state("thinking")
    await asyncio.sleep(0)
    assert timer.is_reminder_playing is False
    assert clock.waiters == []

    timer.set_agent_state("listening")
    await asyncio.sleep(0)
    assert [delay for delay, _ in clock.waiters] == [2.0]
    timer.cancel()


async def _record(values: list[str], activation_id: str) -> bool:
    values.append(activation_id)
    return True
