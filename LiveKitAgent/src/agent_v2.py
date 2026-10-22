"""
LiveKit AI Voice Agent for VR Autism Application
Pipeline: Google STT (Chirp 3) -> Gemini LLM (3.5 Flash Lite) -> Google TTS (Chirp 3 HD) + Silero VAD
SDK: LiveKit Agents 1.6+
"""

import asyncio
import contextlib
import json
import logging
import os
import random
import uuid
from collections.abc import AsyncIterable, AsyncIterator
from dataclasses import dataclass
from enum import Enum, auto
from typing import Any, Optional

from dotenv import load_dotenv
from google.cloud import texttospeech as gcp_tts
from livekit import rtc
from livekit.agents import (
    AutoSubscribe,
    JobContext,
    JobProcess,
    RunContext,
    WorkerOptions,
    cli,
    function_tool,
    llm,
    room_io,
)
from livekit.agents import tts as agents_tts
from livekit.agents.voice import Agent, AgentSession
from livekit.agents.voice.speech_handle import SpeechHandle
from livekit.plugins import google, silero

from fixed_audio_runtime_v1 import (
    FixedAudioRuntimeV1,
    activity_packet,
    done_packet,
    invocation_fields,
)
from shared_teacher_speech import try_handle_teacher_script
from silence_reminder_v2 import ActivationSilenceReminderV2
from tts_cache_client_v1 import CacheServiceError, SessionScopeV1, TtsCacheClientV1
from tts_cache_contract_v1 import (
    CACHE_TOPIC,
    CacheValidationError,
    compute_key,
    parse_cache_packet,
)
from tts_capture_v2 import (
    CAPTURE_CONTEXT,
    CaptureContext,
    FixedCapture,
    PublicationWorker,
)
from tts_generation_v2 import stream_fixed_tts
from voice_command_runtime_v2 import (
    CommandDisposition,
    CommandStatus,
    VoiceCommandRuntime,
)
from voice_contract_v2 import (
    REMOTE_TOPIC,
    VOICE_TOPIC,
    CancelActiveQuest,
    CancelSpeakScriptV2,
    PacketValidationError,
    SetActiveQuest,
    SpeakScriptV2,
    VerbalHintV2,
    accepted_reminder_packet,
    parse_unity_packet,
    quest_matched_packet,
    quest_status_packet,
    speak_script_done_packet,
)
from voice_quest_runtime_v2 import (
    ActivationDisposition,
    ActivationStatus,
    VoiceQuestRuntime,
)

# ---------------------------------------------------------------------------
# Setup & Logging Configuration
# ---------------------------------------------------------------------------
load_dotenv()

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("vr-voice-agent")

# ---------------------------------------------------------------------------
# Instructions & Dynamic Prompt Engineering
# ---------------------------------------------------------------------------
BASE_INSTRUCTIONS = """You are a warm, encouraging teacher in a Vietnamese VR therapy app for children with autism.
You are speaking with a young child. ALWAYS respond in Vietnamese — use simple, gentle, child-friendly language.

BEHAVIOR RULES:
1. When a quest is activated, greet the child warmly using the provided opening phrase.
2. Listen carefully to the child's response and evaluate it against the quest goal.
3. If the child's speech matches the quest intent — even if short, hesitant, mispronounced, or childlike —
   IMMEDIATELY call complete_quest(). Do NOT wait for a perfect answer.
4. If the child goes off-topic or repeats words without meaning (Echolalia),
   do NOT call complete_quest(). Respond with one short, warm sentence to redirect them.
5. Keep all responses to 1-2 sentences maximum. Never explain or lecture.
6. ALWAYS speak Vietnamese, regardless of what language the child uses."""

WAITING_INSTRUCTIONS = (
    BASE_INSTRUCTIONS + "\n\n"
    "You are currently in standby mode. Wait for a quest to be assigned by the system."
)


def build_quest_instructions(quest_name: str, phrases: list[str]) -> str:
    """Build dynamic system prompt for active quest from Unity VR."""
    return (
        BASE_INSTRUCTIONS + "\n\n"
        f"=== ACTIVE QUEST ===\n"
        f"Quest goal (child's task): '{quest_name}'\n"
        f"Teacher sample phrases (Vietnamese): {phrases}\n\n"
        f"EVALUATION RULE:\n"
        f"Listen to the child speak into the microphone. "
        f"If their speech shows intent to complete '{quest_name}' "
        f"(even if short, hesitant, or using child language), "
        f"you MUST immediately call complete_quest() — no delay, no follow-up question first."
    )


# ---------------------------------------------------------------------------
# TTS Phrase Cache
# ---------------------------------------------------------------------------
async def _synthesize_phrases(
    tts: agents_tts.TTS,
    phrases: list[str],
    cache: dict[tuple[str, str, float, str], list[rtc.AudioFrame]],
    profile_key: tuple[str, str, float],
) -> None:
    """Pre-synthesize phrases concurrently into per-job cache."""
    to_synthesize = [
        phrase for phrase in phrases if (*profile_key, phrase) not in cache
    ]
    if not to_synthesize:
        return

    async def _synth_one(text: str) -> None:
        frames: list[rtc.AudioFrame] = []
        try:
            async with tts.synthesize(text) as stream:
                async for event in stream:
                    if event.frame:
                        frames.append(event.frame)
            cache[(*profile_key, text)] = frames
            logger.debug("[TTS] Cached %d frames for phrase: %r", len(frames), text)
        except Exception as exc:
            logger.warning("[TTS] Failed to synthesize phrase %r: %s", text, exc)

    await asyncio.gather(*[_synth_one(p) for p in to_synthesize])


async def _frames_to_async_gen(frames: list[rtc.AudioFrame]):
    """Convert AudioFrame list to async generator for session.say."""
    for frame in frames:
        yield frame


# ---------------------------------------------------------------------------
# Voice Profile Registry (Single Agent Dynamic TTS Switching)
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class VoiceProfile:
    """TTS voice configuration for an NPC voice profile."""

    voice_name: str
    speaking_rate: float = 1.0
    gender: str = "NEUTRAL"
    pitch: float = 0.0


DEFAULT_VOICE_PROFILE = VoiceProfile(
    voice_name="vi-VN-Chirp3-HD-Aoede",
    speaking_rate=1.0,
    gender="FEMALE",
)

DEFAULT_VOICE_PROFILES: dict[str, VoiceProfile] = {
    "teacher-npc": VoiceProfile(
        voice_name="vi-VN-Chirp3-HD-Aoede",
        speaking_rate=1.0,
        gender="FEMALE",
    ),
    "peer-npc": VoiceProfile(
        voice_name="vi-VN-Chirp3-HD-Puck",
        speaking_rate=1.0,
        gender="MALE",
    ),
}


class VoiceProfileRegistry:
    """Registry mapping npc_binding_id to TTS VoiceProfile configurations."""

    def __init__(
        self,
        profiles: dict[str, VoiceProfile] | None = None,
        default_profile: VoiceProfile = DEFAULT_VOICE_PROFILE,
    ) -> None:
        self._profiles: dict[str, VoiceProfile] = dict(
            profiles if profiles is not None else DEFAULT_VOICE_PROFILES
        )
        self._default_profile = default_profile

    def register(self, npc_binding_id: str, profile: VoiceProfile) -> None:
        self._profiles[npc_binding_id] = profile

    def get(self, npc_binding_id: str | None) -> tuple[VoiceProfile, bool]:
        """Return (VoiceProfile, is_fallback).

        If npc_binding_id is unknown or empty, returns (default_profile, True).
        """
        if not npc_binding_id or npc_binding_id not in self._profiles:
            return self._default_profile, True
        return self._profiles[npc_binding_id], False


def _apply_voice_profile(session: AgentSession, profile: VoiceProfile) -> None:
    """Apply voice profile options to TTS if supported."""
    tts = getattr(session, "tts", None)
    if tts is not None and hasattr(tts, "update_options"):
        try:
            tts.update_options(
                language="vi-VN",
                voice_name=profile.voice_name,
                speaking_rate=profile.speaking_rate,
            )
            logger.debug("[TTS] Updated TTS options for voice: %s", profile.voice_name)
        except Exception as exc:
            logger.warning("[TTS] Failed to update TTS options: %s", exc)


# ---------------------------------------------------------------------------
# Runtime & State Management
# ---------------------------------------------------------------------------
class QuestState:
    """Track active quest state per job instance."""

    def __init__(self) -> None:
        self.active: bool = False
        self.name: str = ""
        self.phrases: list[str] = []

    def set_active_quest(self, name: str, phrases: list[str]) -> None:
        self.active = True
        self.name = name
        self.phrases = phrases

    def reset(self) -> None:
        self.active = False
        self.name = ""
        self.phrases = []


class JobRuntime:
    """Container for per-job isolated runtime state and background tasks."""

    def __init__(self, job_ctx: JobContext) -> None:
        self.job_ctx = job_ctx
        self.quest_state = QuestState()
        self.voice_runtime = VoiceQuestRuntime()
        self.command_runtime = VoiceCommandRuntime()
        self.voice_registry = VoiceProfileRegistry()
        self.packet_lock = asyncio.Lock()
        self.command_lock = asyncio.Lock()
        self.publication_lock = asyncio.Lock()
        self.verbal_hint_lock = asyncio.Lock()
        self.activation_task: asyncio.Task[Any] | None = None
        self.active_speech_handle: Any = None
        self.active_opening_handle: Any = None
        self.active_verbal_hint_task: asyncio.Task[Any] | None = None
        self.active_verbal_hint_activation_id: str | None = None
        self.active_verbal_hint_handle: Any = None
        self.active_silence_reminder_handle: Any = None
        self.silence_reminder: ActivationSilenceReminderV2 | None = None
        self.silence_reminder_interrupt: Any = None
        self.matched_sent: set[str] = set()
        self.background_tasks: set[asyncio.Task[Any]] = set()
        self.tts_cache: dict[tuple[str, str, float, str], list[rtc.AudioFrame]] = {}
        self.cache_activation_changed = asyncio.Event()

    def spawn(self, coro: Any) -> None:
        """Spawn background task with auto-cleanup on completion."""
        task = asyncio.create_task(coro)
        self.background_tasks.add(task)
        task.add_done_callback(self.background_tasks.discard)

    def interrupt_active_silence_reminder(self, *, reset_timer: bool = False) -> None:
        if reset_timer and self.silence_reminder is not None:
            self.silence_reminder.note_user_activity()
        if self.silence_reminder and self.silence_reminder.is_reminder_playing:
            interrupt = self.silence_reminder_interrupt
            if callable(interrupt):
                with contextlib.suppress(Exception):
                    interrupt()

    def cancel_silence_reminder(self, activation_id: str | None = None) -> None:
        if self.silence_reminder is None:
            return
        self.interrupt_active_silence_reminder()
        self.silence_reminder.cancel(activation_id)


