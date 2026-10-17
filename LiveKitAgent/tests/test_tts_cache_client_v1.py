import base64
import json

import pytest

from tts_cache_client_v1 import CacheServiceError, TtsCacheClientV1
from tts_cache_contract_v1 import artifact_path, compute_key, default_config
from tts_capture_v2 import CaptureContext, CompletedCapture


class _Response:
    def __init__(self, payload):
        self.payload = payload

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return None

    def read(self):
        return self.payload


def _scope(session_id="room-1"):
    config = default_config()
    return {
        "session_id": session_id,
        "pin": "1234",
        "child_id": "child-1",
        "lesson_id": "lesson-1",
        "host_id": "host-1",
        "config": config.to_dict(),
    }


def test_session_scope_uses_trusted_bearer_and_retained_config() -> None:
    requests = []

    def opener(request, timeout):
        requests.append((request, timeout))
        return _Response(json.dumps(_scope()).encode())

    client = TtsCacheClientV1(
        "https://backend.example", "trusted-secret", opener=opener
    )
    scope = client.get_session_scope("room-1")
    assert scope is not None and scope.config.profile(
        "teacher-npc"
    ).voice_name.endswith("Aoede")
    assert requests[0][0].get_header("Authorization") == "Bearer trusted-secret"
    assert "session_id=room-1" in requests[0][0].full_url
    assert requests[0][1] == 5.0


def test_legacy_snapshot_404_returns_none_but_other_errors_fail_closed(
    monkeypatch,
) -> None:
    from urllib.error import HTTPError

    # Agent imports may load a developer's .env during test collection.
    monkeypatch.delenv("TTS_CACHE_BACKEND_URL", raising=False)
    monkeypatch.delenv("TTS_CACHE_SERVICE_SECRET", raising=False)

    def not_found(*_args, **_kwargs):
        raise HTTPError("url", 404, "missing", {}, None)

    client = TtsCacheClientV1("https://backend.example", "secret", opener=not_found)
    assert client.get_session_scope("room-1") is None
    with pytest.raises(CacheServiceError, match="missing"):
        client.get_session_scope("room-1", required=True)

    def unavailable(*_args, **_kwargs):
        raise HTTPError("url", 503, "unavailable", {}, None)

    client = TtsCacheClientV1("https://backend.example", "secret", opener=unavailable)
    with pytest.raises(CacheServiceError):
        client.get_session_scope("room-1")

    with pytest.raises(CacheServiceError):
        TtsCacheClientV1("https://backend.example", "").get_session_scope("room-1")
    with pytest.raises(CacheServiceError, match="unavailable"):
        TtsCacheClientV1().get_session_scope("room-1", required=True)
    with pytest.raises(CacheServiceError):
        TtsCacheClientV1("http://backend.example", "secret")
    with pytest.raises(CacheServiceError, match="URL"):
        TtsCacheClientV1("https://[", "secret")


def test_publish_encodes_exact_profile_text_and_validates_returned_identity() -> None:
    config = default_config()
    profile = config.profile("teacher-npc")
    text = "Xin chào."
    key = compute_key(text, profile)
    context = CaptureContext(text, config, profile, key, 0, {})
    capture = CompletedCapture(context, b"RIFF", 1, "unused")
    payload = {
        "profile_id": profile.profile_id,
        "key": key,
        "rejection_epoch": 0,
        "object_path": artifact_path(profile.profile_id, key, 0),
        "generation": "1",
        "sha256": "a" * 64,
        "byte_size": 46,
        "sample_count": 1,
        "duration_ms": 0,
        "format": "wav-pcm16-v1",
    }
    requests = []

    def opener(request, timeout):
        requests.append(request)
        return _Response(json.dumps(payload).encode())

    client = TtsCacheClientV1("https://backend.example", "secret", opener=opener)
    result = client.publish(capture)
    assert result == payload
    request = requests[0]

    def decode(value):
        return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))

    assert (
        json.loads(decode(request.get_header("X-tts-cache-profile")))
        == profile.to_dict()
    )
    assert decode(request.get_header("X-tts-cache-text")).decode() == text
    assert request.get_header("X-tts-cache-key") == key
