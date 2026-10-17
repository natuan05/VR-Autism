import json
from dataclasses import replace
from pathlib import Path

import pytest

from tts_cache_contract_v1 import (
    CacheValidationError,
    compute_key,
    default_config,
    parse_cache_packet,
    parse_config,
    parse_profile,
)


def test_reconciliation_retains_only_active_unique_completed_openings():
    packet = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_RECONCILE",
        "session_id": "s",
        "run_id": "r",
        "agent_instance_id": "boot",
        "active_activation_ids": ["a"],
        "current_activity": None,
        "completed_opening_activation_ids": ["a"],
    }
    assert parse_cache_packet(packet)["completed_opening_activation_ids"] == ["a"]
    for invalid in (["b"], ["a", "a"], None, [True]):
        with pytest.raises(CacheValidationError):
            parse_cache_packet({**packet, "completed_opening_activation_ids": invalid})


def test_effective_identity_returns_to_original_rate_without_edit_history():
    profile = default_config().profiles[0]
    assert (
        compute_key("Xin chào.", profile)
        == "3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8"
    )
    assert (
        compute_key("Xin chào.", replace(profile, speaking_rate=0.9))
        == "892436b94b4bfbc26c3d5209aa12c4ab622ed00ed0fe75cb6729e19dd4e958d8"
    )
    assert compute_key("Xin chào.", replace(profile, speaking_rate=1.0)) == compute_key(
        "Xin chào.", profile
    )
    assert compute_key(
        "Xin chào.", replace(profile, profile_id="another")
    ) != compute_key("Xin chào.", profile)


def test_shared_vectors():
    fixture = json.loads(
        (Path(__file__).parent / "fixtures/tts-cache-v1.json").read_text(
            encoding="utf-8"
        )
    )
    for vector in fixture["keys"]:
        assert (
            compute_key(vector["text"], parse_profile(vector["profile"]))
            == vector["key"]
        )


def test_configuration_is_validated_and_immutable():
    raw = default_config().to_dict()
    config = parse_config(raw)
    raw["profiles"][0]["voice_name"] = "changed"
    assert config.profiles[0].voice_name == "vi-VN-Chirp3-HD-Aoede"
    assert config.profile_for_npc("peer-npc").profile_id == "peer-npc"
    with pytest.raises(CacheValidationError):
        config.profile_for_npc("unknown")
    for rate in (True, float("nan"), 2.1, 0.249, 0.1234567):
        invalid = default_config().profiles[0].to_dict()
        invalid["speaking_rate"] = rate
        with pytest.raises(CacheValidationError):
            parse_profile(invalid)


def test_hello_bootstraps_instance_without_inventing_a_run():
    hello = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_HELLO",
        "session_id": "session",
        "agent_instance_id": "boot-1",
    }
    assert parse_cache_packet(hello) == hello
    with pytest.raises(CacheValidationError):
        parse_cache_packet({**hello, "run_id": "invented"})


def test_packet_exact_shape_preserves_spoken_text_and_rejects_stale_shape():
    packet = {
        "contract_version": 1,
        "event": "FIXED_AUDIO_STREAM_REQUEST",
        "session_id": "session",
        "run_id": "run",
        "activation_id": "activation",
        "invocation_id": "command",
        "npc_binding_id": "teacher-npc",
        "kind": "manual_hint",
        "owner_revision": 1,
        "text": "  Hello!  ",
        "profile_id": "teacher-npc",
        "key": "a" * 64,
        "rejection_epoch": 0,
    }
    assert parse_cache_packet(json.dumps(packet))["text"] == "  Hello!  "
    with pytest.raises(CacheValidationError):
        parse_cache_packet({**packet, "voice_name": "untrusted"})
    with pytest.raises(CacheValidationError):
        parse_cache_packet({**packet, "owner_revision": True})
    with pytest.raises(CacheValidationError):
        parse_cache_packet({**packet, "contract_version": 2})
