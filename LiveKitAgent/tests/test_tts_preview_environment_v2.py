"""The preview entry point reads its own configuration without starting a server."""

from pathlib import Path

import pytest

import tts_preview_service_v2 as service


@pytest.fixture
def preview_startup(monkeypatch, tmp_path):
    root = tmp_path / "agent"
    root.mkdir()
    source = root / "src" / "tts_preview_service_v2.py"
    monkeypatch.setattr(service, "__file__", str(source))
    other = tmp_path / "other"
    other.mkdir()
    monkeypatch.chdir(other)
    for name in (
        "TTS_CACHE_SERVICE_SECRET",
        "GOOGLE_APPLICATION_CREDENTIALS",
        "TTS_PREVIEW_HOST",
        "TTS_PREVIEW_PORT",
        "PORT",
    ):
        monkeypatch.delenv(name, raising=False)
    calls = []
    monkeypatch.setattr(
        service, "create_preview_app", lambda secret: calls.append(secret) or "fake-app"
    )
    monkeypatch.setattr(
        service.web, "run_app", lambda app, **kwargs: calls.append((app, kwargs))
    )
    return root, calls


def test_startup_reads_preview_env_from_agent_root_not_working_directory(
    preview_startup, monkeypatch
):
    root, calls = preview_startup
    (root / ".env.preview").write_text(
        "TTS_CACHE_SERVICE_SECRET=file-secret\n"
        "GOOGLE_APPLICATION_CREDENTIALS=preview-credentials.json\n"
        "TTS_PREVIEW_PORT=8090\n",
        encoding="utf-8",
    )
    (Path.cwd() / ".env").write_text(
        "TTS_CACHE_SERVICE_SECRET=wrong-file\n", encoding="utf-8"
    )
    service.main()
    assert calls[0] == "file-secret"
    assert calls[1] == (
        "fake-app",
        {"host": "127.0.0.1", "port": 8090, "handler_cancellation": True},
    )
    assert (
        service.os.environ["GOOGLE_APPLICATION_CREDENTIALS"]
        == "preview-credentials.json"
    )


def test_startup_does_not_override_existing_terminal_configuration(
    preview_startup, monkeypatch
):
    root, calls = preview_startup
    (root / ".env.preview").write_text(
        "TTS_CACHE_SERVICE_SECRET=file-secret\nTTS_PREVIEW_PORT=8090\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("TTS_CACHE_SERVICE_SECRET", "terminal-secret")
    monkeypatch.setenv("TTS_PREVIEW_PORT", "8088")
    service.main()
    assert calls[0] == "terminal-secret"
    assert calls[1][1]["port"] == 8088


def test_startup_allows_missing_file_with_explicit_terminal_credentials(
    preview_startup, monkeypatch
):
    _root, calls = preview_startup
    monkeypatch.setenv("TTS_CACHE_SERVICE_SECRET", "terminal-secret")
    service.main()
    assert calls[0] == "terminal-secret"
    assert calls[1][1]["port"] == 8080
