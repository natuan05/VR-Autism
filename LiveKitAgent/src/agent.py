"""
LiveKit AI Voice Agent for VR Autism Application
Architecture: Pipeline Mode (Google STT -> Gemini LLM -> Google TTS Chirp 3 HD + Silero VAD)
SDK: LiveKit Agents 1.6+

Features:
  1. Dynamic active quest evaluation via Gemini LLM and `complete_quest` tool.
  2. Instant response using cached TTS frames for opening phrases and hints/reminders.
  3. Real-time handling of VERBAL_HINT, ON_REMINDER, and SPEAK_SCRIPT LiveKit DataPackets.
  4. Broadcasts QUEST_STATUS updates back to Web Dashboard and Unity VR.
  5. Isolated per-job runtime state and robust error reporting via AGENT_INIT_FAILED.
"""

import asyncio
import json
import logging
import os
import random
from typing import Any, Dict, List, Optional

from dotenv import load_dotenv
from livekit import rtc
from livekit.agents import (
    AutoSubscribe,
    JobContext,
    JobProcess,
    WorkerOptions,
    cli,
    llm,
)
from livekit.agents.voice import Agent, AgentSession
from livekit.plugins import google, silero
from google.cloud import texttospeech as gcp_tts

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


def build_quest_instructions(quest_name: str, phrases: List[str]) -> str:
    """Build dynamic system prompt for the active quest received from Unity VR."""
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
# Keyed by phrase text -> list of rtc.AudioFrame.
from livekit.agents import tts as agents_tts
_tts_cache: Dict[str, List[rtc.AudioFrame]] = {}


async def _synthesize_phrases(
    tts: agents_tts.TTS,
    phrases: List[str],
) -> None:
    """Synthesize all phrases concurrently and store results in _tts_cache.
    Skips phrases that are already cached.
    """
    to_synthesize = [p for p in phrases if p not in _tts_cache]
    if not to_synthesize:
        return

    async def _synth_one(text: str) -> None:
        frames: List[rtc.AudioFrame] = []
        try:
            async with tts.synthesize(text) as stream:
                async for event in stream:
                    if event.frame:
                        frames.append(event.frame)
            _tts_cache[text] = frames
            logger.debug("[TTS CACHE] Cached %d frames for: %r", len(frames), text)
        except Exception as exc:
            logger.warning("[TTS CACHE] Failed to synthesize %r: %s", text, exc)

    await asyncio.gather(*[_synth_one(p) for p in to_synthesize])


async def _frames_to_async_gen(frames: List[rtc.AudioFrame]):
    """Wrap a cached list of AudioFrame into an AsyncIterable."""
    for frame in frames:
        yield frame


# ---------------------------------------------------------------------------
class QuestState:
    """Quản lý trạng thái bài học hiện tại. Một instance riêng cho mỗi job/room."""

    def __init__(self) -> None:
        self.active: bool = False
        self.name: str = ""
        self.phrases: List[str] = []

    def set_active_quest(self, name: str, phrases: List[str]) -> None:
        self.active = True
        self.name = name
        self.phrases = phrases

    def reset(self) -> None:
        self.active = False
        self.name = ""
        self.phrases = []


class JobRuntime:
    """Gói toàn bộ state thuộc về MỘT job/room cụ thể."""

    def __init__(self, job_ctx: JobContext) -> None:
        self.job_ctx = job_ctx
        self.quest_state = QuestState()
        self.background_tasks: set[asyncio.Task[Any]] = set()

    def spawn(self, coro: Any) -> None:
        task = asyncio.create_task(coro)
        self.background_tasks.add(task)
        task.add_done_callback(self.background_tasks.discard)


# ---------------------------------------------------------------------------
# Helper Utilities
# ---------------------------------------------------------------------------
async def send_rtc_event(
    runtime: JobRuntime, event_name: str, extra_data: Optional[Dict[str, Any]] = None
) -> bool:
    """Gửi gói tin DataPacket về Unity VR và Web Dashboard qua LiveKit RTC DataChannel."""
    if not runtime.job_ctx or not runtime.job_ctx.room:
        logger.warning("[RTC] Không thể gửi gói tin '%s': Chưa kết nối LiveKit Room.", event_name)
        return False

    try:
        payload_dict: Dict[str, Any] = {"event": event_name}
        if extra_data:
            payload_dict.update(extra_data)

        payload_bytes = json.dumps(payload_dict).encode("utf-8")
        await runtime.job_ctx.room.local_participant.publish_data(payload_bytes, reliable=True)
        logger.info("[RTC] Đã bắn sự kiện '%s' thành công qua DataChannel.", event_name)
        return True
    except Exception as err:
        logger.error("[RTC] Lỗi khi gửi dữ liệu '%s': %s", event_name, err)
        return False


