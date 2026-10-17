"""Trusted Python client for retained session settings and cache publication."""

from __future__ import annotations

import base64
import json
import os
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any, Callable

from tts_cache_contract_v1 import (
    CacheValidationError,
    SessionTtsConfigV1,
    TtsProfileV1,
    parse_artifact,
    parse_config,
)


class CacheServiceError(RuntimeError):
    """Backend failure; details are deliberately safe for agent logs."""


@dataclass(frozen=True)
class SessionScopeV1:
    session_id: str
    pin: str
    child_id: str
    lesson_id: str
    host_id: str
    config: SessionTtsConfigV1


class _RejectBackendRedirects(urllib.request.HTTPRedirectHandler):
    """Never forward an authenticated cache request through a redirect."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class TtsCacheClientV1:
    def __init__(
        self,
        base_url: str | None = None,
        service_secret: str | None = None,
        *,
        opener: Callable[..., Any] | None = None,
        timeout_seconds: float = 8.0,
        session_timeout_seconds: float = 5.0,
    ) -> None:
        raw_base_url = base_url or os.getenv("TTS_CACHE_BACKEND_URL", "")
        raw_secret = service_secret or os.getenv("TTS_CACHE_SERVICE_SECRET", "")
        if not isinstance(raw_base_url, str) or not isinstance(raw_secret, str):
            raise CacheServiceError("cache backend configuration is invalid")
        self._base_url = raw_base_url.rstrip("/")
        self._secret = raw_secret
        local_http = False
        if self._base_url:
            try:
                parsed_url = urllib.parse.urlsplit(self._base_url)
                hostname = parsed_url.hostname
                # Reading port validates both its syntax and range.
                _ = parsed_url.port
            except (TypeError, ValueError) as exc:
                raise CacheServiceError("cache backend URL is invalid") from exc
            if (
                not hostname
                or parsed_url.netloc.endswith(":")
                or parsed_url.username is not None
                or parsed_url.password is not None
                or "?" in self._base_url
                or "#" in self._base_url
                or "\\" in self._base_url
                or any(
                    c.isspace() or ord(c) < 32 or ord(c) == 127 for c in self._base_url
                )
            ):
                raise CacheServiceError("cache backend URL is invalid")
            local_http = (
                parsed_url.scheme == "http"
                and hostname in {"localhost", "127.0.0.1"}
                and os.getenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "").strip().lower()
                == "true"
            )
            if parsed_url.scheme != "https" and not local_http:
                raise CacheServiceError("cache backend must use an HTTPS origin")
        if opener is None:
            handlers = [_RejectBackendRedirects()]
            if local_http:
                # A loopback development secret must not pass through a configured proxy.
                handlers.append(urllib.request.ProxyHandler({}))
            opener = urllib.request.build_opener(*handlers).open
        self._opener = opener
        self._timeout = timeout_seconds
        self._session_timeout = session_timeout_seconds

    def get_session_scope(
        self, session_id: str, *, required: bool = False
    ) -> SessionScopeV1 | None:
        """Return retained cache settings; 404 denotes an older/legacy session."""
        if not self._base_url and not self._secret:
            if required:
                raise CacheServiceError("cache backend credentials are unavailable")
            return None
        if not self._base_url or not self._secret:
            raise CacheServiceError("cache backend credentials are incomplete")
        query = urllib.parse.urlencode({"session_id": session_id})
        request = urllib.request.Request(
            f"{self._base_url}/api/tts-cache/session?{query}",
            headers={"Authorization": f"Bearer {self._secret}"},
            method="GET",
        )
        try:
            with self._opener(request, timeout=self._session_timeout) as response:
                payload = json.loads(response.read())
        except urllib.error.HTTPError as exc:
            if exc.code == 404:
                if required:
                    raise CacheServiceError(
                        "retained session snapshot is missing"
                    ) from exc
                return None
            raise CacheServiceError("session snapshot unavailable") from exc
        except Exception as exc:
            raise CacheServiceError("session snapshot unavailable") from exc
        try:
            if not isinstance(payload, dict) or set(payload) != {
                "session_id",
                "pin",
                "child_id",
                "lesson_id",
                "host_id",
                "config",
            }:
                raise CacheValidationError("invalid session scope")
            values = [
                payload[name]
                for name in ("session_id", "pin", "child_id", "lesson_id", "host_id")
            ]
            if any(not isinstance(value, str) or not value.strip() for value in values):
                raise CacheValidationError("invalid session scope")
            if payload["session_id"] != session_id:
                raise CacheValidationError("session scope mismatch")
            return SessionScopeV1(
                session_id=payload["session_id"],
                pin=payload["pin"],
                child_id=payload["child_id"],
                lesson_id=payload["lesson_id"],
                host_id=payload["host_id"],
                config=parse_config(payload["config"]),
            )
        except (CacheValidationError, TypeError, KeyError) as exc:
            raise CacheServiceError("invalid session snapshot") from exc

    def publish(self, capture: Any) -> dict[str, Any]:
        """Publish canonical WAV through the trusted service-only backend route."""
        if not self._base_url or not self._secret:
            raise CacheServiceError("cache publication is not configured")
        context = capture.context
        profile: TtsProfileV1 = context.profile
        headers = {
            "Authorization": f"Bearer {self._secret}",
            "Content-Type": "audio/wav",
            "X-TTS-Cache-Profile": _b64url_json(profile.to_dict()),
            "X-TTS-Cache-Text": _b64url(context.final_text.encode("utf-8")),
            "X-TTS-Cache-Key": context.key,
            "X-TTS-Cache-Rejection-Epoch": str(context.rejection_epoch),
        }
        request = urllib.request.Request(
            f"{self._base_url}/api/tts-cache/publish",
            data=capture.wav,
            headers=headers,
            method="POST",
        )
        try:
            with self._opener(request, timeout=self._timeout) as response:
                body = json.loads(response.read())
            artifact = parse_artifact(body)
            if (
                artifact["profile_id"] != profile.profile_id
                or artifact["key"] != context.key
                or artifact["rejection_epoch"] != context.rejection_epoch
            ):
                raise CacheValidationError("publication identity mismatch")
            return artifact
        except Exception as exc:
            raise CacheServiceError("cache publication failed") from exc


def _b64url(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")


def _b64url_json(value: dict[str, Any]) -> str:
    return _b64url(
        json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    )
