"""Bounded, evidence-gated capture of already-streamed PCM audio."""

from __future__ import annotations

import asyncio
import contextvars
import hashlib
import inspect
import logging
import struct
from collections.abc import Mapping
from dataclasses import dataclass
from types import MappingProxyType
from typing import Any, Callable

from tts_cache_contract_v1 import (
    MAX_PCM_BYTES,
    CacheValidationError,
    SessionTtsConfigV1,
    TtsProfileV1,
    parse_artifact,
)

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class CaptureContext:
    final_text: str
    config: SessionTtsConfigV1
    profile: TtsProfileV1
    key: str
    rejection_epoch: int
    invocation: Mapping[str, str]

    def __post_init__(self) -> None:
        object.__setattr__(self, "invocation", MappingProxyType(dict(self.invocation)))


@dataclass(frozen=True)
class CompletedCapture:
    context: CaptureContext
    wav: bytes
    sample_count: int
    sha256: str


CAPTURE_CONTEXT: contextvars.ContextVar[CaptureContext | None] = contextvars.ContextVar(
    "tts_capture_context_v2", default=None
)


class FixedCapture:
    """Copies valid mono PCM events and only completes with full stream evidence."""

    def __init__(self, context: CaptureContext) -> None:
        self.context = context
        self._pcm = bytearray()
        self._segment_ids: set[str] = set()
        self._final_segments: set[str] = set()
        self._request_ids: set[str] = set()
        self._invalid = False

    @property
    def pcm_bytes(self) -> int:
        return len(self._pcm)

    def append(self, event: Any) -> None:
        if self._invalid:
            return
        frame = getattr(event, "frame", None)
        try:
            raw = bytes(frame.data)
            if (
                not raw
                or len(raw) % 2
                or frame.sample_rate != self.context.profile.sample_rate
                or frame.num_channels != self.context.profile.channels
                or len(self._pcm) + len(raw) > MAX_PCM_BYTES
            ):
                self._invalid = True
                return
            segment_id = getattr(event, "segment_id", "")
            request_id = getattr(event, "request_id", "")
            if not isinstance(segment_id, str) or not segment_id:
                self._invalid = True
                return
            if not isinstance(request_id, str) or not request_id:
                self._invalid = True
                return
            self._segment_ids.add(segment_id)
            self._request_ids.add(request_id)
            self._pcm.extend(raw)
            if bool(getattr(event, "is_final", False)):
                self._final_segments.add(segment_id)
        except (AttributeError, TypeError, ValueError, BufferError):
            self._invalid = True

    def finish(
        self,
        input_complete: bool,
        stream_complete: bool,
        expected_segment_count: int,
    ) -> CompletedCapture | None:
        if (
            self._invalid
            or not input_complete
            or not stream_complete
            or expected_segment_count != 1
            or len(self._pcm) == 0
            or len(self._segment_ids) != expected_segment_count
            or self._final_segments != self._segment_ids
            or len(self._request_ids) != 1
        ):
            return None
        pcm = bytes(self._pcm)
        wav = _canonical_wav(pcm, self.context.profile.sample_rate)
        return CompletedCapture(
            context=self.context,
            wav=wav,
            sample_count=len(pcm) // 2,
            sha256=hashlib.sha256(wav).hexdigest(),
        )


def _canonical_wav(pcm: bytes, sample_rate: int) -> bytes:
    byte_rate = sample_rate * 2
    return (
        b"RIFF"
        + struct.pack("<I", 36 + len(pcm))
        + b"WAVEfmt "
        + struct.pack("<IHHIIHH", 16, 1, 1, sample_rate, byte_rate, 2, 16)
        + b"data"
        + struct.pack("<I", len(pcm))
        + pcm
    )


class PublicationWorker:
    """One bounded asyncio queue and one blocking HTTP publication thread."""

    def __init__(
        self,
        publish: Callable[[CompletedCapture], Any],
        *,
        on_success: Callable[[CompletedCapture, dict[str, Any]], Any] | None = None,
    ) -> None:
        self._publish = publish
        self._on_success = on_success
        self._queue: asyncio.Queue[CompletedCapture] = asyncio.Queue(maxsize=8)
        self._queued_keys: set[tuple[str, str, int]] = set()
        self._task: asyncio.Task[None] | None = None
        self._closing = False

    def start(self) -> None:
        if self._task is None:
            self._task = asyncio.create_task(self._run())

    def try_enqueue(self, capture: CompletedCapture) -> bool:
        if self._closing:
            return False
        if self._task is None:
            self.start()
        key = (
            capture.context.profile.profile_id,
            capture.context.key,
            capture.context.rejection_epoch,
        )
        if key in self._queued_keys:
            return True
        try:
            self._queue.put_nowait(capture)
        except asyncio.QueueFull:
            return False
        self._queued_keys.add(key)
        return True

    async def _run(self) -> None:
        while True:
            capture = await self._queue.get()
            key = (
                capture.context.profile.profile_id,
                capture.context.key,
                capture.context.rejection_epoch,
            )
            try:
                artifact = await asyncio.to_thread(self._publish, capture)
                if artifact is not None and self._on_success is not None:
                    try:
                        validated = parse_artifact(artifact)
                        if (
                            validated["profile_id"]
                            != capture.context.profile.profile_id
                            or validated["key"] != capture.context.key
                            or validated["rejection_epoch"]
                            != capture.context.rejection_epoch
                        ):
                            raise CacheValidationError("publication identity mismatch")
                        callback_result = self._on_success(capture, validated)
                        if inspect.isawaitable(callback_result):
                            await callback_result
                    except Exception:
                        logger.warning("TTS cache publication success callback failed")
            except Exception:
                logger.warning("TTS cache publication failed")
            finally:
                self._queued_keys.discard(key)
                self._queue.task_done()

    async def drain(self, timeout_seconds: float = 5) -> None:
        if self._task is None:
            return
        self._closing = True
        try:
            await asyncio.wait_for(self._queue.join(), timeout=timeout_seconds)
        except asyncio.TimeoutError:
            logger.warning("TTS publication drain timed out")
        self._task.cancel()
        await asyncio.gather(self._task, return_exceptions=True)
        self._task = None