async def clear_agent_chat_history(agent: Agent) -> None:
    """Xóa lịch sử hội thoại cũ qua agent.update_chat_ctx()."""
    try:
        empty_ctx = llm.ChatContext.empty()
        await agent.update_chat_ctx(empty_ctx)
        logger.info("[SESSION] Đã dọn dẹp lịch sử hội thoại cũ cho Quest mới.")
    except Exception as err:
        logger.error("[SESSION] Lỗi khi xóa lịch sử hội thoại: %s", err)


# ---------------------------------------------------------------------------
# Agent Tools
# ---------------------------------------------------------------------------
def make_complete_quest_tool(runtime: JobRuntime):
    """Factory tạo tool `complete_quest` gắn với đúng JobRuntime của phòng hiện tại."""

    @llm.function_tool(
        description=(
            "Gọi hàm này NGAY LẬP TỨC khi câu nói của trẻ thể hiện đúng ý định hoàn thành Quest hiện tại. "
            "Không cần câu trả lời hoàn hảo — nếu trẻ nói đúng ý cơ bản thì gọi ngay. "
            "Ví dụ: Quest 'Báo cáo đã rửa tay xong' -> trẻ nói 'con xong rồi', 'dạ xong rồi ạ' -> GỌI NGAY. "
            "Quest 'Xin chào' -> trẻ nói 'chào chú', 'xin chào', 'hi' -> GỌI NGAY."
        )
    )
    async def complete_quest() -> str:
        """Xác nhận trẻ hoàn thành Quest và gửi tín hiệu QUEST_MATCHED về VR và Web."""
        if not runtime.quest_state.active:
            logger.warning("[TOOL] complete_quest() được gọi nhưng chưa có Quest nào kích hoạt.")
            return "Hiện tại chưa có Quest nào được kích hoạt."

        quest_name = runtime.quest_state.name
        logger.info("[TOOL] 🎉 GEMINI XÁC NHẬN: Trẻ hoàn thành Quest '%s'!", quest_name)
        runtime.quest_state.reset()

        await send_rtc_event(runtime, "QUEST_MATCHED")
        await send_rtc_event(
            runtime, "QUEST_STATUS", {"quest_name": quest_name, "status": "matched"}
        )
        return f"Đã đánh dấu hoàn thành Quest '{quest_name}' thành công."

    return complete_quest


# ---------------------------------------------------------------------------
# Agent Definition
# ---------------------------------------------------------------------------
class TeacherAgent(Agent):
    """Giáo viên AI."""

    def __init__(self, runtime: JobRuntime) -> None:
        super().__init__(
            instructions=WAITING_INSTRUCTIONS,
            tools=[make_complete_quest_tool(runtime)],
        )

    async def on_enter(self) -> None:
        """Agent vào phòng - giữ im lặng chờ VoiceQuest từ VR kích hoạt."""
        logger.info("[AGENT] TeacherAgent đã sẵn sàng trong phòng (chế độ chờ VoiceQuest).")


# ---------------------------------------------------------------------------
# Prewarm: load heavy models once per worker process
# ---------------------------------------------------------------------------
def prewarm(proc: JobProcess) -> None:
    """Tải Silero VAD một lần khi worker process khởi động, dùng lại cho mọi job."""
    proc.userdata["vad"] = silero.VAD.load()


