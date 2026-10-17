import asyncio
import json
from types import SimpleNamespace

import pytest

from fixed_audio_runtime_v1 import (
    FixedAudioRuntimeV1,
    activity_packet,
    done_packet,
    invocation_fields,
    reconcile_packet,
)
from silence_reminder_v2 import ActivationSilenceReminderV2
from tts_cache_client_v1 import SessionScopeV1
from tts_cache_contract_v1 import default_config, parse_cache_packet
from voice_contract_v2 import SetActiveQuest


def _invocation():
    return {
        "session_id": "room-1",
        "run_id": "run-1",
        "activation_id": "activation-1",
        "invocation_id": "invocation-1",
        "npc_binding_id": "teacher-npc",
        "kind": "opening",
    }


def test_cache_lifecycle_packets_are_exact_and_correlated():
    packet = activity_packet(
        _invocation(),
        owner_revision=1,
        activity_sequence=1,
        phase="preparing",
        source="none",
    )
    assert parse_cache_packet(packet) == packet

    done = done_packet(_invocation(), "agent-1", 1, "SUCCESS")
    assert parse_cache_packet(done) == done
    assert invocation_fields(packet)["invocation_id"] == "invocation-1"


def test_agent_runtime_accepts_only_authoritative_reconciliation_run():
    runtime = FixedAudioRuntimeV1("room-1")
    assert runtime.run_id is None
    assert runtime.accept_run("run-1")
    assert runtime.accept_run("run-1")
    assert runtime.accept_run("run-reconciled")
    assert runtime.run_id == "run-reconciled"
    assert not runtime.accept_run("")


def test_agent_runtime_rejects_retired_run_after_reconciliation_advances():
    runtime = FixedAudioRuntimeV1("room-1")
    assert runtime.accept_run("run-a")
    assert runtime.accept_run("run-b")
    assert not runtime.accept_run("run-a")
    assert runtime.run_id == "run-b"
    assert "run-a" in runtime.retired_run_ids


def test_retired_run_history_is_bounded_and_never_contains_current_run():
    runtime = FixedAudioRuntimeV1("room-1")
    for index in range(1026):
        assert runtime.accept_run(f"run-{index}")
    assert len(runtime.retired_run_ids) == 1024
    assert runtime.run_id not in runtime.retired_run_ids


@pytest.mark.asyncio
async def test_duplicate_reconciliation_preserves_current_external_audio_activity():
    from agent_v2 import JobRuntime, _process_cache_packet

    class LocalParticipant:
        async def publish_data(self, _data, **_options):
            return None

    room = SimpleNamespace(local_participant=LocalParticipant())
    runtime = JobRuntime(SimpleNamespace(room=room))
    config = default_config()
    scope = SessionScopeV1("room-1", "1234", "child-1", "lesson-1", "host-1", config)
    fixed = FixedAudioRuntimeV1.from_scope(scope)
    fixed.run_id = "run-1"
    fixed.active_activation_ids.add("activation-1")
    runtime.cache_scope = scope
    runtime.cache_fixed_audio = fixed
    runtime.cache_ready = asyncio.Event()
    runtime.cache_ready.set()
    timer = ActivationSilenceReminderV2(lambda _activation: _record([], _activation))
    timer.activate("activation-1", 30)
    runtime.silence_reminder = timer
    external = activity_packet(
        _invocation(),
        owner_revision=5,
        activity_sequence=1,
        phase="preparing",
        source="none",
    )
    await _process_cache_packet(None, runtime, external)
    assert timer._external_audio_invocation == external["invocation_id"]

    duplicate_reconcile = reconcile_packet(
        "room-1",
        "run-1",
        fixed.agent_instance_id,
        ["activation-1"],
        external,
    )
    await _process_cache_packet(None, runtime, duplicate_reconcile)

    assert fixed.external_activity == external
    assert timer._external_audio_invocation == external["invocation_id"]
    timer.dispose()


def test_invocation_correlation_uses_full_scope_and_terminal_retention_is_bounded():
    runtime = FixedAudioRuntimeV1("room-1")
    invocation = _invocation()
    runtime.current_invocation = {**invocation, "owner_revision": 4}

    assert runtime.is_current({**invocation, "owner_revision": 4})
    assert not runtime.is_current(
        {**invocation, "session_id": "room-other", "owner_revision": 4}
    )
    assert not runtime.is_current(
        {**invocation, "run_id": "run-old", "owner_revision": 4}
    )
    assert not runtime.is_current(
        {**invocation, "activation_id": "activation-old", "owner_revision": 4}
    )
    assert not runtime.is_current(
        {**invocation, "npc_binding_id": "npc-old", "owner_revision": 4}
    )
    assert not runtime.is_current(
        {**invocation, "kind": "reminder", "owner_revision": 4}
    )

    pinned = {**invocation, "invocation_id": "pinned-invocation"}
    runtime.current_invocation = {**pinned, "owner_revision": 5000}
    runtime.remember_terminal(pinned, "SUCCESS", 5000, "")
    for index in range(1025):
        item = {**invocation, "invocation_id": f"invocation-{index}"}
        runtime.remember_terminal(item, "SUCCESS", index, "")
    assert len(runtime.terminal_status) == 1024
    assert runtime.terminal_for(pinned)["status"] == "SUCCESS"