# ---------------------------------------------------------------------------
# Helper Utilities
# ---------------------------------------------------------------------------
class SpeechOutcome(Enum):
    COMPLETED = auto()
    FAILED = auto()
    CANCELLED = auto()
    STALE = auto()


def start_owned_speech(
    runtime: JobRuntime,
    invocation: tuple[str, str],
    text: str,
    session: AgentSession,
    *,
    audio: Any = None,
    allow_interruptions: bool = True,
) -> SpeechHandle | None:
    """Start speech and save its handle synchronously under its owner."""
    kind, _invocation_id = invocation
    say_options: dict[str, Any] = {"allow_interruptions": allow_interruptions}
    if audio is not None:
        say_options["audio"] = audio
    handle = session.say(text, **say_options)
    if kind == "script":
        runtime.active_speech_handle = handle
    elif kind == "hint":
        runtime.active_verbal_hint_handle = handle
    elif kind == "opening":
        runtime.active_opening_handle = handle
    elif kind == "reminder":
        runtime.active_silence_reminder_handle = handle
        interrupt = getattr(handle, "interrupt", None)
        if callable(interrupt):
            runtime.silence_reminder_interrupt = interrupt
    return handle


def start_captured_owned_speech(
    runtime: JobRuntime,
    invocation: tuple[str, str],
    text: str,
    session: AgentSession,
    capture_context: CaptureContext,
    *,
    allow_interruptions: bool = True,
) -> SpeechHandle | None:
    """Copy cache ownership into SDK-created tasks, then reset the caller."""
    token = CAPTURE_CONTEXT.set(capture_context)
    try:
        return start_owned_speech(
            runtime,
            invocation,
            text,
            session,
            allow_interruptions=allow_interruptions,
        )
    finally:
        CAPTURE_CONTEXT.reset(token)


async def finish_owned_speech(
    handle: SpeechHandle | None, is_current: Any
) -> SpeechOutcome:
    """Wait for an owned handle and classify its terminal SDK outcome."""
    if handle is None:
        return SpeechOutcome.FAILED
    try:
        wait_for_playout = getattr(handle, "wait_for_playout", None)
        if callable(wait_for_playout):
            await wait_for_playout()
        elif hasattr(handle, "__await__"):
            await handle
    except asyncio.CancelledError:
        interrupt = getattr(handle, "interrupt", None)
        done = getattr(handle, "done", None)
        if (
            callable(interrupt)
            and (not callable(done) or not done())
            and not bool(getattr(handle, "interrupted", False))
        ):
            with contextlib.suppress(Exception):
                interrupt()
        return SpeechOutcome.CANCELLED
    except Exception:
        return SpeechOutcome.FAILED

    if not is_current():
        return SpeechOutcome.STALE
    if bool(getattr(handle, "interrupted", False)):
        return SpeechOutcome.CANCELLED

    done = getattr(handle, "done", None)
    exception = getattr(handle, "exception", None)
    if callable(done) and done() is True and callable(exception):
        try:
            if exception() is not None:
                return SpeechOutcome.FAILED
        except asyncio.InvalidStateError:
            return SpeechOutcome.FAILED
    return SpeechOutcome.COMPLETED


async def send_rtc_event(
    runtime: JobRuntime, event_name: str, extra_data: Optional[dict[str, Any]] = None
) -> bool:
    """Broadcast JSON DataPacket to Unity VR and Web Dashboard."""
    if not runtime.job_ctx or not runtime.job_ctx.room:
        logger.warning("[RTC] Cannot send '%s': room not connected", event_name)
        return False

    try:
        payload_dict: dict[str, Any] = {"event": event_name}
        if extra_data:
            payload_dict.update(extra_data)

        payload_bytes = json.dumps(payload_dict).encode("utf-8")
        await runtime.job_ctx.room.local_participant.publish_data(
            payload_bytes, reliable=True
        )
        logger.info("[RTC] Sent event: %s | payload: %s", event_name, payload_dict)
        return True
    except Exception as err:
        logger.error("[RTC] Error sending event '%s': %s", event_name, err)
        return False


async def send_v2_packet(runtime: JobRuntime, packet: dict[str, Any]) -> bool:
    """Publish a correlated V2 quest packet on the dedicated reliable topic."""
    if not runtime.job_ctx or not runtime.job_ctx.room:
        logger.warning("[RTC] Cannot send V2 packet: room not connected")
        return False

    try:
        await runtime.job_ctx.room.local_participant.publish_data(
            json.dumps(packet).encode("utf-8"),
            reliable=True,
            topic=VOICE_TOPIC,
        )
        logger.info("[RTC] Sent V2 packet: %s", packet)
        return True
    except Exception as err:
        logger.error("[RTC] Error sending V2 packet: %s", err)
        return False


async def clear_agent_chat_history(agent: Agent) -> None:
    """Reset LLM chat context for new quest."""
    try:
        empty_ctx = llm.ChatContext.empty()
        await agent.update_chat_ctx(empty_ctx)
        logger.info("[SESSION] Chat history cleared for new quest context")
    except Exception as err:
        logger.error("[SESSION] Error clearing chat history: %s", err)


# ---------------------------------------------------------------------------
# Agent Definition
# ---------------------------------------------------------------------------
class TeacherAgent(Agent):
    """Voice AI teacher agent evaluated against quest targets."""

    def __init__(self, runtime: JobRuntime) -> None:
        super().__init__(instructions=WAITING_INSTRUCTIONS)
        self._runtime = runtime
        self._evaluation_activation_id: str | None = None

    async def tts_node(
        self, text: AsyncIterable[str], model_settings: Any
    ) -> AsyncIterator[rtc.AudioFrame]:
        """Tee only an explicitly owned fixed utterance into bounded capture."""
        capture_context = CAPTURE_CONTEXT.get()
        if capture_context is None:
            async for frame in Agent.default.tts_node(self, text, model_settings):
                yield frame
            return

        activity = self._get_activity_or_raise()
        conn_options = activity.session.conn_options.tts_conn_options
        capture = FixedCapture(capture_context)
        input_text: list[str] = []
        stream_complete = False

        async def tracked_text() -> AsyncIterator[str]:
            async for chunk in text:
                if chunk:
                    input_text.append(chunk)
                yield chunk

        try:
            async for event in stream_fixed_tts(
                tracked_text(), capture_context.profile, conn_options=conn_options
            ):
                capture.append(event)
                yield event.frame
            stream_complete = True
        finally:
            submitted_text = "".join(input_text)
            input_complete = submitted_text == capture_context.final_text
            completed = capture.finish(
                input_complete=input_complete,
                stream_complete=stream_complete,
                expected_segment_count=1 if input_complete else 0,
            )
            worker = getattr(self._runtime, "tts_publication_worker", None)
            if completed is not None and worker is not None:
                worker.try_enqueue(completed)

    async def on_enter(self) -> None:
        logger.info("[AGENT] TeacherAgent ready in room (standby mode)")

    async def complete_quest(self, context: RunContext) -> str:
        """Call immediately when child speech matches active quest intent."""
        return await self._complete_activation(self._evaluation_activation_id)

    async def _complete_activation(self, activation_id: str | None) -> str:
        runtime = self._runtime
        async with runtime.packet_lock:
            if not activation_id or not runtime.voice_runtime.mark_matched(
                activation_id
            ):
                return "No active quest"
            runtime.cancel_silence_reminder(activation_id)
            runtime.quest_state.reset()
        await _publish_activation_status(runtime, activation_id)
        return "Quest marked completed"

    async def install_evaluation_tool(self, activation_id: str) -> None:
        # Old LLM generations retain their original callable and immutable ID.
        @function_tool(name="complete_quest")
        async def complete_activation(context: RunContext) -> str:
            """Call immediately when child speech matches active quest intent."""
            return await self._complete_activation(activation_id)

        await self.update_tools([complete_activation])

    def bind_evaluation_activation(self, activation_id: str | None) -> None:
        """Bind LLM tool calls to the activation whose prompt was installed."""
        self._evaluation_activation_id = activation_id


# ---------------------------------------------------------------------------
# Prewarm
# ---------------------------------------------------------------------------
def prewarm(proc: JobProcess) -> None:
    """Preload Silero VAD once per worker process."""
    proc.userdata["vad"] = silero.VAD.load()


# ---------------------------------------------------------------------------
# Entrypoint & Packet Processing
# ---------------------------------------------------------------------------
async def _wait_for_vr_participant(room: rtc.Room) -> rtc.RemoteParticipant:
    """Select Unity's existing vr_ identity contract, never a dashboard listener."""
    available = asyncio.get_running_loop().create_future()

    def on_participant_connected(participant: rtc.RemoteParticipant) -> None:
        if (
            not available.done()
            and participant.kind == rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
            and participant.identity.startswith("vr_")
            and len(participant.identity) > 3
        ):
            available.set_result(participant)

    # Register before scanning so a Unity join cannot fall between the two steps.
    room.on("participant_connected", on_participant_connected)
    try:
        for participant in room.remote_participants.values():
            on_participant_connected(participant)
        return await available
    finally:
        room.off("participant_connected", on_participant_connected)