# ---------------------------------------------------------------------------
# Entrypoint & Packet Processing
# ---------------------------------------------------------------------------
async def entrypoint(ctx: JobContext) -> None:
    runtime = JobRuntime(ctx)

    # 1. Kết nối LiveKit Room
    await ctx.connect(auto_subscribe=AutoSubscribe.SUBSCRIBE_ALL)
    logger.info("[LIVEKIT] 🟢 Đã kết nối vào phòng: %s", ctx.room.name)

    # 2. Kiểm tra biến môi trường
    gemini_key = os.getenv("GOOGLE_API_KEY")
    gcloud_creds = os.getenv("GOOGLE_APPLICATION_CREDENTIALS")

    if not gemini_key:
        logger.error("[ENV] ❌ Thiếu GOOGLE_API_KEY trong file .env!")
        await send_rtc_event(runtime, "AGENT_INIT_FAILED", {"reason": "missing_gemini_key"})
        await ctx.room.disconnect()
        return

    if not gcloud_creds or not os.path.exists(gcloud_creds):
        logger.error(
            "[ENV] ❌ File Service Account JSON không tồn tại tại: %s. "
            "Vui lòng kiểm tra lại đường dẫn GOOGLE_APPLICATION_CREDENTIALS!",
            gcloud_creds,
        )
        await send_rtc_event(runtime, "AGENT_INIT_FAILED", {"reason": "missing_gcloud_credentials"})
        await ctx.room.disconnect()
        return

    # 3. Khởi tạo Pipeline Audio Session
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

    # 4. Bắt đầu phiên làm việc TRƯỚC khi lắng nghe DataPacket từ Unity VR / Web Dashboard
    logger.info("[AGENT] 🤖 Đang khởi động phiên làm việc...")
    await session.start(room=ctx.room, agent=agent)

    def on_data_received(data_packet: rtc.DataPacket) -> None:
        try:
            raw_text = data_packet.data.decode("utf-8")
            data = json.loads(raw_text)
            event_type = data.get("event")

            if event_type == "SET_ACTIVE_QUEST":
                quest_name = data.get("quest_name", "")
                phrases = data.get("default_phrases", [])

                runtime.quest_state.set_active_quest(quest_name, phrases)
                logger.info(
                    "[UNITY] 🎯 KÍCH HOẠT QUEST: '%s' | %d gợi ý: %s",
                    quest_name,
                    len(phrases),
                    phrases,
                )

                # Broadcast QUEST_STATUS sang Web Dashboard
                runtime.spawn(
                    send_rtc_event(
                        runtime,
                        "QUEST_STATUS",
                        {
                            "quest_name": quest_name,
                            "status": "active",
                            "phrases_cached": True,
                        },
                    )
                )

                # Chạy ngầm việc cập nhật ngữ cảnh và mở lời
                runtime.spawn(_handle_quest_activation(agent, session, quest_name, phrases))

            elif event_type in ("VERBAL_HINT", "ON_REMINDER"):
                logger.info("[DATA] 💡 Nhận sự kiện %s từ hệ thống", event_type)
                runtime.spawn(_handle_hint_reminder(session, runtime, event_type))

            elif event_type == "SPEAK_SCRIPT":
                text = data.get("text", "").strip()
                if text:
                    logger.info("[DATA] 💬 Nhận lệnh SPEAK_SCRIPT từ Web: %r", text)
                    runtime.spawn(_handle_speak_script(session, text))

        except Exception as err:
            logger.error("[DATA] Lỗi xử lý DataPacket: %s", err)

    ctx.room.on("data_received", on_data_received)
    logger.info("[AGENT] 🤖 Agent đứng chờ lệnh từ Unity VR và Web Dashboard...")


async def _handle_quest_activation(
    agent: Agent,
    session: AgentSession,
    quest_name: str,
    phrases: List[str],
) -> None:
    """Update context and speak the opening phrase with pre-synthesized audio."""
    try:
        opening = (
            random.choice(phrases)
            if phrases
            else f"Nào, chúng ta bắt đầu bài học '{quest_name}' nhé!"
        )

        new_instructions = build_quest_instructions(quest_name, phrases)
        await asyncio.gather(
            clear_agent_chat_history(agent),
            _synthesize_phrases(session.tts, phrases),
        )
        await agent.update_instructions(new_instructions)

        cached_frames = _tts_cache.get(opening)
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
        logger.error("[AGENT] Lỗi khi kích hoạt Quest context: %s", err)


async def _handle_hint_reminder(
    session: AgentSession,
    runtime: JobRuntime,
    event_name: str,
) -> None:
    """Play a random cached phrase for the active quest on VERBAL_HINT or ON_REMINDER."""
    try:
        if not runtime.quest_state.active or not runtime.quest_state.phrases:
            logger.warning("[HINT] %s received but no active quest or phrases.", event_name)
            return

        phrase = random.choice(runtime.quest_state.phrases)
        cached_frames = _tts_cache.get(phrase)
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
