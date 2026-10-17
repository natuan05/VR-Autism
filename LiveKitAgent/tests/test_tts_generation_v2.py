import asyncio
from types import SimpleNamespace

import pytest
from livekit.agents import tts

from tts_cache_contract_v1 import compute_key, default_config
from tts_generation_v2 import TtsGenerationError, generate_complete, stream_fixed_tts


class _Stream:
    def __init__(self):
        self.pushed = []
        self.ended = False

    def push_text(self, chunk):
        self.pushed.append(chunk)

    def end_input(self):
        self.ended = True

    async def __aenter__(self):
        return self

    async def __aexit__(self, *_):
        return None

    def __aiter__(self):
        async def events():
            while not self.ended:
                await asyncio.sleep(0)
            frame = SimpleNamespace(
                data=memoryview(b"\x01\x00\x02\x00"),
                sample_rate=24_000,
                num_channels=1,
            )
            yield tts.SynthesizedAudio(
                frame=frame, request_id="request", is_final=True, segment_id="segment"
            )

        return events()


class _Provider:
    def __init__(self):
        self.instance_stream = _Stream()
        self.closed = False

    def stream(self, **_kwargs):
        return self.instance_stream

    async def aclose(self):
        self.closed = True


@pytest.mark.asyncio
async def test_stream_fixed_tts_uses_validated_profile_instance_and_complete_text() -> (
    None
):
    profile = default_config().profile("teacher-npc")
    provider = _Provider()
    seen = []

    async def text():
        yield "Xin "
        yield "chào."

    result = [
        event
        async for event in stream_fixed_tts(
            text(),
            profile,
            provider_factory=lambda value: seen.append(value) or provider,
        )
    ]
    assert seen == [profile]
    assert provider.instance_stream.pushed == ["Xin ", "chào."]
    assert provider.instance_stream.ended
    assert provider.closed
    assert result[0].segment_id == "segment"


@pytest.mark.asyncio
async def test_generate_complete_returns_canonical_wav_for_roomless_preview():
    profile = default_config().profile("teacher-npc")
    result = await generate_complete(
        "Xin chào.",
        profile,
        compute_key("Xin chào.", profile),
        0,
        provider_factory=lambda _profile: _Provider(),
    )
    assert result.wav[:12] == b"RIFF(\x00\x00\x00WAVE"
    assert result.sample_count == 2
    assert len(result.sha256) == 64


@pytest.mark.asyncio
async def test_stream_fixed_tts_closes_provider_when_stream_fails():
    class BrokenStream(_Stream):
        def __aiter__(self):
            async def events():
                raise RuntimeError("provider stream failed")
                yield

            return events()

    class Provider(_Provider):
        def __init__(self):
            self.instance_stream = BrokenStream()

    provider = Provider()

    async def text():
        yield "hello"

    with pytest.raises(RuntimeError, match="provider stream failed"):
        async for _event in stream_fixed_tts(
            text(),
            default_config().profile("teacher-npc"),
            provider_factory=lambda _profile: provider,
        ):
            pass
    assert provider.closed


@pytest.mark.asyncio
async def test_final_event_followed_by_rpc_499_does_not_become_complete_wav():
    class FailedAfterFinalStream(_Stream):
        def __aiter__(self):
            async def events():
                while not self.ended:
                    await asyncio.sleep(0)
                frame = SimpleNamespace(
                    data=memoryview(b"\x01\x00\x02\x00"),
                    sample_rate=24_000,
                    num_channels=1,
                )
                yield tts.SynthesizedAudio(
                    frame=frame,
                    request_id="first-request",
                    is_final=True,
                    segment_id="first-segment",
                )
                raise RuntimeError("provider rpc 499 before next segment")

            return events()

    class FailedProvider:
        def stream(self, **_kwargs):
            return FailedAfterFinalStream()

    profile = default_config().profile("teacher-npc")
    with pytest.raises(TtsGenerationError, match="did not complete"):
        await generate_complete(
            "Xin chào.",
            profile,
            compute_key("Xin chào.", profile),
            0,
            provider_factory=lambda _profile: FailedProvider(),
        )


@pytest.mark.asyncio
async def test_stream_fixed_tts_rejects_invalid_profile_before_provider_creation() -> (
    None
):
    profile = default_config().profile("teacher-npc")
    invalid = profile.__class__(**{**profile.to_dict(), "speaking_rate": 9.0})
    called = False

    def factory(_profile):
        nonlocal called
        called = True
        return _Provider()

    async def text():
        yield "hello"

    with pytest.raises(ValueError):
        async for _event in stream_fixed_tts(text(), invalid, provider_factory=factory):
            pass
    assert not called
