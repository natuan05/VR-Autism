"""
Test TTS độc lập -- khong can LiveKit, khong can Unity.
Chay: python test_tts.py
Sau do mo file output_tts.wav de nghe kiem tra chat luong am thanh.
"""

import asyncio
import struct
import wave
import os
from dotenv import load_dotenv

load_dotenv()

# coding: utf-8
TEST_TEXT = "Xin ch\u00e0o! Con \u0111\u00e3 r\u1eeda tay xong ch\u01b0a? H\u00e3y n\u00f3i v\u1edbi c\u00f4 nh\u00e9!"
OUTPUT_FILE = "output_tts.wav"
SAMPLE_RATE = 24000
NUM_CHANNELS = 1


def pcm_to_wav(pcm_bytes: bytes, output_path: str, sample_rate: int, num_channels: int) -> None:
    """Đóng gói raw PCM (S16 LE) thành file WAV để nghe."""
    with wave.open(output_path, "wb") as wf:
        wf.setnchannels(num_channels)
        wf.setsampwidth(2)  # 16-bit = 2 bytes
        wf.setframerate(sample_rate)
        wf.writeframes(pcm_bytes)


async def test_tts() -> None:
    from livekit.plugins import google

    print("[TTS TEST] Dang khoi tao Google TTS (voice: vi-VN-Chirp3-HD-Aoede)...")

    from google.cloud import texttospeech

    tts = google.TTS(
        language="vi-VN",
        voice_name="vi-VN-Chirp3-HD-Aoede",
        # OGG_OPUS: Chirp 3 HD ho tro, la container day du, LiveKit framework decode dung
        # LINEAR16 tra ve raw PCM khong co WAV header -> mime_type="audio/wav" -> decode sai -> static
        audio_encoding=texttospeech.AudioEncoding.OGG_OPUS,
        sample_rate=SAMPLE_RATE,
        use_streaming=False,
    )

    print(f"[TTS TEST] Dang tong hop giong noi cho: \"{TEST_TEXT}\"")

    all_audio_bytes = bytearray()

    try:
        async with tts.synthesize(TEST_TEXT) as stream:
            async for audio_event in stream:
                # audio_event.frame chứa raw audio data
                frame = audio_event.frame
                if frame and frame.data:
                    all_audio_bytes.extend(bytes(frame.data))

        if not all_audio_bytes:
            print("[TTS TEST] THAT BAI: Khong nhan duoc du lieu audio nao tu API!")
            return

        # OGG_OPUS: giai ma bang ffmpeg/pydub roi ghi ra WAV de nghe
        import subprocess, shutil
        if shutil.which("ffmpeg"):
            subprocess.run(
                ["ffmpeg", "-y", "-i", "output_raw.ogg", OUTPUT_FILE],
                input=bytes(all_audio_bytes), capture_output=True,
            )
        else:
            # Fallback: ghi thang OGG, doi ten extension
            with open("output_tts.ogg", "wb") as f:
                f.write(bytes(all_audio_bytes))
            OUTPUT_FILE_DISPLAY = "output_tts.ogg (mo bang VLC neu khong co ffmpeg)"
            print(f"  - Da luu OGG vao: {os.path.abspath('output_tts.ogg')}")
            # Wrap PCM thu cong cho truong hop khong co ffmpeg
            # OGG bytes khong phai raw PCM nen cach nay chi de test
            OUTPUT_FILE = "output_tts.ogg"

        duration_sec = len(all_audio_bytes) / (SAMPLE_RATE * NUM_CHANNELS * 2)
        print("[TTS TEST] THANH CONG!")
        print(f"  - Du lieu nhan duoc : {len(all_audio_bytes):,} bytes")
        print(f"  - Thoi luong uoc tinh: {duration_sec:.2f} giay")
        print(f"  - Da luu vao        : {os.path.abspath(OUTPUT_FILE)}")
        print(f"\nMo file '{OUTPUT_FILE}' de nghe kiem tra chat luong am thanh.")

    except Exception as e:
        print(f"[TTS TEST] LOI: {type(e).__name__}: {e}")
        raise


if __name__ == "__main__":
    asyncio.run(test_tts())
