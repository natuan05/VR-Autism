"""Authenticated roomless HTTP adapter for the shared fixed TTS provider."""

from __future__ import annotations

import asyncio
import hmac
import logging
import os
import struct
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from pathlib import Path

from aiohttp import web
from dotenv import load_dotenv

from tts_cache_contract_v1 import (
    MAX_SAFE_INTEGER,
    MAX_WAV_BYTES,
    CacheValidationError,
    TtsProfileV1,
    compute_key,
    parse_profile,
)
from tts_capture_v2 import CompletedCapture
from tts_generation_v2 import generate_complete

Generator = Callable[[str, TtsProfileV1, str, int], Awaitable[CompletedCapture]]
logger = logging.getLogger(__name__)


class PreviewBusyError(RuntimeError):
    """All distinct preview generation slots are occupied."""


@dataclass
class _Flight:
    task: asyncio.Task[CompletedCapture]
    subscribers: int = 0


class GenerationPool:
    """Share complete results while each subscriber owns its cancellation."""

    def __init__(self, generator: Generator, limit: int = 4) -> None:
        self.generator = generator
        self.limit = limit
        self.pending: dict[tuple[str, str, int], _Flight] = {}
        self.closed = False

    async def generate(
        self, text: str, profile: TtsProfileV1, key: str, rejection_epoch: int
    ) -> CompletedCapture:
        identity = (profile.profile_id, key, rejection_epoch)
        flight = self.pending.get(identity)
        if self.closed:
            raise PreviewBusyError("preview service is stopping")
        if flight is None:
            if len(self.pending) >= self.limit:
                raise PreviewBusyError("preview generation capacity reached")
            flight = _Flight(
                asyncio.create_task(self.generator(text, profile, key, rejection_epoch))
            )
            self.pending[identity] = flight
        flight.subscribers += 1
        try:
            return await asyncio.shield(flight.task)
        finally:
            flight.subscribers -= 1
            if flight.subscribers == 0:
                if self.pending.get(identity) is flight:
                    del self.pending[identity]
                if not flight.task.done():
                    flight.task.cancel()
                await asyncio.gather(flight.task, return_exceptions=True)

    async def close(self) -> None:
        self.closed = True
        tasks = [flight.task for flight in self.pending.values()]
        self.pending.clear()
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)


def _validate_complete_wav(data: bytes) -> bytes:
    """Admit only the canonical bounded format produced by FixedCapture."""
    if not isinstance(data, bytes) or not 46 <= len(data) <= MAX_WAV_BYTES:
        raise ValueError("invalid WAV size")
    if data[:4] != b"RIFF" or data[8:16] != b"WAVEfmt " or data[36:40] != b"data":
        raise ValueError("invalid WAV layout")
    if struct.unpack_from("<I", data, 4)[0] != len(data) - 8:
        raise ValueError("invalid RIFF size")
    if struct.unpack_from("<IHHIIHH", data, 16) != (16, 1, 1, 24000, 48000, 2, 16):
        raise ValueError("invalid PCM settings")
    pcm_bytes = struct.unpack_from("<I", data, 40)[0]
    if pcm_bytes != len(data) - 44 or pcm_bytes % 2:
        raise ValueError("invalid PCM length")
    return data


def create_preview_app(
    secret: str,
    *,
    generator: Generator = generate_complete,
    timeout_seconds: float = 120,
) -> web.Application:
    if not secret:
        raise ValueError("TTS_CACHE_SERVICE_SECRET is required")
    pool = GenerationPool(generator)

    async def generate(request: web.Request) -> web.Response:
        authorization = request.headers.get("Authorization", "").encode("utf-8")
        if not hmac.compare_digest(authorization, f"Bearer {secret}".encode()):
            return web.json_response({"code": "UNAUTHORIZED"}, status=401)
        try:
            body = await request.json()
            if not isinstance(body, dict) or set(body) != {
                "text",
                "profile",
                "key",
                "rejection_epoch",
            }:
                raise CacheValidationError("invalid preview request fields")
            text = body["text"]
            if (
                not isinstance(text, str)
                or not text.strip()
                or len(text.encode("utf-16-le")) // 2 > 500
            ):
                raise CacheValidationError("invalid fixed text")
            profile = parse_profile(body["profile"])
            key = body["key"]
            epoch = body["rejection_epoch"]
            if type(epoch) is not int or not 0 <= epoch <= MAX_SAFE_INTEGER:
                raise CacheValidationError("invalid rejection epoch")
            if not isinstance(key, str) or compute_key(text, profile) != key:
                raise CacheValidationError("cache key mismatch")
        except web.HTTPRequestEntityTooLarge:
            return web.json_response({"code": "REQUEST_TOO_LARGE"}, status=413)
        except (CacheValidationError, ValueError, TypeError, UnicodeError):
            return web.json_response({"code": "INVALID_REQUEST"}, status=400)
        try:
            capture = await asyncio.wait_for(
                pool.generate(text, profile, key, epoch), timeout_seconds
            )
            audio = _validate_complete_wav(capture.wav)
            return web.Response(
                body=audio,
                content_type="audio/wav",
                headers={"Cache-Control": "no-store"},
            )
        except PreviewBusyError:
            return web.json_response({"code": "GENERATION_BUSY"}, status=429)
        except asyncio.TimeoutError:
            return web.json_response({"code": "GENERATION_TIMEOUT"}, status=504)
        except Exception:
            logger.warning("Roomless TTS generation failed")
            return web.json_response({"code": "GENERATION_FAILED"}, status=502)

    async def close_pool(_app: web.Application) -> None:
        await pool.close()

    app = web.Application(client_max_size=65536)
    app.router.add_post("/generate", generate)
    app.on_cleanup.append(close_pool)
    return app


def main() -> None:
    load_dotenv(Path(__file__).resolve().parent.parent / ".env.preview", override=False)
    # Deploy behind HTTPS. Cancellation follows peer disconnection, never a room timeout.
    web.run_app(
        create_preview_app(os.environ.get("TTS_CACHE_SERVICE_SECRET", "")),
        host=os.environ.get("TTS_PREVIEW_HOST", "127.0.0.1"),
        port=int(os.environ.get("TTS_PREVIEW_PORT", os.environ.get("PORT", "8080"))),
        handler_cancellation=True,
    )


if __name__ == "__main__":
    main()
