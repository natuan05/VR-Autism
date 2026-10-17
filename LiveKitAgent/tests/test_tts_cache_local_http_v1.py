"""Runtime cache development exceptions must stay on the local machine."""

import json
import threading
import urllib.request
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

from tts_cache_client_v1 import CacheServiceError, TtsCacheClientV1


@pytest.mark.parametrize("flag", [None, "false", "0", "1", "yes", ""])
def test_http_requires_explicit_true_opt_in(monkeypatch, flag):
    if flag is None:
        monkeypatch.delenv("TTS_CACHE_ALLOW_LOCAL_HTTP", raising=False)
    else:
        monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", flag)
    with pytest.raises(CacheServiceError):
        TtsCacheClientV1("http://127.0.0.1:3000", "secret")


@pytest.mark.parametrize("host", ["localhost", "127.0.0.1", "LOCALHOST"])
def test_explicit_opt_in_accepts_only_supported_loopback_hosts(monkeypatch, host):
    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    client = TtsCacheClientV1(f"http://{host}:3000", "secret")
    assert client._base_url == f"http://{host}:3000"


@pytest.mark.parametrize(
    "url",
    [
        "http://192.168.1.25:3000",
        "http://backend.example:3000",
        "http://localhost.example:3000",
        "http://127.0.0.2:3000",
        "http://127.1:3000",
        "http://2130706433:3000",
        "http://[::1]:3000",
        "ftp://127.0.0.1:3000",
        "http://user:password@127.0.0.1:3000",
        "http://127.0.0.1:3000?secret=bad",
        "http://127.0.0.1:3000#fragment",
        "http://127.0.0.1:99999",
        "http://127.0.0.1:bad",
        "http://localhost:",
        "http://127.0.0.1:/base",
        "http://127.0.0.1\\@public.example:3000",
    ],
)
def test_opt_in_never_authorizes_other_addresses_or_ambiguous_urls(monkeypatch, url):
    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    with pytest.raises(CacheServiceError):
        TtsCacheClientV1(url, "secret")


@pytest.mark.parametrize(
    "url",
    [
        "https://user:password@backend.example",
        "https://backend.example?other=1",
        "https://backend.example#fragment",
        "https://backend.example:99999",
        "https://backend.example:",
    ],
)
def test_https_configuration_also_rejects_ambiguous_origins(url):
    with pytest.raises(CacheServiceError):
        TtsCacheClientV1(url, "secret")


def test_local_environment_settings_keep_secret_requirement(monkeypatch):
    monkeypatch.setenv("TTS_CACHE_BACKEND_URL", "http://127.0.0.1:3000")
    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    monkeypatch.delenv("TTS_CACHE_SERVICE_SECRET", raising=False)
    client = TtsCacheClientV1()
    with pytest.raises(CacheServiceError, match="incomplete"):
        client.get_session_scope("room-1", required=True)


def test_loopback_client_does_not_use_environment_proxy(monkeypatch):
    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    original_builder = urllib.request.build_opener
    supplied_handlers = []

    def build(*handlers):
        supplied_handlers.extend(handlers)
        return original_builder(*handlers)

    monkeypatch.setattr(urllib.request, "build_opener", build)
    TtsCacheClientV1("http://127.0.0.1:3000", "secret")
    assert any(
        isinstance(handler, urllib.request.ProxyHandler) and handler.proxies == {}
        for handler in supplied_handlers
    )


@contextmanager
def _redirect_server(location):
    requests = []

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            requests.append((self.path, self.headers.get("Authorization")))
            self.send_response(302)
            self.send_header("Location", location)
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, *_args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}", requests
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


def test_bearer_request_does_not_follow_even_loopback_redirect(monkeypatch):
    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    with (
        _redirect_server("/not-used") as (destination, leaked_requests),
        _redirect_server(destination + "/redirect-target") as (source, requests),
    ):
        client = TtsCacheClientV1(source, "trusted-secret")
        with pytest.raises(CacheServiceError, match="unavailable"):
            client.get_session_scope("room-1", required=True)
        assert requests == [
            ("/api/tts-cache/session?session_id=room-1", "Bearer trusted-secret")
        ]
        assert leaked_requests == []


def test_https_remains_default_and_injected_transport_keeps_authorization(monkeypatch):
    monkeypatch.delenv("TTS_CACHE_ALLOW_LOCAL_HTTP", raising=False)
    from tts_cache_contract_v1 import default_config

    requests = []

    class Response:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return None

        def read(self):
            return json.dumps(
                {
                    "session_id": "room-1",
                    "pin": "1234",
                    "child_id": "child-1",
                    "lesson_id": "lesson-1",
                    "host_id": "host-1",
                    "config": default_config().to_dict(),
                }
            ).encode()

    def opener(request, **_kwargs):
        requests.append(request)
        return Response()

    client = TtsCacheClientV1("https://backend.example", "secret", opener=opener)
    assert client.get_session_scope("room-1", required=True).session_id == "room-1"
    assert requests[0].get_header("Authorization") == "Bearer secret"


def test_loopback_runtime_reads_snapshot_and_publishes_with_same_secret(monkeypatch):
    from tts_cache_contract_v1 import artifact_path, compute_key, default_config
    from tts_capture_v2 import CaptureContext, CompletedCapture

    monkeypatch.setenv("TTS_CACHE_ALLOW_LOCAL_HTTP", "true")
    config = default_config()
    profile = config.profile("teacher-npc")
    key = compute_key("Hello", profile)
    artifact = {
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
    scope = {
        "session_id": "room-1",
        "pin": "1234",
        "child_id": "child-1",
        "lesson_id": "lesson-1",
        "host_id": "host-1",
        "config": config.to_dict(),
    }
    received = []

    class Handler(BaseHTTPRequestHandler):
        def _respond(self, payload):
            body = json.dumps(payload).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            received.append(
                (self.command, self.path, self.headers.get("Authorization"))
            )
            self._respond(scope)

        def do_POST(self):
            received.append(
                (self.command, self.path, self.headers.get("Authorization"))
            )
            assert self.rfile.read(int(self.headers["Content-Length"])) == b"RIFF"
            self._respond(artifact)

        def log_message(self, *_args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        client = TtsCacheClientV1(f"http://127.0.0.1:{server.server_port}", "secret")
        assert client.get_session_scope("room-1", required=True).config == config
        capture = CompletedCapture(
            CaptureContext("Hello", config, profile, key, 0, {}), b"RIFF", 1, "unused"
        )
        assert client.publish(capture) == artifact
        assert received == [
            ("GET", "/api/tts-cache/session?session_id=room-1", "Bearer secret"),
            ("POST", "/api/tts-cache/publish", "Bearer secret"),
        ]
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)
