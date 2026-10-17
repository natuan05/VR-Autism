import asyncio
from types import SimpleNamespace

import pytest

from tts_cache_contract_v1 import compute_key, default_config
from tts_capture_v2 import (
    CaptureContext,
    CompletedCapture,
    FixedCapture,
    PublicationWorker,
)


def _context() -> CaptureContext:
    config = default_config()
    profile = config.profile("teacher-npc")
    return CaptureContext(
        "Xin chào.", config, profile, compute_key("Xin chào.", profile), 0, {}
    )


def _event(
    *,
    final=True,
    segment="segment-1",
    request="request-1",
    rate=24_000,
    channels=1,
    raw=b"\x01\x00\x02\x00",
):
    frame = SimpleNamespace(
        data=memoryview(raw), sample_rate=rate, num_channels=channels
    )
    return SimpleNamespace(
        frame=frame, is_final=final, segment_id=segment, request_id=request
    )


def test_capture_copies_pcm_and_requires_input_stream_segment_and_final_evidence() -> (
    None
):
    capture = FixedCapture(_context())
    event = _event()
    capture.append(event)
    assert capture.pcm_bytes == 4

    complete = capture.finish(True, True, 1)
    assert complete is not None
    assert complete.wav[:4] == b"RIFF"
    assert complete.wav[-4:] == b"\x01\x00\x02\x00"
    assert complete.sample_count == 2


def test_missing_final_or_expected_segment_never_publishes() -> None:
    capture = FixedCapture(_context())
    capture.append(_event(final=False))
    assert capture.finish(True, True, 1) is None

    capture = FixedCapture(_context())
    capture.append(_event())
    assert capture.finish(True, True, 2) is None


def test_failure_and_invalid_pcm_never_publish() -> None:
    for kwargs in ({"input_complete": False}, {"stream_complete": False}):
        capture = FixedCapture(_context())
        capture.append(_event())
        assert (
            capture.finish(
                kwargs.get("input_complete", True),
                kwargs.get("stream_complete", True),
                1,
            )
            is None
        )

    capture = FixedCapture(_context())
    capture.append(_event(raw=b"\x00"))
    assert capture.finish(True, True, 1) is None


def test_request_retry_or_multiple_segments_invalidates_capture() -> None:
    capture = FixedCapture(_context())
    capture.append(_event(segment="seg1", request="req1"))
    capture.append(_event(segment="seg2", request="req2"))
    assert capture.finish(True, True, 1) is None


def test_capture_rejects_noncanonical_format_and_overflow() -> None:
    capture = FixedCapture(_context())
    capture.append(_event(rate=16_000))
    assert capture.finish(True, True, 1) is None

    capture = FixedCapture(_context())
    capture.append(_event(raw=b"\x00\x00" * (2_097_152 + 1)))
    assert capture.finish(True, True, 1) is None


def test_capture_context_owns_an_immutable_copy_of_invocation():
    source = {"invocation_id": "before"}
    context = _context()
    copied = CaptureContext(
        context.final_text,
        context.config,
        context.profile,
        context.key,
        context.rejection_epoch,
        source,
    )
    source["invocation_id"] = "after"
    assert copied.invocation["invocation_id"] == "before"
    with pytest.raises(TypeError):
        copied.invocation["invocation_id"] = "mutated"


def test_publication_queue_is_bounded_and_nonblocking():
    async def run():
        context = _context()
        capture = FixedCapture(context)
        capture.append(_event())
        completed = capture.finish(True, True, 1)
        assert completed is not None
        worker = PublicationWorker(lambda _capture: None)
        assert all(worker.try_enqueue(completed) for _ in range(8))
        assert worker.try_enqueue(completed)  # same immutable key coalesces
        context2 = CaptureContext(
            context.final_text,
            context.config,
            context.profile,
            "b" * 64,
            0,
            {"invocation_id": "different"},
        )
        other = CompletedCapture(
            context2, completed.wav, completed.sample_count, completed.sha256
        )
        # Eight identical publications coalesce to one queued job, so fill the
        # remaining capacity with distinct identities before exercising overflow.
        assert worker.try_enqueue(other)
        for index in range(6):
            next_context = CaptureContext(
                context.final_text,
                context.config,
                context.profile,
                f"{index + 1:064x}",
                0,
                {"invocation_id": str(index)},
            )
            assert worker.try_enqueue(
                CompletedCapture(
                    next_context,
                    completed.wav,
                    completed.sample_count,
                    completed.sha256,
                )
            )
        overflow = CaptureContext(
            context.final_text,
            context.config,
            context.profile,
            "c" * 64,
            0,
            {},
        )
        assert not worker.try_enqueue(
            CompletedCapture(
                overflow, completed.wav, completed.sample_count, completed.sha256
            )
        )
        await worker.drain(1)

    asyncio.run(run())


def test_publication_success_callback_runs_on_event_loop_with_validated_artifact():
    async def run():
        context = _context()
        capture = FixedCapture(context)
        capture.append(_event())
        completed = capture.finish(True, True, 1)
        assert completed is not None
        artifact = {
            "profile_id": context.profile.profile_id,
            "key": context.key,
            "rejection_epoch": context.rejection_epoch,
            "object_path": f"tts-cache/v1/{context.profile.profile_id}/{context.key}/r0.wav",
            "generation": "1",
            "sha256": completed.sha256,
            "format": "wav-pcm16-v1",
            "sample_count": completed.sample_count,
            "byte_size": len(completed.wav),
            "duration_ms": completed.sample_count * 1000 // 24_000,
        }
        callbacks = []
        loop_thread = __import__("threading").get_ident()
        worker = PublicationWorker(
            lambda _capture: artifact,
            on_success=lambda sent_capture, sent_artifact: callbacks.append(
                (sent_capture, sent_artifact, __import__("threading").get_ident())
            ),
        )
        assert worker.try_enqueue(completed)
        await worker.drain(1)
        assert callbacks == [(completed, artifact, loop_thread)]

    asyncio.run(run())


def test_publication_failure_never_invokes_success_callback_or_logs_exception_text(
    caplog,
):
    async def run():
        context = _context()
        capture = FixedCapture(context)
        capture.append(_event())
        completed = capture.finish(True, True, 1)
        assert completed is not None
        called = []

        def fail(_capture):
            raise RuntimeError("sensitive provider text and token")

        worker = PublicationWorker(fail, on_success=lambda *_args: called.append(True))
        assert worker.try_enqueue(completed)
        await worker.drain(1)
        assert called == []
        assert "sensitive provider text" not in caplog.text
        assert "token" not in caplog.text

    asyncio.run(run())
