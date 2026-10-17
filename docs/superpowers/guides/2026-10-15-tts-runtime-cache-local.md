# Test the runtime TTS cache on one computer

This setup runs the Next.js cache API, Python V2 agent and Unity Editor on the same computer. Cache metadata still uses the existing Firebase configuration; audio still lives in Firebase Storage and downloads through HTTPS signed URLs. This is not an offline or Firebase emulator setup.

## 1. Start the web backend

Keep the existing local Firebase Admin, Storage bucket, LiveKit and authentication configuration. Configure the existing `TTS_CACHE_SERVICE_SECRET` with the same secret used by the Python agent. Do not disable session authorization.

```powershell
Set-Location D:/Lab/VRA-web
npm run dev -- --hostname 127.0.0.1 --port 3000
```

Use development mode. `npm run start` is a production-mode server; the publication route requires HTTPS in production even on the same computer.

`TTS_CACHE_BACKEND_URL` refers to this Next.js API origin, not a Firebase Storage URL.

## 2. Start the Python agent

In a separate terminal, keep the existing LiveKit/provider credentials and configure the shared service secret. Enable the local exception explicitly:

```powershell
Set-Location D:/Lab/VR-Autism/LiveKitAgent
$env:TTS_CACHE_BACKEND_URL = 'http://127.0.0.1:3000'
$env:TTS_CACHE_ALLOW_LOCAL_HTTP = 'true'
uv run src/agent_v2.py start
```

Without `TTS_CACHE_ALLOW_LOCAL_HTTP=true`, the agent requires HTTPS. The exception accepts only the exact hosts `localhost` and `127.0.0.1`, with an optional valid port. It rejects LAN/public addresses, alternate numeric loopback forms, IPv6, credentials, queries and fragments in the configured backend URL. Local requests bypass environment proxies; authenticated backend requests do not follow redirects. The service secret remains mandatory.

## 3. Enable the Unity Editor override

Leave the production HTTPS URL serialized on the Waiting Area `SceneMenuController` unchanged. In Unity, choose:

**Tools > TTS Cache > Use Local Backend (127.0.0.1:3000)**

The setting is stored only in `EditorPrefs`. The effective URL is selected once when the next session launches and retained for both snapshot loading and subsequent runtime cache requests. Changing the menu during an active session takes effect on the next launch.

To use the configured production backend again, choose:

**Tools > TTS Cache > Use Configured Backend**

The loopback HTTP JSON transport exists only in Editor compilation and uses `HttpClient`, so it does not need a project-wide insecure HTTP setting. Do not change **Allow downloads over HTTP**. HTTPS backend requests and signed Storage audio reads continue through the existing Unity transport. Storage URLs must remain HTTPS; bearer tokens are never sent to Storage. Player builds exclude the override/local transport and reject HTTP or loopback backend URLs at runtime.

## 4. Check the runtime cache

Launch a cache-aware V2 session through the existing teacher/device pairing flow. A cache miss should use the normal LiveKit stream and publish that same completed audio in the background. Replay after publication or in a new session: a valid hit should use cached audio. Verify that enabling this setup does not change profile keys, cancellation, reminders or quest completion.

`TTS_PREVIEW_SERVICE_URL` and the separate roomless Python preview process are needed only for teacher phrase preview. They are not required for runtime cache testing; no preview service setup is part of this guide.

`127.0.0.1` works only when the agent and Unity Editor run directly on the same computer. A physical headset, container, VM or remote worker has its own loopback address. Physical-headset testing requires a separately reachable HTTPS backend and is outside this local setup.

## Focused verification

Python: `tests/test_tts_cache_local_http_v1.py` and `tests/test_tts_cache_client_v1.py`.

Unity: compile first, then run EditMode filter `TtsCacheLocalDevelopmentV1Tests`. Also run `TtsUnityCacheIntegrationV1Tests` for the existing backend/Storage boundaries. The agent does not launch Unity or run headset checks.

Implementation verification on 2026-10-15: 136 focused Python tests passed; scoped Ruff check/format passed. Isolated C# 9 type checks of the actual backend client, URL policy, menu and local Editor fixture passed against the installed Unity assemblies. Actual-source policy/bearer probes passed 33 checks with Editor compilation and 34 with Player compilation. The 32-file syntax scan passed. These are non-Unity checks; full Unity compilation, EditMode execution and actual Editor-to-Next.js requests remain user verification.

Files in this follow-up (relative to `D:/Lab/VR-Autism`):

```text
LiveKitAgent/src/tts_cache_client_v1.py
LiveKitAgent/tests/test_tts_cache_client_v1.py
LiveKitAgent/tests/test_tts_cache_local_http_v1.py
Assets/Project/Scripts/Gameplay/WaitingArea/SceneMenuController.cs
Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/TtsCacheBackendClientV1.cs
Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/TtsCacheBackendUrlPolicyV1.cs
Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Editor/TtsCacheBackendUrlOverrideMenuV1.cs
Assets/Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Tests/Editor/TtsCacheLocalDevelopmentV1Tests.cs
```

New Unity files have accompanying metadata, including the new `Editor` folder. Documentation changes are this guide, design Section 15, and links in the implementation plan/handoff. Scenes, ProjectSettings and web product code were not changed. The focused independent review is complete; its empty-port finding was reproduced, corrected and rechecked. No commit, push or deployment was performed.
