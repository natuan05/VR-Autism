"""Strict shared cache identity, session profiles and fixed-audio packets."""

from __future__ import annotations

import hashlib
import json
import math
import re
import struct
from dataclasses import asdict, dataclass
from decimal import Decimal
from typing import Any

CACHE_TOPIC = "lesson-graph-v2.audio-cache"
MAX_PCM_BYTES = 4_194_304
MAX_WAV_BYTES = MAX_PCM_BYTES + 44
MAX_PACKET_BYTES = 16_384
MAX_SAFE_INTEGER = 9_007_199_254_740_991
FORMAT = "wav-pcm16-v1"
PROFILE_FIELDS = {
    "profile_id",
    "provider",
    "model",
    "language",
    "voice_name",
    "speaking_rate",
    "provider_encoding",
    "sample_rate",
    "channels",
    "audio_format",
}
ARTIFACT_FIELDS = {
    "profile_id",
    "key",
    "rejection_epoch",
    "object_path",
    "generation",
    "sha256",
    "byte_size",
    "sample_count",
    "duration_ms",
    "format",
}
INVOCATION_FIELDS = {
    "session_id",
    "run_id",
    "activation_id",
    "invocation_id",
    "npc_binding_id",
    "kind",
}
KINDS = {"dialogue", "opening", "manual_hint", "automatic_reminder"}
HEX = re.compile(r"^[a-f0-9]{64}$")
PROFILE_ID = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")


class CacheValidationError(ValueError):
    """Invalid or incompatible cache input; never silently substitute settings."""


def _exact(data: Any, fields: set[str]) -> dict[str, Any]:
    if not isinstance(data, dict) or set(data) != fields:
        raise CacheValidationError("fields do not match the cache contract")
    return data


def _text(value: Any) -> str:
    if not isinstance(value, str) or not value.strip():
        raise CacheValidationError("nonblank text required")
    try:
        value.encode("utf-8", errors="strict")
    except UnicodeEncodeError as exc:
        raise CacheValidationError("invalid Unicode") from exc
    return value


def _integer(value: Any, minimum: int = 0, maximum: int = MAX_SAFE_INTEGER) -> int:
    if type(value) is not int or not minimum <= value <= maximum:
        raise CacheValidationError("invalid integer")
    return value


def canonical_rate(value: Any) -> str:
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
    ):
        raise CacheValidationError("invalid speaking rate")
    number = Decimal(str(value))
    if not Decimal("0.25") <= number <= Decimal("2") or number != number.quantize(
        Decimal("0.000001")
    ):
        raise CacheValidationError("unsupported speaking rate")
    return (
        format(number, "f").rstrip("0").rstrip(".")
        if "." in format(number, "f")
        else format(number, "f")
    )


@dataclass(frozen=True)
class TtsProfileV1:
    profile_id: str
    provider: str
    model: str
    language: str
    voice_name: str
    speaking_rate: float
    provider_encoding: str
    sample_rate: int
    channels: int
    audio_format: str

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass(frozen=True)
class NpcProfileV1:
    npc_binding_id: str
    profile_id: str


@dataclass(frozen=True)
class SessionTtsConfigV1:
    schema_version: int
    remote_cache_wait_ms: int
    profiles: tuple[TtsProfileV1, ...]
    npc_profiles: tuple[NpcProfileV1, ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "schema_version": 1,
            "remote_cache_wait_ms": self.remote_cache_wait_ms,
            "profiles": [p.to_dict() for p in self.profiles],
            "npc_profiles": [asdict(p) for p in self.npc_profiles],
        }

    def profile(self, profile_id: str) -> TtsProfileV1:
        for profile in self.profiles:
            if profile.profile_id == profile_id:
                return profile
        raise CacheValidationError("unknown profile")

    def profile_for_npc(self, npc_binding_id: str) -> TtsProfileV1:
        for mapping in self.npc_profiles:
            if mapping.npc_binding_id == npc_binding_id:
                return self.profile(mapping.profile_id)
        raise CacheValidationError("unknown NPC profile mapping")


def parse_profile(data: Any) -> TtsProfileV1:
    data = _exact(data, PROFILE_FIELDS)
    if not isinstance(data["profile_id"], str) or not PROFILE_ID.fullmatch(
        data["profile_id"]
    ):
        raise CacheValidationError("unsafe profile ID")
    canonical_rate(data["speaking_rate"])
    if (
        data["provider"],
        data["model"],
        data["language"],
        data["provider_encoding"],
        data["audio_format"],
    ) != ("google-cloud-tts", "chirp_3", "vi-VN", "OGG_OPUS", FORMAT):
        raise CacheValidationError("unsupported profile configuration")
    _integer(data["sample_rate"], 24_000, 24_000)
    _integer(data["channels"], 1, 1)
    if not re.fullmatch(r"vi-VN-Chirp3-HD-[A-Za-z]+", _text(data["voice_name"])):
        raise CacheValidationError("unsupported voice")
    return TtsProfileV1(**data)