def _cache_schema_version(participant: rtc.RemoteParticipant) -> int | None:
    """Read the cache capability carried in trusted VR participant metadata."""
    metadata = getattr(participant, "metadata", "")
    if metadata is None or metadata == "":
        return None
    try:
        value = metadata if isinstance(metadata, dict) else json.loads(metadata)
    except (TypeError, json.JSONDecodeError) as exc:
        raise CacheServiceError("invalid VR participant metadata") from exc
    if not isinstance(value, dict):
        raise CacheServiceError("invalid VR participant metadata")
    version = value.get("tts_cache_schema_version")
    if version is None:
        return None
    if type(version) is not int or version != 1:
        raise CacheServiceError("unsupported TTS cache schema version")
    return version


def _trusted_teacher_scope(participant: Any, room: Any) -> dict[str, str] | None:
    """Read the immutable lesson scope issued on the selected VR participant."""
    if (
        participant is None
        or getattr(participant, "kind", None)
        != rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
    ):
        return None
    metadata = getattr(participant, "metadata", None)
    try:
        value = metadata if isinstance(metadata, dict) else json.loads(metadata or "")
    except (TypeError, json.JSONDecodeError):
        return None
    if not isinstance(value, dict):
        return None
    session_id = value.get("session_id")
    host_id = value.get("host_id")
    lesson_runtime = value.get("lesson_runtime")
    if (
        value.get("participant_role") != "vr"
        or not isinstance(session_id, str)
        or not session_id
        or session_id != getattr(room, "name", None)
        or not isinstance(host_id, str)
        or not host_id
        or not isinstance(lesson_runtime, str)
        or lesson_runtime
        not in {"lesson_graph_v2", "quiz", "exploration", "legacy_hello_friend"}
    ):
        return None
    return {
        "session_id": session_id,
        "host_id": host_id,
        "lesson_runtime": lesson_runtime,
    }


def _is_current_room_participant(room: Any, participant: Any) -> bool:
    """Reject queued packets from a departed/replaced participant SID."""
    identity = getattr(participant, "identity", None)
    current = getattr(room, "remote_participants", {}).get(identity)
    if current is participant:
        return True
    participant_sid = getattr(participant, "sid", None)
    return bool(participant_sid and getattr(current, "sid", None) == participant_sid)


def _is_paired_vr_participant(runtime: JobRuntime, participant: Any) -> bool:
    if not (
        participant is not None
        and participant.kind == rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
        and participant.identity == getattr(runtime, "vr_participant_identity", None)
    ):
        return False
    if getattr(runtime, "cache_scope", None) is None:
        return True
    try:
        return _cache_schema_version(participant) == 1
    except CacheServiceError:
        return False


def _paired_identity_matches_scope(
    participant_identity: str, scope: SessionScopeV1
) -> bool:
    return participant_identity == f"vr_{scope.pin}"


async def entrypoint(ctx: JobContext) -> None:
    runtime = JobRuntime(ctx)

    # 1. Connect to LiveKit Room
    await ctx.connect(auto_subscribe=AutoSubscribe.SUBSCRIBE_ALL)
    logger.info("[LIVEKIT] Connected to room: %s", ctx.room.name)

    # 2. Validate environment credentials
    gemini_key = os.getenv("GOOGLE_API_KEY")
    gcloud_creds = os.getenv("GOOGLE_APPLICATION_CREDENTIALS")

    if not gemini_key:
        logger.error("[ENV] Missing GOOGLE_API_KEY")
        await send_rtc_event(
            runtime, "AGENT_INIT_FAILED", {"reason": "missing_gemini_key"}
        )
        await ctx.room.disconnect()
        return

    if not gcloud_creds or not os.path.exists(gcloud_creds):
        logger.error("[ENV] Service account JSON not found: %s", gcloud_creds)
        await send_rtc_event(
            runtime, "AGENT_INIT_FAILED", {"reason": "missing_gcloud_credentials"}
        )
        await ctx.room.disconnect()
        return

    try:
        participant = await _wait_for_vr_participant(ctx.room)
        cache_schema_version = _cache_schema_version(participant)
    except CacheServiceError:
        logger.error("[CACHE] VR cache capability metadata is invalid")
        await send_rtc_event(
            runtime, "AGENT_INIT_FAILED", {"reason": "invalid_vr_cache_metadata"}
        )
        await ctx.room.disconnect()
        return
    cache_client = None
    cache_scope = None
    if cache_schema_version == 1:
        try:
            cache_client = TtsCacheClientV1()
            cache_scope = await asyncio.to_thread(
                cache_client.get_session_scope, ctx.room.name, required=True
            )
            if cache_scope is None:
                raise CacheServiceError("retained session snapshot is missing")
        except CacheServiceError:
            logger.error("[CACHE] Retained session settings are unavailable")
            await send_rtc_event(
                runtime,
                "AGENT_INIT_FAILED",
                {"reason": "tts_session_snapshot_unavailable"},
            )
            await ctx.room.disconnect()
            return
        if not _paired_identity_matches_scope(participant.identity, cache_scope):
            logger.error("[CACHE] VR participant does not match retained session PIN")
            await send_rtc_event(
                runtime,
                "AGENT_INIT_FAILED",
                {"reason": "vr_session_pairing_mismatch"},
            )
            await ctx.room.disconnect()
            return
    runtime.vr_participant_identity = participant.identity
    runtime.vr_participant_sid = getattr(participant, "sid", None)
    runtime.teacher_speech_scope = _trusted_teacher_scope(participant, ctx.room)
    runtime.cache_scope = cache_scope
    runtime.cache_client = cache_client
    runtime.cache_fixed_audio = (
        FixedAudioRuntimeV1.from_scope(cache_scope) if cache_scope else None
    )
    runtime.cache_ready = asyncio.Event()
    if cache_scope:
        runtime.cache_packet_lock = asyncio.Lock()

        async def announce_cache_artifact(_capture: Any, artifact: dict[str, Any]):
            fixed: FixedAudioRuntimeV1 | None = runtime.cache_fixed_audio
            if fixed is None or runtime.cache_scope is None:
                return
            packet = {
                "contract_version": 1,
                "event": "AUDIO_CACHE_READY",
                "session_id": fixed.session_id,
                "agent_instance_id": fixed.agent_instance_id,
                **artifact,
            }
            try:
                validated_packet = parse_cache_packet(packet)
            except CacheValidationError:
                logger.warning("[CACHE] Ignoring invalid publication receipt")
                return
            await _send_cache_packet(runtime, validated_packet)

        runtime.tts_publication_worker = PublicationWorker(
            cache_client.publish, on_success=announce_cache_artifact
        )
        runtime.tts_publication_worker.start()

    # 3. Initialize Audio Pipeline & Agent
    vad = ctx.proc.userdata.get("vad") or silero.VAD.load(
        min_silence_duration=0.5,
    )
    session = AgentSession(
        stt=google.STT(
            languages=["vi-VN"],
            model="chirp_3",
            location="us",
            spoken_punctuation=False,
        ),
        llm=google.LLM(model="gemini-3.5-flash-lite", api_key=gemini_key),
        tts=google.TTS(
            language="vi-VN",
            voice_name=DEFAULT_VOICE_PROFILE.voice_name,
            speaking_rate=DEFAULT_VOICE_PROFILE.speaking_rate,
            audio_encoding=gcp_tts.AudioEncoding.OGG_OPUS,
            sample_rate=24000,
            use_streaming=True,
        ),
        vad=vad,
        preemptive_generation=True,
    )

    agent = TeacherAgent(runtime)
    agent_ready = asyncio.Event()

    async def play_silence_reminder(activation_id: str) -> bool:
        return await _play_silence_reminder(session, runtime, activation_id)

    runtime.silence_reminder = ActivationSilenceReminderV2(play_silence_reminder)
    runtime.silence_reminder_interrupt = session.interrupt
    if cache_scope is not None:
        runtime.silence_reminder.set_enabled(False)

    def on_user_input_transcribed(event: Any) -> None:
        transcript = getattr(event, "transcript", "")
        if not isinstance(transcript, str) or not transcript.strip():
            return
        runtime.interrupt_active_silence_reminder()
        if getattr(runtime, "cache_scope", None) is not None:
            runtime.spawn(_cancel_cache_reminder(runtime, "user_speaking"))
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.note_user_activity()

    def on_user_state_changed(event: Any) -> None:
        state = getattr(event, "new_state", "")
        state = getattr(state, "value", state)
        speaking = state == "speaking"
        if speaking:
            runtime.interrupt_active_silence_reminder()
            if getattr(runtime, "cache_scope", None) is not None:
                runtime.spawn(_cancel_cache_reminder(runtime, "user_speaking"))
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.set_user_speaking(speaking)

    def on_agent_state_changed(event: Any) -> None:
        state = getattr(event, "new_state", "idle")
        state = getattr(state, "value", state)
        if state in {"initializing", "thinking"}:
            runtime.interrupt_active_silence_reminder()
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.set_agent_state(state)

    def on_room_reconnecting() -> None:
        runtime.interrupt_active_silence_reminder()
        if getattr(runtime, "cache_scope", None) is not None:
            runtime.cache_ready.clear()
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.reconnecting()
            if getattr(runtime, "cache_scope", None) is not None:
                runtime.silence_reminder.set_enabled(False)

    def on_room_reconnected() -> None:
        if getattr(runtime, "cache_scope", None) is not None:
            runtime.cache_ready.clear()
            runtime.spawn(_announce_cache_agent(runtime))
        elif runtime.silence_reminder is not None:
            runtime.silence_reminder.reconnected()

    session.on("user_input_transcribed", on_user_input_transcribed)
    session.on("user_state_changed", on_user_state_changed)
    session.on("agent_state_changed", on_agent_state_changed)
    ctx.room.on("reconnecting", on_room_reconnecting)
    ctx.room.on("reconnected", on_room_reconnected)

    async def on_job_shutdown(_reason: str) -> None:
        runtime.cancel_silence_reminder()
        session.off("user_input_transcribed", on_user_input_transcribed)
        session.off("user_state_changed", on_user_state_changed)
        session.off("agent_state_changed", on_agent_state_changed)
        ctx.room.off("reconnecting", on_room_reconnecting)
        ctx.room.off("reconnected", on_room_reconnected)
        pending = tuple(runtime.background_tasks)
        for task in pending:
            task.cancel()
        await asyncio.gather(*pending, return_exceptions=True)
        publication_worker = getattr(runtime, "tts_publication_worker", None)
        if publication_worker is not None:
            await publication_worker.drain(timeout_seconds=5)

    register_shutdown_callback = getattr(ctx, "add_shutdown_callback", None)
    if callable(register_shutdown_callback):
        register_shutdown_callback(on_job_shutdown)

    # 4. Register DataPacket handler BEFORE session.start to prevent packet loss race condition
    def on_data_received(data_packet: rtc.DataPacket) -> None:
        sender = data_packet.participant
        is_paired_vr = _is_paired_vr_participant(runtime, sender)
        is_standard_remote = (
            sender is not None
            and getattr(sender, "kind", None)
            == rtc.ParticipantKind.PARTICIPANT_KIND_STANDARD
        )
        if not is_paired_vr and not is_standard_remote:
            return

        async def _process_packet():
            # Wait for session initialization if packet arrives during handshake
            await agent_ready.wait()
            if not _is_current_room_participant(ctx.room, sender):
                return
            if not is_paired_vr:
                scope = getattr(runtime, "teacher_speech_scope", None)
                selected_vr = ctx.room.remote_participants.get(
                    getattr(runtime, "vr_participant_identity", None)
                )
                selected_sid = getattr(runtime, "vr_participant_sid", None)
                current_selected_sid = getattr(selected_vr, "sid", None)
                if (
                    scope is None
                    or selected_vr is None
                    or (
                        selected_sid is not None
                        and current_selected_sid != selected_sid
                    )
                    or _trusted_teacher_scope(selected_vr, ctx.room) != scope
                ):
                    return
                await try_handle_teacher_script(
                    session,
                    data_packet,
                    session_id=scope["session_id"],
                    host_id=scope["host_id"],
                    lesson_runtime=scope["lesson_runtime"],
                )
                return
            if not _is_paired_vr_participant(runtime, sender):
                return
            try:
                await _process_data_packet(
                    agent,
                    session,
                    runtime,
                    data_packet.topic,
                    data_packet.data,
                )

            except Exception as err:
                logger.error("[DATA] Error processing DataPacket: %s", err)

        runtime.spawn(_process_packet())

    ctx.room.on("data_received", on_data_received)

    # 5. Start AgentSession
    try:
        logger.info(
            "[AUDIO] Linking Unity microphone participant: %s", participant.identity
        )
        logger.info("[SESSION] Starting AgentSession...")
        await session.start(
            room=ctx.room,
            agent=agent,
            room_options=room_io.RoomOptions(participant_identity=participant.identity),
        )
    except (Exception, asyncio.CancelledError):
        runtime.cancel_silence_reminder()
        session.off("user_input_transcribed", on_user_input_transcribed)
        session.off("user_state_changed", on_user_state_changed)
        session.off("agent_state_changed", on_agent_state_changed)
        ctx.room.off("reconnecting", on_room_reconnecting)
        ctx.room.off("reconnected", on_room_reconnected)
        ctx.room.off("data_received", on_data_received)
        pending = tuple(runtime.background_tasks)
        for task in pending:
            task.cancel()
        await asyncio.gather(*pending, return_exceptions=True)
        publication_worker = getattr(runtime, "tts_publication_worker", None)
        if publication_worker is not None:
            await publication_worker.drain(timeout_seconds=5)
        raise
    agent_ready.set()
    logger.info("[AGENT] Agent pipeline active and ready for packets")
    if cache_scope is not None:
        await _announce_cache_agent(runtime)