class _Clock:
    def __init__(self):
        self.waiters = []

    async def sleep(self, delay):
        future = asyncio.get_running_loop().create_future()
        self.waiters.append((delay, future))
        try:
            await future
        finally:
            self.waiters = [item for item in self.waiters if item[1] is not future]


async def _tick(clock):
    waiters, clock.waiters = clock.waiters, []
    for _delay, future in waiters:
        if not future.done():
            future.set_result(None)
    await asyncio.sleep(0)
    await asyncio.sleep(0)


def test_external_vr_audio_pauses_reminders_until_terminal_then_rearms():
    async def run():
        clock = _Clock()
        called = []
        timer = ActivationSilenceReminderV2(
            lambda activation: _record(called, activation), sleep=clock.sleep
        )
        timer.activate("activation-1", 3)
        timer.opening_finished()
        await asyncio.sleep(0)
        assert len(clock.waiters) == 1

        assert timer.set_external_audio_activity("invocation-1", "preparing", 1)
        assert all(future.cancelled() for _delay, future in clock.waiters)
        await asyncio.sleep(0)
        assert clock.waiters == []

        assert timer.set_external_audio_activity("invocation-1", "playing", 2)
        assert timer.set_external_audio_activity("invocation-1", "terminal", 3)
        await asyncio.sleep(0)
        assert [delay for delay, _future in clock.waiters] == [3]
        await _tick(clock)
        assert called == ["activation-1"]
        timer.dispose()

    asyncio.run(run())


def test_external_audio_sequence_restarts_for_a_new_invocation():
    timer = ActivationSilenceReminderV2(lambda _activation: _record([], _activation))
    timer.activate("activation-1", 30)
    assert timer.set_external_audio_activity("first", "preparing", 1)
    assert timer.set_external_audio_activity("first", "terminal", 3)
    assert timer.set_external_audio_activity("second", "preparing", 1)


@pytest.mark.parametrize("source", ["local", "stream"])
def test_cache_reminder_scheduler_survives_its_own_preparing_activity_without_done(
    source,
):
    from agent_v2 import JobRuntime, _play_silence_reminder, _process_cache_packet

    async def run():
        sent = []

        class LocalParticipant:
            async def publish_data(self, data, **options):
                sent.append((json.loads(data.decode()), options))

        room = SimpleNamespace(local_participant=LocalParticipant())
        runtime = JobRuntime(SimpleNamespace(room=room))
        config = default_config()
        scope = SessionScopeV1(
            "room-1", "1234", "child-1", "lesson-1", "host-1", config
        )
        fixed = FixedAudioRuntimeV1.from_scope(scope)
        fixed.run_id = "run-1"
        fixed.active_activation_ids.add("activation-1")
        runtime.cache_scope = scope
        runtime.cache_fixed_audio = fixed
        runtime.cache_ready = asyncio.Event()
        runtime.cache_ready.set()
        runtime.voice_runtime.activate(
            SetActiveQuest("activation-1", "say hello", ("Hello",), "teacher-npc")
        )
        clock = _Clock()
        timer = ActivationSilenceReminderV2(
            lambda activation: _play_silence_reminder(None, runtime, activation),
            sleep=clock.sleep,
        )
        runtime.silence_reminder = timer
        timer.activate("activation-1", 3)
        timer.opening_finished()
        await asyncio.sleep(0)
        await _tick(clock)
        request = next(
            packet
            for packet, options in sent
            if options.get("topic") == "lesson-graph-v2.audio-cache"
            and packet.get("event") == "FIXED_AUDIO_REMINDER_REQUEST"
        )
        shared = invocation_fields(request)
        preparing = {
            "contract_version": 1,
            "event": "FIXED_AUDIO_ACTIVITY",
            **shared,
            "owner_revision": 7,
            "activity_sequence": 1,
            "phase": "preparing",
            "source": "none",
            "status": "PENDING",
            "reason": "",
        }
        await _process_cache_packet(None, runtime, preparing)
        terminal = {
            **preparing,
            "activity_sequence": 2,
            "phase": "terminal",
            "source": source,
            "status": "SUCCESS",
        }
        await _process_cache_packet(None, runtime, terminal)
        await asyncio.sleep(0)

        assert timer.activation_id == "activation-1"
        assert timer._external_audio_invocation is None
        assert fixed.reminder_waiters == {}
        accepted = [
            packet
            for packet, options in sent
            if options.get("topic") != "lesson-graph-v2.audio-cache"
            and packet.get("event") == "ON_REMINDER"
        ]
        assert len(accepted) == 1
        assert accepted[0]["command_id"] == request["invocation_id"]
        timer.dispose()

    asyncio.run(run())