def parse_config(data: Any) -> SessionTtsConfigV1:
    data = _exact(
        data, {"schema_version", "remote_cache_wait_ms", "profiles", "npc_profiles"}
    )
    _integer(data["schema_version"], 1, 1)
    _integer(data["remote_cache_wait_ms"], 0, 10_000)
    if not isinstance(data["profiles"], list) or not isinstance(
        data["npc_profiles"], list
    ):
        raise CacheValidationError("profile arrays required")
    profiles = tuple(parse_profile(p) for p in data["profiles"])
    ids = {p.profile_id for p in profiles}
    if len(ids) != len(profiles):
        raise CacheValidationError("duplicate profile")
    mappings = []
    seen = set()
    for mapping in data["npc_profiles"]:
        _exact(mapping, {"npc_binding_id", "profile_id"})
        npc = _text(mapping["npc_binding_id"])
        if npc in seen or mapping["profile_id"] not in ids:
            raise CacheValidationError("duplicate or unknown NPC mapping")
        seen.add(npc)
        mappings.append(NpcProfileV1(**mapping))
    return SessionTtsConfigV1(
        1, data["remote_cache_wait_ms"], profiles, tuple(mappings)
    )


def default_config() -> SessionTtsConfigV1:
    profiles = tuple(
        TtsProfileV1(
            profile_id,
            "google-cloud-tts",
            "chirp_3",
            "vi-VN",
            voice,
            1.0,
            "OGG_OPUS",
            24_000,
            1,
            FORMAT,
        )
        for profile_id, voice in (
            ("teacher-npc", "vi-VN-Chirp3-HD-Aoede"),
            ("peer-npc", "vi-VN-Chirp3-HD-Puck"),
        )
    )
    return SessionTtsConfigV1(
        1,
        1500,
        profiles,
        tuple(NpcProfileV1(p.profile_id, p.profile_id) for p in profiles),
    )


def compute_key(final_text: str, profile: TtsProfileV1) -> str:
    parse_profile(profile.to_dict())
    _text(final_text)
    values = [
        "tts-cache-key-v1",
        profile.profile_id,
        profile.provider,
        profile.model,
        profile.language,
        profile.voice_name,
        canonical_rate(profile.speaking_rate),
        profile.provider_encoding,
        str(profile.sample_rate),
        str(profile.channels),
        profile.audio_format,
        final_text,
    ]
    payload = bytearray()
    for value in values:
        encoded = value.encode("utf-8", errors="strict")
        payload.extend(struct.pack(">I", len(encoded)))
        payload.extend(encoded)
    return hashlib.sha256(payload).hexdigest()


def artifact_path(profile_id: str, key: str, rejection_epoch: int) -> str:
    if (
        not isinstance(profile_id, str)
        or not PROFILE_ID.fullmatch(profile_id)
        or not isinstance(key, str)
        or not HEX.fullmatch(key)
    ):
        raise CacheValidationError("unsafe cache path")
    _integer(rejection_epoch)
    return f"tts-cache/v1/{profile_id}/{key}/r{rejection_epoch}.wav"


