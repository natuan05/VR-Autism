"""
LiveKit AI Voice Agent for VR Autism Application
Pipeline: Google STT (Chirp 3) -> Gemini LLM (3.5 Flash Lite) -> Google TTS (Chirp 3 HD) + Silero VAD
SDK: LiveKit Agents 1.6+
"""

import asyncio
import json
import logging
import os
import random
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
)
from livekit.agents import tts as agents_tts
from livekit.agents.voice import Agent, AgentSession
from livekit.plugins import google, silero

from voice_contract_v2 import (
    VOICE_TOPIC,
    CancelActiveQuest,
    PacketValidationError,
    SetActiveQuest,
    parse_unity_packet,
    quest_matched_packet,
    quest_status_packet,
)
from voice_quest_runtime_v2 import ActivationDisposition, VoiceQuestRuntime

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
    cache: dict[str, list[rtc.AudioFrame]],
) -> None:
    """Pre-synthesize phrases concurrently into per-job cache."""
    to_synthesize = [p for p in phrases if p not in cache]
    if not to_synthesize:
        return

    async def _synth_one(text: str) -> None:
        frames: list[rtc.AudioFrame] = []
        try:
            async with tts.synthesize(text) as stream:
                async for event in stream:
                    if event.frame:
                        frames.append(event.frame)
            cache[text] = frames
            logger.debug("[TTS] Cached %d frames for phrase: %r", len(frames), text)
        except Exception as exc:
            logger.warning("[TTS] Failed to synthesize phrase %r: %s", text, exc)

    await asyncio.gather(*[_synth_one(p) for p in to_synthesize])


async def _frames_to_async_gen(frames: list[rtc.AudioFrame]):
    """Convert AudioFrame list to async generator for session.say."""
    for frame in frames:
        yield frame


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
        self.background_tasks: set[asyncio.Task[Any]] = set()
        self.tts_cache: dict[str, list[rtc.AudioFrame]] = {}

    def spawn(self, coro: Any) -> None:
        """Spawn background task with auto-cleanup on completion."""
        task = asyncio.create_task(coro)
        self.background_tasks.add(task)
        task.add_done_callback(self.background_tasks.discard)


# ---------------------------------------------------------------------------
# Helper Utilities
# ---------------------------------------------------------------------------
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

    async def on_enter(self) -> None:
        logger.info("[AGENT] TeacherAgent ready in room (standby mode)")

    @function_tool()
    async def complete_quest(self, context: RunContext) -> str:
        """Call immediately when child speech matches active quest intent."""
        runtime = self._runtime
        activation_id = self._evaluation_activation_id
        if not activation_id or not runtime.voice_runtime.mark_matched(activation_id):
            logger.warning("[TOOL] complete_quest invoked with no active quest")
            return "Hiện tại chưa có Quest nào được kích hoạt."

        quest_name = runtime.voice_runtime.active_goal or ""
        logger.info("[TOOL] Quest completed: %s", quest_name)
        runtime.quest_state.reset()

        await send_v2_packet(runtime, quest_matched_packet(activation_id))
        await send_v2_packet(runtime, quest_status_packet(activation_id, "MATCHED"))
        return f"Quest '{quest_name}' marked completed"

    def bind_evaluation_activation(self, activation_id: str) -> None:
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
            voice_name="vi-VN-Chirp3-HD-Aoede",
            audio_encoding=gcp_tts.AudioEncoding.OGG_OPUS,
            sample_rate=24000,
            use_streaming=True,
        ),
        vad=vad,
        preemptive_generation=True,
    )

    agent = TeacherAgent(runtime)
    agent_ready = asyncio.Event()

    # 4. Register DataPacket handler BEFORE session.start to prevent packet loss race condition
    def on_data_received(data_packet: rtc.DataPacket) -> None:
        async def _process_packet():
            # Wait for session initialization if packet arrives during handshake
            await agent_ready.wait()
            try:
                raw_text = data_packet.data.decode("utf-8")
                if data_packet.topic == VOICE_TOPIC:
                    await _process_v2_packet(agent, session, runtime, raw_text)
                    return

                data = json.loads(raw_text)
                event_type = data.get("event")
                if event_type == "SPEAK_SCRIPT":
                    text = data.get("text", "").strip()
                    if text:
                        logger.info("[SCRIPT] Received SPEAK_SCRIPT text: %r", text)
                        await _handle_speak_script(session, text)
                elif event_type in ("VERBAL_HINT", "ON_REMINDER"):
                    logger.info("[HINT] Received event: %s", event_type)
                    await _handle_hint_reminder(session, runtime, event_type)

            except Exception as err:
                logger.error("[DATA] Error processing DataPacket: %s", err)

        runtime.spawn(_process_packet())

    ctx.room.on("data_received", on_data_received)

    # 5. Start AgentSession
    logger.info("[SESSION] Starting AgentSession...")
    await session.start(room=ctx.room, agent=agent)
    agent_ready.set()
    logger.info("[AGENT] Agent pipeline active and ready for packets")


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

    if isinstance(packet, SetActiveQuest):
        disposition = runtime.voice_runtime.activate(packet)
        if not runtime.voice_runtime.can_continue(packet.activation_id):
            logger.info("[V2] Ignoring terminal replay: %s", packet.activation_id)
            return

        await send_v2_packet(
            runtime, quest_status_packet(packet.activation_id, "ACTIVE")
        )
        if disposition is ActivationDisposition.REPLAY:
            logger.info(
                "[V2] Replayed activation acknowledged: %s", packet.activation_id
            )
            return

        runtime.quest_state.set_active_quest(packet.quest_goal, list(packet.phrases))
        if runtime.voice_runtime.claim_opening(packet.activation_id):
            logger.info("[V2] Activated quest: %s", packet.activation_id)
            await _handle_quest_activation(
                agent,
                session,
                runtime,
                packet.activation_id,
                packet.quest_goal,
                list(packet.phrases),
            )
        return

    if isinstance(packet, CancelActiveQuest) and runtime.voice_runtime.cancel(packet):
        runtime.quest_state.reset()
        logger.info("[V2] Cancelled activation: %s", packet.activation_id)