async def _send_cache_packet(runtime: JobRuntime, packet: dict[str, Any]) -> bool:
    room = getattr(runtime.job_ctx, "room", None)
    if room is None:
        return False
    try:
        await room.local_participant.publish_data(
            json.dumps(packet, ensure_ascii=False).encode("utf-8"),
            reliable=True,
            topic=CACHE_TOPIC,
        )
        return True
    except Exception:
        logger.warning("[CACHE] Failed to publish cache lifecycle packet")
        return False


async def _announce_cache_agent(runtime: JobRuntime) -> bool:
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    if fixed is None:
        return False
    return await _send_cache_packet(
        runtime,
        {
            "contract_version": 1,
            "event": "FIXED_AUDIO_HELLO",
            "session_id": fixed.session_id,
            "agent_instance_id": fixed.agent_instance_id,
        },
    )


async def _send_fixed_activity(
    runtime: JobRuntime,
    invocation: dict[str, Any],
    owner_revision: int,
    phase: str,
    source: str,
    *,
    status: str = "PENDING",
    reason: str = "",
) -> bool:
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    fixed.activity_sequence += 1
    packet = activity_packet(
        invocation,
        owner_revision=owner_revision,
        activity_sequence=fixed.activity_sequence,
        phase=phase,
        source=source,
        status=status,
        reason=reason,
    )
    fixed.current_activity = packet
    return await _send_cache_packet(runtime, packet)


async def _cancel_cache_reminder(runtime: JobRuntime, reason: str) -> None:
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    invocation = getattr(runtime, "cache_reminder_invocation", None)
    if fixed is None or invocation is None:
        return
    await _send_cache_packet(
        runtime,
        {
            "contract_version": 1,
            "event": "FIXED_AUDIO_REMINDER_CANCEL",
            **invocation_fields(invocation),
            "agent_instance_id": fixed.agent_instance_id,
            "reason": reason,
        },
    )
    invocation_key = fixed.invocation_key(invocation)
    waiter = fixed.reminder_waiters.pop(invocation_key, None)
    fixed.reminder_owner_revisions.pop(invocation_key, None)
    fixed.reminder_terminal_status.pop(invocation_key, None)
    if waiter is not None and not waiter.done():
        waiter.set_result(False)
    runtime.cache_reminder_invocation = None