def parse_artifact(data: Any) -> dict[str, Any]:
    data = _exact(data, ARTIFACT_FIELDS)
    if data["object_path"] != artifact_path(
        data["profile_id"], data["key"], data["rejection_epoch"]
    ):
        raise CacheValidationError("artifact path mismatch")
    if not isinstance(data["generation"], str) or not re.fullmatch(
        r"[1-9][0-9]*", data["generation"]
    ):
        raise CacheValidationError("invalid Storage generation")
    if (
        not isinstance(data["sha256"], str)
        or not HEX.fullmatch(data["sha256"])
        or data["format"] != FORMAT
    ):
        raise CacheValidationError("invalid artifact checksum/format")
    samples = _integer(data["sample_count"], 1, MAX_PCM_BYTES // 2)
    if (
        _integer(data["byte_size"], 46, MAX_WAV_BYTES) != samples * 2 + 44
        or _integer(data["duration_ms"]) != samples * 1000 // 24_000
    ):
        raise CacheValidationError("artifact size/duration mismatch")
    return dict(data)


def parse_cache_packet(payload: bytes | str | dict[str, Any]) -> dict[str, Any]:
    try:
        if isinstance(payload, dict):
            data = payload
            size = len(json.dumps(payload, ensure_ascii=False).encode("utf-8"))
        else:
            size = len(
                payload if isinstance(payload, bytes) else payload.encode("utf-8")
            )
            data = json.loads(payload)
        if size > MAX_PACKET_BYTES:
            raise CacheValidationError("packet too large")
        if (
            not isinstance(data, dict)
            or type(data.get("contract_version")) is not int
            or data["contract_version"] != 1
        ):
            raise CacheValidationError("unsupported cache contract")
        event = data.get("event")
        extra = {
            "FIXED_AUDIO_STREAM_REQUEST": {
                "owner_revision",
                "text",
                "profile_id",
                "key",
                "rejection_epoch",
            },
            "FIXED_AUDIO_CANCEL": {"owner_revision", "reason"},
            "FIXED_AUDIO_ACTIVITY": {
                "owner_revision",
                "activity_sequence",
                "phase",
                "source",
                "status",
                "reason",
            },
            "FIXED_AUDIO_DONE": {
                "owner_revision",
                "agent_instance_id",
                "status",
                "reason",
            },
            "FIXED_AUDIO_REMINDER_REQUEST": {
                "agent_instance_id",
                "text",
                "profile_id",
                "key",
            },
            "FIXED_AUDIO_REMINDER_CANCEL": {"agent_instance_id", "reason"},
        }
        if event == "FIXED_AUDIO_HELLO":
            _exact(
                data, {"contract_version", "event", "session_id", "agent_instance_id"}
            )
        elif event == "AUDIO_CACHE_READY":
            _exact(
                data,
                {
                    "contract_version",
                    "event",
                    "session_id",
                    "agent_instance_id",
                    *ARTIFACT_FIELDS,
                },
            )
            parse_artifact({field: data[field] for field in ARTIFACT_FIELDS})
        elif event == "FIXED_AUDIO_RECONCILE":
            _exact(
                data,
                {
                    "contract_version",
                    "event",
                    "session_id",
                    "run_id",
                    "agent_instance_id",
                    "active_activation_ids",
                    "current_activity",
                }
                | (
                    {"completed_opening_activation_ids"}
                    if "completed_opening_activation_ids" in data
                    else set()
                ),
            )
            activations = data["active_activation_ids"]
            if (
                not isinstance(activations, list)
                or len(activations) > 1024
                or len(set(map(_text, activations))) != len(activations)
            ):
                raise CacheValidationError("invalid activation list")
            if "completed_opening_activation_ids" in data:
                completed = data["completed_opening_activation_ids"]
                if (
                    not isinstance(completed, list)
                    or len(completed) > 1024
                    or len(set(map(_text, completed))) != len(completed)
                    or not set(completed).issubset(set(activations))
                ):
                    raise CacheValidationError("invalid completed opening list")
            if data["current_activity"] is not None:
                activity = parse_cache_packet(data["current_activity"])
                if (
                    activity["event"] != "FIXED_AUDIO_ACTIVITY"
                    or activity["session_id"] != data["session_id"]
                    or activity["run_id"] != data["run_id"]
                ):
                    raise CacheValidationError("invalid reconciliation activity")
        elif event in extra:
            _exact(
                data, {"contract_version", "event", *INVOCATION_FIELDS, *extra[event]}
            )
            if data["kind"] not in KINDS:
                raise CacheValidationError("invalid invocation kind")
            if "REMINDER" in event and data["kind"] != "automatic_reminder":
                raise CacheValidationError("invalid reminder kind")
        else:
            raise CacheValidationError("unknown cache event")
        for field in (
            "session_id",
            "run_id",
            "activation_id",
            "invocation_id",
            "npc_binding_id",
            "agent_instance_id",
        ):
            if field in data:
                _text(data[field])
        for field in ("owner_revision", "activity_sequence", "rejection_epoch"):
            if field in data:
                _integer(data[field])
        if "text" in data and len(_text(data["text"]).encode("utf-16-le")) // 2 > 500:
            raise CacheValidationError("text too long")
        if "profile_id" in data and not PROFILE_ID.fullmatch(_text(data["profile_id"])):
            raise CacheValidationError("unsafe profile ID")
        if "key" in data and not HEX.fullmatch(_text(data["key"])):
            raise CacheValidationError("invalid key")
        if "reason" in data and not isinstance(data["reason"], str):
            raise CacheValidationError("reason must be text")
        if event in {"FIXED_AUDIO_CANCEL", "FIXED_AUDIO_REMINDER_CANCEL"} and data[
            "reason"
        ] not in {
            "skip",
            "scope_changed",
            "session_ended",
            "user_speaking",
            "superseded",
            "reconfigured",
        }:
            raise CacheValidationError("invalid cancellation reason")
        if event == "FIXED_AUDIO_DONE" and data["status"] not in {
            "SUCCESS",
            "FAILED",
            "CANCELLED",
        }:
            raise CacheValidationError("invalid terminal status")
        if event == "FIXED_AUDIO_ACTIVITY":
            phase, source, status = data["phase"], data["source"], data["status"]
            if (
                phase
                not in {
                    "preparing",
                    "local_selected",
                    "stream_selected",
                    "playing",
                    "terminal",
                }
                or source not in {"none", "local", "stream"}
                or status
                not in {"PENDING", "SUCCESS", "FAILED", "CANCELLED", "SKIPPED"}
            ):
                raise CacheValidationError("invalid lifecycle state")
            if (
                (phase == "terminal") != (status != "PENDING")
                or (phase == "preparing" and source != "none")
                or (phase == "local_selected" and source != "local")
                or (phase == "stream_selected" and source != "stream")
                or (phase == "playing" and source == "none")
            ):
                raise CacheValidationError("inconsistent lifecycle state")
        return dict(data)
    except (TypeError, KeyError, UnicodeError, json.JSONDecodeError) as exc:
        raise CacheValidationError("invalid cache packet") from exc