async def _handle_quest_activation(
    agent: TeacherAgent,
    session: AgentSession,
    runtime: JobRuntime,
    activation_id: str,
    quest_name: str,
    phrases: list[str],
) -> None:
    """Update system prompt and speak opening phrase with cached audio."""
    try:
        if not runtime.voice_runtime.can_continue(activation_id):
            return
        opening = (
            random.choice(phrases)
            if phrases
            else f"Nào, chúng ta bắt đầu bài học '{quest_name}' nhé!"
        )

        new_instructions = build_quest_instructions(quest_name, phrases)
        await asyncio.gather(
            clear_agent_chat_history(agent),
            _synthesize_phrases(session.tts, phrases, runtime.tts_cache),
        )
        if not runtime.voice_runtime.can_continue(activation_id):
            return
        agent.bind_evaluation_activation(activation_id)
        await agent.update_instructions(new_instructions)
        if not runtime.voice_runtime.can_continue(activation_id):
            return

        cached_frames = runtime.tts_cache.get(opening)
        audio_arg = _frames_to_async_gen(cached_frames) if cached_frames else None
        logger.info(
            '[AGENT] Opening phrase (cached=%s): "%s"',
            cached_frames is not None,
            opening,
        )
        await session.say(
            opening,
            audio=audio_arg,
            allow_interruptions=True,
        )
    except Exception as err:
        logger.error("[AGENT] Error activating quest context: %s", err)


async def _handle_hint_reminder(
    session: AgentSession,
    runtime: JobRuntime,
    event_name: str,
) -> None:
    """Play cached phrase on VERBAL_HINT or ON_REMINDER."""
    try:
        activation_id = runtime.voice_runtime.active_activation_id
        phrases = runtime.voice_runtime.active_phrases
        if not activation_id or not runtime.voice_runtime.can_continue(activation_id):
            logger.warning("[HINT] %s received with no active activation", event_name)
            return
        if not phrases:
            logger.warning(
                "[HINT] %s received with no active quest or phrases", event_name
            )
            return

        phrase = random.choice(phrases)
        cached_frames = runtime.tts_cache.get(phrase)
        audio_arg = _frames_to_async_gen(cached_frames) if cached_frames else None
        logger.info(
            "[HINT] %s playing phrase (cached=%s): %r",
            event_name,
            cached_frames is not None,
            phrase,
        )
        await session.say(
            phrase,
            audio=audio_arg,
            allow_interruptions=True,
        )
    except Exception as err:
        logger.error("[HINT] Error handling %s: %s", event_name, err)


async def _handle_speak_script(
    session: AgentSession,
    text: str,
) -> None:
    """Speak custom text from Web dashboard via live TTS."""
    try:
        logger.info("[SCRIPT] Speaking teacher custom text: %r", text)
        await session.say(
            text,
            allow_interruptions=True,
        )
    except Exception as err:
        logger.error("[SCRIPT] Error handling SPEAK_SCRIPT: %s", err)


if __name__ == "__main__":
    cli.run_app(WorkerOptions(entrypoint_fnc=entrypoint, prewarm_fnc=prewarm))
