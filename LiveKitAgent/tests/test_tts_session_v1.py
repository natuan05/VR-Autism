import asyncio
import json
from types import SimpleNamespace

import pytest

from agent_v2 import (
    JobRuntime,
    _cache_schema_version,
    _is_paired_vr_participant,
    _paired_identity_matches_scope,
    start_captured_owned_speech,
)
from tts_cache_client_v1 import CacheServiceError, SessionScopeV1
from tts_cache_contract_v1 import default_config
from tts_capture_v2 import CAPTURE_CONTEXT, CaptureContext


class _Handle:
    def done(self):
        return False


class _Session:
    def __init__(self):
        self.saw = None
        self.child = None

    def say(self, _text, **_options):
        self.saw = CAPTURE_CONTEXT.get()

        async def inherited_context():
            await asyncio.sleep(0)
            return CAPTURE_CONTEXT.get()

        self.child = asyncio.create_task(inherited_context())
        return _Handle()


def test_synchronous_say_task_inherits_copy_and_caller_resets_immediately():
    async def run():
        config = default_config()
        profile = config.profile("teacher-npc")
        capture_context = CaptureContext(
            "hello", config, profile, "a" * 64, 0, {"invocation_id": "i-1"}
        )
        runtime = JobRuntime(object())
        session = _Session()

        handle = start_captured_owned_speech(
            runtime, ("script", "i-1"), "hello", session, capture_context
        )

        assert handle is not None
        assert session.saw is capture_context
        assert CAPTURE_CONTEXT.get() is None
        assert await session.child is capture_context

    asyncio.run(run())


def test_capture_context_is_context_local_across_copied_contexts():
    config = default_config()
    profile = config.profile("teacher-npc")
    context_a = CaptureContext("A", config, profile, "a" * 64, 0, {})
    context_b = CaptureContext("B", config, profile, "b" * 64, 0, {})
    token = CAPTURE_CONTEXT.set(context_a)
    try:
        copied = __import__("contextvars").copy_context()
        CAPTURE_CONTEXT.set(context_b)
        assert copied.run(CAPTURE_CONTEXT.get) is context_a
        assert CAPTURE_CONTEXT.get() is context_b
    finally:
        CAPTURE_CONTEXT.reset(token)


def test_cache_capability_is_opt_in_from_vr_token_metadata():
    assert _cache_schema_version(SimpleNamespace(metadata="")) is None
    assert _cache_schema_version(SimpleNamespace(metadata="{}")) is None
    assert (
        _cache_schema_version(
            SimpleNamespace(metadata=json.dumps({"tts_cache_schema_version": 1}))
        )
        == 1
    )
    with pytest.raises(CacheServiceError, match="unsupported"):
        _cache_schema_version(
            SimpleNamespace(metadata=json.dumps({"tts_cache_schema_version": 2}))
        )


def test_cache_data_packets_require_exact_paired_standard_participant():
    from livekit import rtc

    runtime = JobRuntime(object())
    runtime.vr_participant_identity = "vr_paired"
    kind = rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
    assert _is_paired_vr_participant(
        runtime, SimpleNamespace(kind=kind, identity="vr_paired")
    )
    assert not _is_paired_vr_participant(
        runtime, SimpleNamespace(kind=kind, identity="vr_other")
    )
    assert not _is_paired_vr_participant(runtime, None)

    config = default_config()
    runtime.cache_scope = SessionScopeV1(
        "room-1", "1234", "child-1", "lesson-1", "host-1", config
    )
    assert not _is_paired_vr_participant(
        runtime, SimpleNamespace(kind=kind, identity="vr_paired", metadata="")
    )
    assert _is_paired_vr_participant(
        runtime,
        SimpleNamespace(
            kind=kind,
            identity="vr_paired",
            metadata=json.dumps({"tts_cache_schema_version": 1}),
        ),
    )


def test_cache_scope_requires_exact_vr_pin_identity():
    config = default_config()
    scope = SessionScopeV1("room-1", "1234", "child-1", "lesson-1", "host-1", config)
    assert _paired_identity_matches_scope("vr_1234", scope)
    assert not _paired_identity_matches_scope("vr_5678", scope)
    assert not _paired_identity_matches_scope("vr_1234_extra", scope)