async def _process_cache_packet(
    session: AgentSession, runtime: JobRuntime, payload: bytes | str
) -> None:
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    scope: SessionScopeV1 | None = getattr(runtime, "cache_scope", None)
    if fixed is None or scope is None:
        return
    try:
        packet = parse_cache_packet(payload)
    except CacheValidationError as exc:
        logger.warning("[CACHE] Ignoring malformed cache packet: %s", exc)
        return

    if packet.get("session_id") != scope.session_id:
        return
    event = packet["event"]
    if event == "FIXED_AUDIO_RECONCILE":
        if packet["agent_instance_id"] != fixed.agent_instance_id:
            return
        previous_run_id = fixed.run_id
        if not fixed.accept_run(packet["run_id"]):
            return
        if (
            previous_run_id is not None
            and previous_run_id != fixed.run_id
            and runtime.silence_reminder is not None
        ):
            runtime.silence_reminder.clear_external_audio_activity()
        if (
            previous_run_id is not None
            and previous_run_id != fixed.run_id
            and fixed.current_task is not None
            and not fixed.current_task.done()
        ):
            fixed.current_task.cancel()
            await asyncio.gather(fixed.current_task, return_exceptions=True)
        remote_active = set(packet["active_activation_ids"])
        fixed.active_activation_ids = remote_active
        fixed.completed_opening_activation_ids = (
            set(packet.get("completed_opening_activation_ids", [])) & remote_active
        )
        runtime.cache_activation_changed.set()
        if (
            fixed.current_invocation is not None
            and fixed.current_invocation["activation_id"] not in remote_active
            and fixed.current_task is not None
            and not fixed.current_task.done()
        ):
            fixed.current_task.cancel()
        current_activation = runtime.voice_runtime.active_activation_id
        if current_activation and current_activation not in remote_active:
            runtime.cancel_silence_reminder(current_activation)
        activity = packet["current_activity"]
        if activity is not None and _accept_external_activity(
            runtime, activity, authoritative=True
        ):
            fixed.current_activity = activity
            _apply_external_activity(runtime, activity)
        elif activity is None:
            fixed.external_activity = None
            fixed.current_activity = None
            if runtime.silence_reminder is not None:
                runtime.silence_reminder.clear_external_audio_activity()
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.set_enabled(True)
            runtime.silence_reminder.reconnected()
            if (
                activity is not None
                and activity["event"] == "FIXED_AUDIO_ACTIVITY"
                and activity["kind"] == "opening"
                and activity["phase"] == "terminal"
                and activity["status"] == "SUCCESS"
                and activity["activation_id"] == current_activation
            ):
                runtime.silence_reminder.opening_finished(current_activation)
            elif current_activation is not None:
                _apply_reconciled_opening_proof(runtime, current_activation)
        runtime.cache_ready.set()
        return

    if not runtime.cache_ready.is_set() or packet.get("run_id") != fixed.run_id:
        return
    invocation_key = fixed.invocation_key(packet)
    if event == "FIXED_AUDIO_STREAM_REQUEST":
        runtime.spawn(_handle_fixed_audio_stream_request(session, runtime, packet))
    elif event == "FIXED_AUDIO_CANCEL":
        fixed.cancelled_invocations[invocation_key] = packet["owner_revision"]
        runtime.cache_activation_changed.set()
        if len(fixed.cancelled_invocations) > 1024:
            fixed.cancelled_invocations.pop(next(iter(fixed.cancelled_invocations)))
        if (
            fixed.is_current(packet)
            and packet["owner_revision"] == fixed.owner_revision
            and fixed.current_task is not None
        ):
            fixed.current_task.cancel()
    elif event == "FIXED_AUDIO_REMINDER_CANCEL":
        if packet["agent_instance_id"] != fixed.agent_instance_id:
            return
        waiter = fixed.reminder_waiters.pop(invocation_key, None)
        fixed.reminder_owner_revisions.pop(invocation_key, None)
        fixed.reminder_terminal_status.pop(invocation_key, None)
        if (
            runtime.cache_reminder_invocation is not None
            and fixed.invocation_key(runtime.cache_reminder_invocation)
            == invocation_key
        ):
            runtime.cache_reminder_invocation = None
        if waiter is not None and not waiter.done():
            waiter.set_result(False)
    elif event == "FIXED_AUDIO_ACTIVITY":
        if packet.get("agent_instance_id") is not None:
            return
        if not _accept_external_activity(runtime, packet):
            return
        _apply_external_activity(runtime, packet)
        fixed.current_activity = packet
        if (
            packet["kind"] == "automatic_reminder"
            and invocation_key in fixed.reminder_waiters
        ):
            fixed.reminder_owner_revisions[invocation_key] = packet["owner_revision"]
            if packet["phase"] == "terminal":
                fixed.reminder_terminal_status[invocation_key] = packet["status"]
                await _finish_cache_reminder_from_activity(runtime, packet)
        if (
            packet["kind"] == "opening"
            and packet["phase"] == "terminal"
            and packet["status"] == "SUCCESS"
            and runtime.silence_reminder is not None
        ):
            runtime.silence_reminder.opening_finished(packet["activation_id"])
    elif event == "FIXED_AUDIO_DONE":
        if packet["agent_instance_id"] != fixed.agent_instance_id:
            return
        if not fixed.is_current(packet):
            # Reminder requests are separately owned and can complete without
            # replacing the agent's stream owner.
            waiter = fixed.reminder_waiters.get(invocation_key)
            expected_revision = fixed.reminder_owner_revisions.get(invocation_key)
            if (
                waiter is None
                or waiter.done()
                or expected_revision != packet["owner_revision"]
            ):
                return
            success = (
                packet["status"] == "SUCCESS"
                and fixed.reminder_terminal_status.get(invocation_key) == "SUCCESS"
            )
            waiter.set_result(success)
            fixed.reminder_waiters.pop(invocation_key, None)
            fixed.reminder_owner_revisions.pop(invocation_key, None)
            fixed.reminder_terminal_status.pop(invocation_key, None)
            if (
                runtime.cache_reminder_invocation is not None
                and fixed.invocation_key(runtime.cache_reminder_invocation)
                == invocation_key
            ):
                runtime.cache_reminder_invocation = None
                if success and runtime.voice_runtime.can_continue(
                    packet["activation_id"]
                ):
                    await send_v2_packet(
                        runtime,
                        accepted_reminder_packet(
                            packet["activation_id"],
                            packet["npc_binding_id"],
                            packet["invocation_id"],
                        ),
                    )


def _apply_external_activity(runtime: JobRuntime, packet: dict[str, Any]) -> None:
    if runtime.silence_reminder is not None:
        runtime.silence_reminder.set_external_audio_activity(
            packet["invocation_id"], packet["phase"], packet["activity_sequence"]
        )


def _accept_external_activity(
    runtime: JobRuntime, packet: dict[str, Any], *, authoritative: bool = False
) -> bool:
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    scope: SessionScopeV1 | None = getattr(runtime, "cache_scope", None)
    if fixed is None or scope is None:
        return False
    try:
        scope.config.profile_for_npc(packet["npc_binding_id"])
    except (CacheValidationError, KeyError, TypeError):
        return False
    return fixed.accept_external_activity(packet, authoritative=authoritative)


def _apply_reconciled_opening_proof(runtime: JobRuntime, activation_id: str) -> bool:
    """Apply authenticated VR evidence once its matching quest is installed."""
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    scope: SessionScopeV1 | None = getattr(runtime, "cache_scope", None)
    reminder: ActivationSilenceReminderV2 | None = getattr(
        runtime, "silence_reminder", None
    )
    if (
        fixed is None
        or scope is None
        or reminder is None
        or activation_id not in fixed.active_activation_ids
        or activation_id not in fixed.completed_opening_activation_ids
        or not runtime.voice_runtime.can_continue(activation_id)
    ):
        return False
    npc_binding_id = runtime.voice_runtime.active_npc_binding_id or ""
    try:
        scope.config.profile_for_npc(npc_binding_id)
    except (CacheValidationError, KeyError, TypeError):
        return False
    reminder.opening_finished(activation_id)
    return True


async def _finish_cache_reminder_from_activity(
    runtime: JobRuntime, packet: dict[str, Any]
) -> None:
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    invocation_key = fixed.invocation_key(packet)
    waiter = fixed.reminder_waiters.pop(invocation_key, None)
    fixed.reminder_owner_revisions.pop(invocation_key, None)
    fixed.reminder_terminal_status.pop(invocation_key, None)
    if waiter is None or waiter.done():
        return
    matches_current_request = bool(
        runtime.cache_reminder_invocation is not None
        and fixed.invocation_key(runtime.cache_reminder_invocation) == invocation_key
    )
    is_current = bool(
        matches_current_request
        and packet["activation_id"] in fixed.active_activation_ids
        and runtime.voice_runtime.can_continue(packet["activation_id"])
    )
    success = (
        packet["status"] == "SUCCESS"
        and packet["source"] in {"local", "stream"}
        and is_current
    )
    waiter.set_result(success)
    if not matches_current_request:
        return
    runtime.cache_reminder_invocation = None
    if success:
        await send_v2_packet(
            runtime,
            accepted_reminder_packet(
                packet["activation_id"],
                packet["npc_binding_id"],
                packet["invocation_id"],
            ),
        )


async def _handle_fixed_audio_stream_request(
    session: AgentSession, runtime: JobRuntime, packet: dict[str, Any]
) -> None:
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    activation_state = _fixed_stream_activation_state(runtime, packet)
    if activation_state == "stale":
        return

    while activation_state == "pending":
        runtime.cache_activation_changed.clear()
        activation_state = _fixed_stream_activation_state(runtime, packet)
        if activation_state == "pending":
            await runtime.cache_activation_changed.wait()
    if activation_state == "stale":
        return

    try:
        profile = runtime.cache_scope.config.profile(packet["profile_id"])
        if (
            runtime.cache_scope.config.profile_for_npc(packet["npc_binding_id"])
            != profile
        ):
            raise CacheValidationError("profile does not match NPC")
        if compute_key(packet["text"], profile) != packet["key"]:
            raise CacheValidationError("request cache key mismatch")
    except CacheValidationError:
        profile = None

    owner_lock = getattr(runtime, "cache_owner_lock", None)
    if owner_lock is None:
        runtime.cache_owner_lock = asyncio.Lock()
        owner_lock = runtime.cache_owner_lock
    current_task = asyncio.current_task()
    async with owner_lock:
        terminal = fixed.terminal_for(packet)
        if terminal is not None:
            await _send_cache_packet(
                runtime,
                done_packet(
                    packet,
                    fixed.agent_instance_id,
                    terminal["owner_revision"],
                    terminal["status"],
                    terminal["reason"],
                ),
            )
            return
        if fixed.current_invocation and fixed.invocation_key(
            fixed.current_invocation
        ) == fixed.invocation_key(packet):
            return
        if packet["owner_revision"] <= fixed.owner_revision:
            return
        previous_task = fixed.current_task
        if (
            previous_task is not None
            and not previous_task.done()
            and previous_task is not current_task
        ):
            previous_task.cancel()
            await asyncio.gather(previous_task, return_exceptions=True)
            if (
                packet["session_id"] != fixed.session_id
                or packet["run_id"] != fixed.run_id
                or _fixed_stream_activation_state(runtime, packet) != "active"
            ):
                return
        fixed.owner_revision = packet["owner_revision"]
        invocation = {**packet}
        fixed.current_invocation = invocation
        fixed.current_task = current_task
        runtime.active_speech_handle = None

    if profile is None:
        await _finish_fixed_audio_request(
            runtime, invocation, packet["owner_revision"], "FAILED", "invalid_profile"
        )
        return

    if (
        fixed.cancelled_invocations.get(fixed.invocation_key(packet))
        == packet["owner_revision"]
    ):
        await _finish_fixed_audio_request(
            runtime, invocation, packet["owner_revision"], "CANCELLED", "skip"
        )
        return

    capture_context = CaptureContext(
        final_text=packet["text"],
        config=runtime.cache_scope.config,
        profile=profile,
        key=packet["key"],
        rejection_epoch=packet["rejection_epoch"],
        invocation=invocation_fields(packet),
    )
    handle = None
    status = "FAILED"
    reason = "stream_failed"
    try:
        await _send_fixed_activity(
            runtime, invocation, packet["owner_revision"], "preparing", "none"
        )
        handle = start_captured_owned_speech(
            runtime,
            ("script", packet["invocation_id"]),
            packet["text"],
            session,
            capture_context,
            allow_interruptions=True,
        )
        if handle is None:
            raise RuntimeError("speech handle unavailable")
        await _send_fixed_activity(
            runtime, invocation, packet["owner_revision"], "stream_selected", "stream"
        )
        await _send_fixed_activity(
            runtime, invocation, packet["owner_revision"], "playing", "stream"
        )
        outcome = await finish_owned_speech(
            handle,
            lambda: _fixed_stream_is_current(runtime, packet),
        )
        status = (
            "SUCCESS"
            if outcome is SpeechOutcome.COMPLETED
            else (
                "CANCELLED"
                if outcome in {SpeechOutcome.CANCELLED, SpeechOutcome.STALE}
                else "FAILED"
            )
        )
        reason = "" if status == "SUCCESS" else outcome.name.lower()
    except asyncio.CancelledError:
        status, reason = "CANCELLED", "superseded"
        raise
    except Exception:
        logger.warning("[CACHE] Fixed stream invocation failed")
    finally:
        if fixed.is_current(packet):
            await _send_fixed_activity(
                runtime,
                invocation,
                packet["owner_revision"],
                "terminal",
                "stream",
                status=status,
                reason=reason,
            )
            await _send_cache_packet(
                runtime,
                done_packet(
                    invocation,
                    fixed.agent_instance_id,
                    packet["owner_revision"],
                    status,
                    reason,
                ),
            )
            fixed.remember_terminal(
                invocation, status, packet["owner_revision"], reason
            )
            fixed.current_invocation = None
            fixed.current_task = None
            if runtime.active_speech_handle is handle:
                runtime.active_speech_handle = None


