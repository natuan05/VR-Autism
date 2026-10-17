"""Provider construction and complete-text streaming shared by room and preview."""

from __future__ import annotations

import contextlib
from collections.abc import AsyncIterable, AsyncIterator
from typing import Any, Callable

from google.cloud import texttospeech as gcp_tts
from livekit.agents import tts as agents_tts
from livekit.plugins import google

from tts_cache_contract_v1 import (
    CacheValidationError,
    SessionTtsConfigV1,
    TtsProfileV1,
    compute_key,
    parse_profile,
)
from tts_capture_v2 import CaptureContext, CompletedCapture, FixedCapture


def create_tts_provider(profile: TtsProfileV1) -> agents_tts.TTS:
    """Create an immutable provider instance for one cache profile."""
    profile = parse_profile(profile.to_dict())
    return google.TTS(
        language=profile.language,
        voice_name=profile.voice_name,
        speaking_rate=profile.speaking_rate,
        audio_encoding=gcp_tts.AudioEncoding.OGG_OPUS,
        sample_rate=profile.sample_rate,
        use_streaming=True,
    )


async def stream_fixed_tts(
    text: AsyncIterable[str],
    profile: TtsProfileV1,
    *,
    provider_factory: Callable[[TtsProfileV1], agents_tts.TTS] = create_tts_provider,
    conn_options: Any = None,
) -> AsyncIterator[agents_tts.SynthesizedAudio]:
    """Synthesize one complete exact text input with a profile-local provider."""
    profile = parse_profile(profile.to_dict())
    provider = provider_factory(profile)
    stream_options = {"conn_options": conn_options} if conn_options is not None else {}
    import asyncio

    try:
        async with provider.stream(**stream_options) as stream:

            async def forward_text() -> None:
                async for chunk in text:
                    if chunk:
                        stream.push_text(chunk)
                stream.end_input()

            forward_task = asyncio.create_task(forward_text())
            try:
                async for event in stream:
                    yield event
                await forward_task
            finally:
                if not forward_task.done():
                    forward_task.cancel()
                    await asyncio.gather(forward_task, return_exceptions=True)
    finally:
        close = getattr(provider, "aclose", None)
        if callable(close):
            with contextlib.suppress(Exception):
                await close()


class TtsGenerationError(RuntimeError):
    """Provider output did not prove one complete bounded audio segment."""


async def generate_complete(
    text: str,
    profile: TtsProfileV1,
    key: str,
    rejection_epoch: int,
    *,
    provider_factory: Callable[[TtsProfileV1], agents_tts.TTS] = create_tts_provider,
) -> CompletedCapture:
    """Roomless complete WAV generation shared by previews and live capture."""
    profile = parse_profile(profile.to_dict())
    if compute_key(text, profile) != key:
        raise CacheValidationError("cache key mismatch")
    if type(rejection_epoch) is not int or rejection_epoch < 0:
        raise CacheValidationError("invalid rejection epoch")
    config = SessionTtsConfigV1(1, 0, (profile,), ())
    context = CaptureContext(text, config, profile, key, rejection_epoch, {})
    capture = FixedCapture(context)

    async def one_text():
        yield text

    try:
        async for event in stream_fixed_tts(
            one_text(), profile, provider_factory=provider_factory
        ):
            capture.append(event)
    except Exception as exc:
        raise TtsGenerationError("TTS stream did not complete") from exc
    result = capture.finish(True, True, 1)
    if result is None:
        raise TtsGenerationError("TTS output is incomplete or invalid")
    return result
