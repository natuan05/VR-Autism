import asyncio
import struct
from types import SimpleNamespace

import pytest
from aiohttp.test_utils import TestClient, TestServer

from tts_cache_contract_v1 import compute_key, default_config
from tts_preview_service_v2 import GenerationPool, PreviewBusyError, create_preview_app


def request_data(text="Xin chào."):
    profile = default_config().profiles[0]
    return {
        "text": text,
        "profile": profile.to_dict(),
        "key": compute_key(text, profile),
        "rejection_epoch": 0,
    }


def wav():
    return (
        b"RIFF"
        + struct.pack("<I", 38)
        + b"WAVEfmt "
        + struct.pack("<IHHIIHH", 16, 1, 1, 24000, 48000, 2, 16)
        + b"data"
        + struct.pack("<I", 2)
        + b"\0\0"
    )


@pytest.mark.asyncio
async def test_http_auth_and_validation_precede_provider():
    calls = []

    async def generate(*args):
        calls.append(args)
        return SimpleNamespace(wav=wav())

    async with TestClient(
        TestServer(create_preview_app("secret", generator=generate))
    ) as client:
        response = await client.post("/generate", json=request_data())
        assert response.status == 401
        headers = {"Authorization": "Bearer secret"}
        invalid = request_data()
        invalid["key"] = "0" * 64
        response = await client.post("/generate", json=invalid, headers=headers)
        assert response.status == 400
        response = await client.post(
            "/generate", json=request_data("🙂" * 251), headers=headers
        )
        assert response.status == 400
        invalid = request_data()
        invalid["extra"] = True
        response = await client.post("/generate", json=invalid, headers=headers)
        assert response.status == 400
        response = await client.post("/generate", data=b"x" * 65537, headers=headers)
        assert response.status == 413
        assert calls == []
        response = await client.post("/generate", json=request_data(), headers=headers)
        assert response.status == 200
        assert response.content_type == "audio/wav"
        assert await response.read() == wav()
        assert len(calls) == 1


@pytest.mark.asyncio
async def test_same_key_followers_receive_complete_audio_and_one_cancel_keeps_other():
    entered = asyncio.Event()
    release = asyncio.Event()
    calls = 0

    async def generate(*args):
        nonlocal calls
        calls += 1
        entered.set()
        await release.wait()
        return SimpleNamespace(wav=wav())

    pool = GenerationPool(generate, limit=1)
    data = request_data()
    profile = default_config().profiles[0]
    first = asyncio.create_task(pool.generate(data["text"], profile, data["key"], 0))
    await entered.wait()
    second = asyncio.create_task(pool.generate(data["text"], profile, data["key"], 0))
    await asyncio.sleep(0)
    first.cancel()
    await asyncio.gather(first, return_exceptions=True)
    with pytest.raises(PreviewBusyError):
        await pool.generate("Other", profile, compute_key("Other", profile), 0)
    release.set()
    assert (await second).wav == wav()
    assert calls == 1
    await pool.close()


@pytest.mark.asyncio
async def test_last_subscriber_cancellation_aborts_provider_without_retaining_result():
    entered = asyncio.Event()
    cancelled = asyncio.Event()

    async def generate(*args):
        entered.set()
        try:
            await asyncio.Event().wait()
        finally:
            cancelled.set()

    pool = GenerationPool(generate)
    data = request_data()
    profile = default_config().profiles[0]
    request = asyncio.create_task(pool.generate(data["text"], profile, data["key"], 0))
    await entered.wait()
    request.cancel()
    await asyncio.gather(request, return_exceptions=True)
    await asyncio.wait_for(cancelled.wait(), 1)
    assert not pool.pending
    await pool.close()


@pytest.mark.asyncio
async def test_invalid_provider_output_is_never_returned_as_audio():
    async def generate(*args):
        return SimpleNamespace(wav=b"partial")

    async with TestClient(
        TestServer(create_preview_app("secret", generator=generate))
    ) as client:
        response = await client.post(
            "/generate", json=request_data(), headers={"Authorization": "Bearer secret"}
        )
        assert response.status == 502
        assert (await response.json())["code"] == "GENERATION_FAILED"


@pytest.mark.asyncio
async def test_http_deadline_cancels_last_generation_and_epoch_is_strict():
    cancelled = asyncio.Event()
    calls = 0

    async def generate(*args):
        nonlocal calls
        calls += 1
        try:
            await asyncio.Event().wait()
        finally:
            cancelled.set()

    app = create_preview_app("secret", generator=generate, timeout_seconds=0.01)
    async with TestClient(TestServer(app)) as client:
        headers = {"Authorization": "Bearer secret"}
        body = request_data()
        body["rejection_epoch"] = True
        response = await client.post("/generate", json=body, headers=headers)
        assert response.status == 400
        assert calls == 0
        response = await client.post("/generate", json=request_data(), headers=headers)
        assert response.status == 504
        assert (await response.json())["code"] == "GENERATION_TIMEOUT"
        await asyncio.wait_for(cancelled.wait(), 1)


@pytest.mark.asyncio
async def test_provider_details_do_not_enter_response_or_logs(caplog):
    async def generate(*args):
        raise RuntimeError("private-phrase-and-provider-token")

    async with TestClient(
        TestServer(create_preview_app("secret", generator=generate))
    ) as client:
        response = await client.post(
            "/generate", json=request_data(), headers={"Authorization": "Bearer secret"}
        )
        assert response.status == 502
        assert "private-phrase-and-provider-token" not in await response.text()
        assert "private-phrase-and-provider-token" not in caplog.text