def _fixed_stream_activation_state(runtime: JobRuntime, packet: dict[str, Any]) -> str:
    """Return active, pending, or stale using VR activation ownership."""
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    activation_id = packet["activation_id"]
    if (
        fixed.cancelled_invocations.get(fixed.invocation_key(packet))
        == packet["owner_revision"]
    ):
        return "active"
    if packet["kind"] == "dialogue":
        return "active" if activation_id in fixed.active_activation_ids else "stale"
    if (
        runtime.voice_runtime.can_continue(activation_id)
        and runtime.voice_runtime.active_npc_binding_id == packet["npc_binding_id"]
    ):
        return "active"
    if activation_id not in fixed.active_activation_ids:
        return "stale"
    return "pending"


def _fixed_stream_is_current(runtime: JobRuntime, packet: dict[str, Any]) -> bool:
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    if not (
        fixed.is_current(packet)
        and fixed.owner_revision == packet["owner_revision"]
        and packet["session_id"] == fixed.session_id
        and packet["run_id"] == fixed.run_id
        and packet["activation_id"] in fixed.active_activation_ids
    ):
        return False
    if packet["kind"] == "dialogue":
        return True
    return bool(
        runtime.voice_runtime.can_continue(packet["activation_id"])
        and runtime.voice_runtime.active_npc_binding_id == packet["npc_binding_id"]
    )


async def _finish_fixed_audio_request(
    runtime: JobRuntime,
    invocation: dict[str, Any],
    owner_revision: int,
    status: str,
    reason: str,
) -> None:
    fixed: FixedAudioRuntimeV1 = runtime.cache_fixed_audio
    await _send_fixed_activity(runtime, invocation, owner_revision, "preparing", "none")
    await _send_fixed_activity(
        runtime,
        invocation,
        owner_revision,
        "terminal",
        "stream",
        status=status,
        reason=reason,
    )
    await _send_cache_packet(
        runtime,
        done_packet(
            invocation, fixed.agent_instance_id, owner_revision, status, reason
        ),
    )
    fixed.remember_terminal(invocation, status, owner_revision, reason)
    fixed.current_invocation = None
    fixed.current_task = None


async def _process_data_packet(
    agent: TeacherAgent,
    session: AgentSession,
    runtime: JobRuntime,
    topic: str,
    payload: bytes | str,
) -> None:
    """Route V2 voice/cache packets while ignoring unrelated topics."""
    raw_text = payload.decode("utf-8") if isinstance(payload, bytes) else payload
    if topic == CACHE_TOPIC:
        packet_lock = getattr(runtime, "cache_packet_lock", None)
        if packet_lock is None:
            await _process_cache_packet(session, runtime, raw_text)
        else:
            async with packet_lock:
                await _process_cache_packet(session, runtime, raw_text)
        return
    if topic == VOICE_TOPIC:
        await _process_v2_packet(agent, session, runtime, raw_text)
        return
    if topic == REMOTE_TOPIC:
        return
    return


async def _process_v2_packet(
    agent: TeacherAgent,
    session: AgentSession,
    runtime: JobRuntime,
    payload: bytes | str,
) -> None:
    """Apply a strict V2 request without allowing old callbacks to win."""
    try:
        packet = parse_unity_packet(payload)
    except PacketValidationError as exc:
        logger.warning("[V2] Ignoring malformed V2 packet: %s", exc)
        return

    if isinstance(packet, SpeakScriptV2):
        await _handle_speak_script_v2(session, runtime, packet)
        return
    if isinstance(packet, CancelSpeakScriptV2):
        await _handle_cancel_speak_script_v2(runtime, packet)
        return
    if isinstance(packet, VerbalHintV2):
        await _handle_verbal_hint_v2(session, runtime, packet)
        return

    async with runtime.packet_lock:
        if isinstance(packet, SetActiveQuest):
            previous = runtime.voice_runtime.active_activation_id
            try:
                disposition = runtime.voice_runtime.activate(packet)
            except ValueError as exc:
                logger.warning(
                    "[V2] Rejected activation %s: %s", packet.activation_id, exc
                )
                return
            fixed_audio = getattr(runtime, "cache_fixed_audio", None)
            if fixed_audio is not None:
                runtime.cache_activation_changed.set()
            if disposition is ActivationDisposition.NEW:
                try:
                    await _reset_activation(agent, session, runtime)
                    if runtime.silence_reminder is not None:
                        runtime.silence_reminder.activate(
                            packet.activation_id,
                            packet.speech_silence_timeout_seconds,
                            enabled=packet.enable_speech_silence_reminder,
                        )
                        _apply_reconciled_opening_proof(runtime, packet.activation_id)
                except Exception:
                    runtime.voice_runtime.mark_failed(packet.activation_id)
                    logger.exception(
                        "[V2] Failed to reset activation %s", packet.activation_id
                    )
                    await _publish_activation_status(runtime, packet.activation_id)
                    return
                runtime.matched_sent.intersection_update(
                    key
                    for key in tuple(runtime.matched_sent)
                    if runtime.voice_runtime.status(key) is not None
                )
                runtime.quest_state.set_active_quest(
                    packet.quest_goal, list(packet.phrases)
                )
                if previous:
                    await _publish_activation_status(runtime, previous)
                if runtime.voice_runtime.claim_opening(packet.activation_id):
                    runtime.activation_task = asyncio.create_task(
                        _handle_quest_activation(
                            agent,
                            session,
                            runtime,
                            packet.activation_id,
                            packet.quest_goal,
                            list(packet.phrases),
                            packet.npc_binding_id,
                        )
                    )
            await _publish_activation_status(runtime, packet.activation_id)
        elif isinstance(packet, CancelActiveQuest):
            if runtime.voice_runtime.cancel(packet):
                fixed_audio = getattr(runtime, "cache_fixed_audio", None)
                if fixed_audio is not None:
                    runtime.cache_activation_changed.set()
                await _reset_activation(agent, session, runtime)
            await _publish_activation_status(runtime, packet.activation_id)
    await asyncio.sleep(0)


async def _handle_cancel_speak_script_v2(
    runtime: JobRuntime, packet: CancelSpeakScriptV2
) -> None:
    """Interrupt one matching therapist script without cancelling its quest."""
    async with runtime.command_lock:
        if not runtime.command_runtime.cancel(
            packet.activation_id,
            packet.sequence_id,
            packet.npc_binding_id,
            packet.reason,
        ):
            return

        handle = runtime.active_speech_handle
        if handle is not None:
            done = getattr(handle, "done", None)
            if not callable(done) or not done():
                with contextlib.suppress(Exception):
                    handle.interrupt()
            if runtime.active_speech_handle is handle:
                runtime.active_speech_handle = None

        await send_v2_packet(
            runtime,
            speak_script_done_packet(
                packet.activation_id,
                packet.sequence_id,
                packet.npc_binding_id,
                status="CANCELLED",
                reason=packet.reason,
            ),
        )


async def _reset_activation(
    agent: TeacherAgent, session: AgentSession, runtime: JobRuntime
) -> None:
    runtime.cancel_silence_reminder()
    reminder_handle = runtime.active_silence_reminder_handle
    reminder_done = getattr(reminder_handle, "done", None) if reminder_handle else None
    reminder_interrupt = getattr(reminder_handle, "interrupt", None)
    if (
        reminder_handle is not None
        and callable(reminder_interrupt)
        and (not callable(reminder_done) or not reminder_done())
        and not bool(getattr(reminder_handle, "interrupted", False))
    ):
        with contextlib.suppress(Exception):
            reminder_interrupt()
    opening_handle = runtime.active_opening_handle
    runtime.active_opening_handle = None
    if opening_handle is not None:
        interrupt = getattr(opening_handle, "interrupt", None)
        done = getattr(opening_handle, "done", None)
        if callable(interrupt) and (not callable(done) or not done()):
            with contextlib.suppress(Exception):
                interrupt()
    task = runtime.activation_task
    runtime.activation_task = None
    if task and not task.done():
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
    hint_handle = runtime.active_verbal_hint_handle
    runtime.active_verbal_hint_handle = None
    hint_done = getattr(hint_handle, "done", None) if hint_handle else None
    if hint_handle and (not callable(hint_done) or not hint_done()):
        with contextlib.suppress(Exception):
            hint_handle.interrupt()
    hint_task = runtime.active_verbal_hint_task
    if hint_task and not hint_task.done() and hint_task is not asyncio.current_task():
        hint_task.cancel()
        await asyncio.gather(hint_task, return_exceptions=True)
    if runtime.active_verbal_hint_task is hint_task:
        runtime.active_verbal_hint_task = None
        runtime.active_verbal_hint_activation_id = None
    if runtime.active_speech_handle and not runtime.active_speech_handle.done():
        with contextlib.suppress(Exception):
            runtime.active_speech_handle.interrupt()
        runtime.active_speech_handle = None
    active_cmd = runtime.command_runtime.active_command
    if active_cmd:
        act_id = active_cmd.activation_id
        seq_id = active_cmd.sequence_id
        npc_id = active_cmd.npc_binding_id
        runtime.command_runtime.cancel_active(reason="activation_reset")
        runtime.spawn(
            send_v2_packet(
                runtime,
                speak_script_done_packet(
                    act_id,
                    seq_id,
                    npc_id,
                    status="CANCELLED",
                    reason="activation_reset",
                ),
            )
        )
    else:
        runtime.command_runtime.cancel_active(reason="activation_reset")
    runtime.quest_state.reset()
    runtime.tts_cache.clear()
    agent.bind_evaluation_activation(None)
    session.interrupt()
    session.clear_user_turn()
    await agent.update_tools([])
    await clear_agent_chat_history(agent)
    await agent.update_instructions(WAITING_INSTRUCTIONS)