def test_stale_activity_cannot_mutate_timer_or_replace_accepted_terminal():
    from agent_v2 import JobRuntime, _process_cache_packet

    async def run():
        room = SimpleNamespace(local_participant=SimpleNamespace(publish_data=None))
        runtime = JobRuntime(SimpleNamespace(room=room))
        config = default_config()
        scope = SessionScopeV1(
            "room-1", "1234", "child-1", "lesson-1", "host-1", config
        )
        fixed = FixedAudioRuntimeV1.from_scope(scope)
        fixed.run_id = "run-1"
        fixed.active_activation_ids.add("activation-current")
        runtime.cache_scope = scope
        runtime.cache_fixed_audio = fixed
        runtime.cache_ready = asyncio.Event()
        runtime.cache_ready.set()
        timer = ActivationSilenceReminderV2(
            lambda _activation: _record([], _activation)
        )
        timer.activate("activation-current", 30)
        runtime.silence_reminder = timer

        def activity(invocation, revision, sequence, phase, source, status):
            return {
                "contract_version": 1,
                "event": "FIXED_AUDIO_ACTIVITY",
                **invocation,
                "owner_revision": revision,
                "activity_sequence": sequence,
                "phase": phase,
                "source": source,
                "status": status,
                "reason": "",
            }

        current = {
            "session_id": "room-1",
            "run_id": "run-1",
            "activation_id": "activation-current",
            "invocation_id": "current",
            "npc_binding_id": "teacher-npc",
            "kind": "dialogue",
        }
        await _process_cache_packet(
            None, runtime, activity(current, 3, 1, "preparing", "none", "PENDING")
        )
        await _process_cache_packet(
            None, runtime, activity(current, 3, 1, "playing", "local", "PENDING")
        )
        stale_owner = {**current, "invocation_id": "old"}
        await _process_cache_packet(
            None,
            runtime,
            activity(stale_owner, 2, 1, "preparing", "none", "PENDING"),
        )
        await _process_cache_packet(
            None,
            runtime,
            activity(
                current,
                3,
                2,
                "terminal",
                "local",
                "SUCCESS",
            ),
        )
        await _process_cache_packet(
            None,
            runtime,
            activity(current, 3, 3, "playing", "local", "PENDING"),
        )
        stale_activation = {**current, "activation_id": "activation-old"}
        await _process_cache_packet(
            None,
            runtime,
            activity(stale_activation, 4, 1, "preparing", "none", "PENDING"),
        )

        assert fixed.external_activity["phase"] == "terminal"
        assert fixed.external_activity["status"] == "SUCCESS"
        assert timer._external_audio_invocation is None
        assert timer._external_audio_sequence == -1
        timer.dispose()

    asyncio.run(run())


async def _record(values, activation):
    values.append(activation)
    return True


def test_cache_aware_reminder_waits_for_vr_terminal_and_emits_one_acceptance():
    from agent_v2 import (
        JobRuntime,
        _play_silence_reminder,
        _process_cache_packet,
    )

    async def run():
        sent = []

        class LocalParticipant:
            async def publish_data(self, data, **options):
                sent.append((json.loads(data.decode()), options))

        room = SimpleNamespace(local_participant=LocalParticipant())
        runtime = JobRuntime(SimpleNamespace(room=room))
        config = default_config()
        scope = SessionScopeV1(
            "room-1", "1234", "child-1", "lesson-1", "host-1", config
        )
        fixed = FixedAudioRuntimeV1.from_scope(scope)
        fixed.run_id = "run-1"
        fixed.active_activation_ids.add("activation-1")
        runtime.cache_scope = scope
        runtime.cache_fixed_audio = fixed
        runtime.cache_ready = asyncio.Event()
        runtime.cache_ready.set()
        runtime.voice_runtime.activate(
            SetActiveQuest(
                "activation-1", "say hello", ("Hello", "Xin chào"), "teacher-npc"
            )
        )

        reminder = asyncio.create_task(
            _play_silence_reminder(None, runtime, "activation-1")
        )
        await asyncio.sleep(0)
        request = parse_cache_packet(sent[0][0])
        assert request["event"] == "FIXED_AUDIO_REMINDER_REQUEST"
        assert request["kind"] == "automatic_reminder"
        assert request["text"] in {"Hello", "Xin chào"}

        shared = invocation_fields(request)
        playing = {
            "contract_version": 1,
            "event": "FIXED_AUDIO_ACTIVITY",
            **shared,
            "owner_revision": 7,
            "activity_sequence": 1,
            "phase": "playing",
            "source": "local",
            "status": "PENDING",
            "reason": "",
        }
        await _process_cache_packet(None, runtime, playing)
        terminal = {
            **playing,
            "activity_sequence": 2,
            "phase": "terminal",
            "status": "SUCCESS",
        }
        await _process_cache_packet(None, runtime, terminal)
        done = done_packet(
            request,
            fixed.agent_instance_id,
            7,
            "SUCCESS",
        )
        await _process_cache_packet(None, runtime, done)
        assert await reminder is True

        accepted = [
            packet
            for packet, options in sent
            if options.get("topic") != "lesson-graph-v2.audio-cache"
            and packet.get("event") == "ON_REMINDER"
        ]
        assert len(accepted) == 1

    asyncio.run(run())