async def _publish_activation_status(runtime: JobRuntime, activation_id: str) -> None:
    async with runtime.publication_lock:
        status = runtime.voice_runtime.status(activation_id)
        if status is None:
            return
        if status.name == "MATCHED" and activation_id not in runtime.matched_sent:
            if not await send_v2_packet(runtime, quest_matched_packet(activation_id)):
                return
            runtime.matched_sent.add(activation_id)
        await send_v2_packet(
            runtime,
            quest_status_packet(
                activation_id, status.name, runtime.voice_runtime.reason(activation_id)
            ),
        )


async def _handle_quest_activation(
    agent: TeacherAgent,
    session: AgentSession,
    runtime: JobRuntime,
    activation_id: str,
    quest_name: str,
    phrases: list[str],
    npc_binding_id: str = "",
) -> None:
    """Update system prompt and speak opening phrase with cached audio."""
    try:
        if not runtime.voice_runtime.can_continue(activation_id):
            return

        cache_scope: SessionScopeV1 | None = getattr(runtime, "cache_scope", None)
        if cache_scope is not None:
            # The VR owns the opening utterance in cache-aware sessions. The
            # agent configures evaluation only and starts silence timing after
            # VR reports the opening's terminal lifecycle.
            cache_scope.config.profile_for_npc(npc_binding_id)
            await clear_agent_chat_history(agent)
            agent.bind_evaluation_activation(activation_id)
            await agent.install_evaluation_tool(activation_id)
            await agent.update_instructions(
                build_quest_instructions(quest_name, phrases)
            )
            return

        profile, is_fallback = runtime.voice_registry.get(npc_binding_id)
        if is_fallback and npc_binding_id:
            logger.warning(
                "[V2] Unknown npc_binding_id %r for quest activation %s. Falling back to default voice %r.",
                npc_binding_id,
                activation_id,
                profile.voice_name,
            )
        _apply_voice_profile(session, profile)

        opening = (
            random.choice(phrases)
            if phrases
            else f"Nào, chúng ta bắt đầu bài học '{quest_name}' nhé!"
        )

        new_instructions = build_quest_instructions(quest_name, phrases)
        profile_key = (npc_binding_id, profile.voice_name, profile.speaking_rate)
        await asyncio.gather(
            clear_agent_chat_history(agent),
            _synthesize_phrases(
                session.tts,
                phrases,
                runtime.tts_cache,
                profile_key,
            ),
        )
        if not runtime.voice_runtime.can_continue(activation_id):
            return
        agent.bind_evaluation_activation(activation_id)
        await agent.install_evaluation_tool(activation_id)
        await agent.update_instructions(new_instructions)
        if not runtime.voice_runtime.can_continue(activation_id):
            return

        cached_frames = runtime.tts_cache.get((*profile_key, opening))
        audio_arg = _frames_to_async_gen(cached_frames) if cached_frames else None
        logger.info(
            '[AGENT] Opening phrase (cached=%s): "%s"',
            cached_frames is not None,
            opening,
        )
        handle = start_owned_speech(
            runtime,
            ("opening", activation_id),
            opening,
            session,
            audio=audio_arg,
            allow_interruptions=True,
        )
        outcome = await finish_owned_speech(
            handle,
            lambda: (
                runtime.active_opening_handle is handle
                and runtime.voice_runtime.can_continue(activation_id)
            ),
        )
        if runtime.active_opening_handle is handle:
            runtime.active_opening_handle = None
        if outcome is SpeechOutcome.FAILED:
            raise RuntimeError("opening speech generation failed")
        if outcome is not SpeechOutcome.COMPLETED:
            return
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.opening_finished(activation_id)
    except Exception as err:
        logger.error("[AGENT] Error activating quest context: %s", err)
        async with runtime.packet_lock:
            if runtime.voice_runtime.mark_failed(activation_id):
                runtime.cancel_silence_reminder(activation_id)
                runtime.quest_state.reset()
                agent.bind_evaluation_activation(None)
                session.interrupt()
                session.clear_user_turn()
                runtime.tts_cache.clear()
                await agent.update_tools([])
                await clear_agent_chat_history(agent)
                await agent.update_instructions(WAITING_INSTRUCTIONS)
                await _publish_activation_status(runtime, activation_id)


async def _play_silence_reminder(
    session: AgentSession,
    runtime: JobRuntime,
    activation_id: str,
) -> bool:
    """Play one cached activation phrase and publish accepted evidence after playout."""
    if not runtime.voice_runtime.can_continue(activation_id):
        return False

    npc_binding_id = runtime.voice_runtime.active_npc_binding_id or ""
    phrases = runtime.voice_runtime.active_phrases
    if not npc_binding_id or not phrases:
        return False

    cache_scope: SessionScopeV1 | None = getattr(runtime, "cache_scope", None)
    fixed: FixedAudioRuntimeV1 | None = getattr(runtime, "cache_fixed_audio", None)
    if cache_scope is not None and fixed is not None:
        if not runtime.cache_ready.is_set() or fixed.run_id is None:
            return False
        try:
            profile = cache_scope.config.profile_for_npc(npc_binding_id)
        except CacheValidationError:
            return False
        phrase = random.choice(phrases)
        invocation = {
            "contract_version": 1,
            "event": "FIXED_AUDIO_REMINDER_REQUEST",
            "session_id": fixed.session_id,
            "run_id": fixed.run_id,
            "activation_id": activation_id,
            "invocation_id": uuid.uuid4().hex,
            "npc_binding_id": npc_binding_id,
            "kind": "automatic_reminder",
            "agent_instance_id": fixed.agent_instance_id,
            "text": phrase,
            "profile_id": profile.profile_id,
            "key": compute_key(phrase, profile),
        }
        waiter = asyncio.get_running_loop().create_future()
        invocation_key = fixed.invocation_key(invocation)
        fixed.reminder_waiters[invocation_key] = waiter
        runtime.cache_reminder_invocation = invocation
        if runtime.silence_reminder is not None:
            runtime.silence_reminder.set_owned_reminder_invocation(
                invocation["invocation_id"]
            )
        if not await _send_cache_packet(runtime, invocation):
            fixed.reminder_waiters.pop(invocation_key, None)
            runtime.cache_reminder_invocation = None
            if runtime.silence_reminder is not None:
                runtime.silence_reminder.clear_owned_reminder_invocation(
                    invocation["invocation_id"]
                )
            return False
        try:
            return await waiter
        except asyncio.CancelledError:
            await _cancel_cache_reminder(runtime, "user_speaking")
            raise
        finally:
            if runtime.silence_reminder is not None:
                runtime.silence_reminder.clear_owned_reminder_invocation(
                    invocation["invocation_id"]
                )

    phrase = random.choice(phrases)
    profile, is_fallback = runtime.voice_registry.get(npc_binding_id)
    if is_fallback:
        logger.warning(
            "[V2] Unknown npc_binding_id %r for silence reminder %s. "
            "Falling back to default voice %r.",
            npc_binding_id,
            activation_id,
            profile.voice_name,
        )
    _apply_voice_profile(session, profile)
    cache_key = (npc_binding_id, profile.voice_name, profile.speaking_rate, phrase)
    cached_frames = runtime.tts_cache.get(cache_key)
    audio_arg = _frames_to_async_gen(cached_frames) if cached_frames else None

    handle = None
    try:
        handle = start_owned_speech(
            runtime,
            ("reminder", activation_id),
            phrase,
            session,
            audio=audio_arg,
            allow_interruptions=True,
        )
        if handle is None:
            return False
        outcome = await finish_owned_speech(
            handle,
            lambda: (
                runtime.active_silence_reminder_handle is handle
                and runtime.voice_runtime.can_continue(activation_id)
                and runtime.voice_runtime.active_npc_binding_id == npc_binding_id
            ),
        )
        if outcome is not SpeechOutcome.COMPLETED:
            return False

        return await send_v2_packet(
            runtime,
            accepted_reminder_packet(
                activation_id,
                npc_binding_id,
                uuid.uuid4().hex,
            ),
        )
    except asyncio.CancelledError:
        raise
    except Exception:
        logger.exception(
            "[V2] Silence reminder failed for activation %s", activation_id
        )
        return False
    finally:
        if runtime.active_silence_reminder_handle is handle:
            runtime.active_silence_reminder_handle = None
            runtime.silence_reminder_interrupt = None


async def _handle_verbal_hint_v2(
    session: AgentSession,
    runtime: JobRuntime,
    packet: VerbalHintV2,
) -> None:
    """Speak one deduplicated phrase for the active activation and NPC."""
    if getattr(runtime, "cache_scope", None) is not None:
        # Cache-aware VR owns hint selection and must send the selected exact
        # phrase through FIXED_AUDIO_STREAM_REQUEST.
        return
    if (
        runtime.voice_runtime.active_activation_id == packet.activation_id
        and runtime.voice_runtime.status(packet.activation_id)
        is ActivationStatus.ACTIVE
    ):
        runtime.interrupt_active_silence_reminder(reset_timer=True)
    async with runtime.verbal_hint_lock:
        async with runtime.packet_lock:
            phrase = runtime.voice_runtime.claim_verbal_hint(packet)
            if phrase is None:
                logger.info(
                    "[V2] Ignoring stale, duplicate, or wrong-NPC verbal hint %s",
                    packet.command_id,
                )
                return
            hint_task = asyncio.current_task()
            runtime.active_verbal_hint_task = hint_task
            runtime.active_verbal_hint_activation_id = packet.activation_id

        handle = None
        try:
            profile, is_fallback = runtime.voice_registry.get(packet.npc_binding_id)
            if is_fallback:
                logger.warning(
                    "[V2] Unknown npc_binding_id %r for verbal hint %s. "
                    "Falling back to default voice %r.",
                    packet.npc_binding_id,
                    packet.command_id,
                    profile.voice_name,
                )
            _apply_voice_profile(session, profile)
            profile_key = (
                packet.npc_binding_id,
                profile.voice_name,
                profile.speaking_rate,
            )
            cache_key = (*profile_key, phrase)
            if cache_key not in runtime.tts_cache:
                await _synthesize_phrases(
                    session.tts,
                    [phrase],
                    runtime.tts_cache,
                    profile_key,
                )

            if (
                not runtime.voice_runtime.can_continue(packet.activation_id)
                or runtime.voice_runtime.active_npc_binding_id != packet.npc_binding_id
            ):
                return

            cached_frames = runtime.tts_cache.get(cache_key)
            audio_arg = _frames_to_async_gen(cached_frames) if cached_frames else None
            handle = start_owned_speech(
                runtime,
                ("hint", packet.command_id),
                phrase,
                session,
                audio=audio_arg,
                allow_interruptions=True,
            )
            outcome = await finish_owned_speech(
                handle,
                lambda: (
                    runtime.active_verbal_hint_handle is handle
                    and runtime.active_verbal_hint_task is hint_task
                    and runtime.voice_runtime.can_continue(packet.activation_id)
                    and runtime.voice_runtime.active_npc_binding_id
                    == packet.npc_binding_id
                ),
            )
            if outcome is SpeechOutcome.FAILED:
                logger.error(
                    "[V2] Verbal hint generation failed: %s", packet.command_id
                )
        except asyncio.CancelledError:
            raise
        except Exception as err:
            logger.error(
                "[V2] Error handling verbal hint %s: %s", packet.command_id, err
            )
        finally:
            if runtime.active_verbal_hint_task is hint_task:
                runtime.active_verbal_hint_task = None
                runtime.active_verbal_hint_activation_id = None
            if runtime.active_verbal_hint_handle is handle:
                runtime.active_verbal_hint_handle = None


async def _handle_speak_script_v2(
    session: AgentSession,
    runtime: JobRuntime,
    packet: SpeakScriptV2,
) -> None:
    """Execute a correlated V2 dialogue node through LiveKit, awaiting playout."""
    if not _matches_active_speak_script_scope(runtime, packet):
        logger.warning(
            "[V2] Ignoring stale SPEAK_SCRIPT %s/%s",
            packet.activation_id,
            packet.sequence_id,
        )
        return

    runtime.interrupt_active_silence_reminder(reset_timer=True)
    profile, is_fallback = runtime.voice_registry.get(packet.npc_binding_id)
    if is_fallback:
        logger.warning(
            "[V2] Unknown npc_binding_id %r not found in VoiceProfileRegistry. Falling back to default voice %r.",
            packet.npc_binding_id,
            profile.voice_name,
        )
    _apply_voice_profile(session, profile)

    async with runtime.command_lock:
        if not _matches_active_speak_script_scope(runtime, packet):
            return
        disposition = runtime.command_runtime.submit(packet)
        if disposition is CommandDisposition.REPLAY:
            status = runtime.command_runtime.status(
                packet.activation_id, packet.sequence_id
            )
            if status in (
                CommandStatus.COMPLETED,
                CommandStatus.CANCELLED,
                CommandStatus.FAILED,
            ):
                mapped_status = (
                    "SUCCESS" if status == CommandStatus.COMPLETED else status.name
                )
                await send_v2_packet(
                    runtime,
                    speak_script_done_packet(
                        packet.activation_id,
                        packet.sequence_id,
                        packet.npc_binding_id,
                        status=mapped_status,
                        reason=runtime.command_runtime.reason(
                            packet.activation_id, packet.sequence_id
                        ),
                    ),
                )
            return

        if disposition is CommandDisposition.REJECTED:
            logger.warning(
                "[V2] Rejected duplicate/conflicting SPEAK_SCRIPT %s/%s",
                packet.activation_id,
                packet.sequence_id,
            )
            return

        if runtime.active_speech_handle and not runtime.active_speech_handle.done():
            with contextlib.suppress(Exception):
                runtime.active_speech_handle.interrupt()
            runtime.active_speech_handle = None

        runtime.command_runtime.start(packet.activation_id, packet.sequence_id)

    handle = None
    try:
        logger.info(
            "[V2] Speaking scripted text for NPC %r: %r",
            packet.npc_binding_id,
            packet.text,
        )
        handle = start_owned_speech(
            runtime,
            ("script", packet.sequence_id),
            packet.text,
            session,
            allow_interruptions=True,
        )
        if handle is None:
            logger.warning(
                "[V2] session.say returned None for %s/%s",
                packet.activation_id,
                packet.sequence_id,
            )
            if runtime.command_runtime.mark_failed(
                packet.activation_id, packet.sequence_id, reason="say_returned_none"
            ):
                await send_v2_packet(
                    runtime,
                    speak_script_done_packet(
                        packet.activation_id,
                        packet.sequence_id,
                        packet.npc_binding_id,
                        status="FAILED",
                        reason="say_returned_none",
                    ),
                )
            return

        if not runtime.command_runtime.can_continue(
            packet.activation_id, packet.sequence_id
        ) or not _matches_active_speak_script_scope(runtime, packet):
            with contextlib.suppress(Exception):
                handle.interrupt()
            return
        outcome = await finish_owned_speech(
            handle,
            lambda: (
                runtime.active_speech_handle is handle
                and runtime.command_runtime.can_continue(
                    packet.activation_id, packet.sequence_id
                )
                and _matches_active_speak_script_scope(runtime, packet)
            ),
        )

        if outcome is SpeechOutcome.STALE:
            return
        if outcome is SpeechOutcome.CANCELLED:
            logger.warning(
                "[V2] Dialogue playback interrupted for %s/%s",
                packet.activation_id,
                packet.sequence_id,
            )
            marked = runtime.command_runtime.mark_completed(
                packet.activation_id,
                packet.sequence_id,
                status="CANCELLED",
                reason="interrupted",
            )
            if marked:
                await send_v2_packet(
                    runtime,
                    speak_script_done_packet(
                        packet.activation_id,
                        packet.sequence_id,
                        packet.npc_binding_id,
                        status="CANCELLED",
                        reason="interrupted",
                    ),
                )
        elif outcome is SpeechOutcome.FAILED:
            failure_reason = "speech_generation_failed"
            get_exception = getattr(handle, "exception", None)
            done = getattr(handle, "done", None)
            if callable(get_exception) and callable(done) and done() is True:
                with contextlib.suppress(Exception):
                    failure = get_exception()
                    if failure is not None:
                        failure_reason = str(failure)
            marked = runtime.command_runtime.mark_completed(
                packet.activation_id,
                packet.sequence_id,
                status="FAILED",
                reason=failure_reason,
            )
            if marked:
                await send_v2_packet(
                    runtime,
                    speak_script_done_packet(
                        packet.activation_id,
                        packet.sequence_id,
                        packet.npc_binding_id,
                        status="FAILED",
                        reason=failure_reason,
                    ),
                )
        else:
            logger.info(
                "[V2] Dialogue playout completed for %s/%s",
                packet.activation_id,
                packet.sequence_id,
            )
            marked = runtime.command_runtime.mark_completed(
                packet.activation_id,
                packet.sequence_id,
                status="SUCCESS",
            )
            if marked:
                await send_v2_packet(
                    runtime,
                    speak_script_done_packet(
                        packet.activation_id,
                        packet.sequence_id,
                        packet.npc_binding_id,
                        status="SUCCESS",
                    ),
                )
    except (Exception, asyncio.CancelledError) as err:
        logger.error(
            "[V2] Exception during dialogue playout %s/%s: %s",
            packet.activation_id,
            packet.sequence_id,
            err,
        )
        status = "CANCELLED" if isinstance(err, asyncio.CancelledError) else "FAILED"
        if runtime.command_runtime.mark_completed(
            packet.activation_id,
            packet.sequence_id,
            status=status,
            reason=str(err),
        ):
            await send_v2_packet(
                runtime,
                speak_script_done_packet(
                    packet.activation_id,
                    packet.sequence_id,
                    packet.npc_binding_id,
                    status=status,
                    reason=str(err),
                ),
            )
        if isinstance(err, asyncio.CancelledError):
            raise
    finally:
        if runtime.active_speech_handle is handle:
            runtime.active_speech_handle = None


def _matches_active_speak_script_scope(
    runtime: JobRuntime, packet: SpeakScriptV2
) -> bool:
    status = runtime.voice_runtime.status(packet.activation_id)
    if status is None:
        return True
    return (
        runtime.voice_runtime.active_activation_id == packet.activation_id
        and runtime.voice_runtime.active_npc_binding_id == packet.npc_binding_id
        and status is ActivationStatus.ACTIVE
    )


if __name__ == "__main__":
    cli.run_app(WorkerOptions(entrypoint_fnc=entrypoint, prewarm_fnc=prewarm))
