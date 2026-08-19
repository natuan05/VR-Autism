# VR-Autism Repository Map

> **Multi-Platform Architecture**: Unity C# (`Assets/Project/Scripts`), Python LiveKit Voice Agent (`LiveKitAgent/src`), and Next.js Web Dashboard (`src`).

- **Graph Stats**: 2053 symbols/nodes, 2530 relations/edges across 4 subsystems.
- **Subsystem Counts**: Unity: 820, Python: 24, Web: 1192, Contracts: 17.

## Cross-Stack Communication Bridges

Cross-boundary coordination contracts linking Unity C# clients, Python Voice Agent, and Web Dashboard:

| Contract | Type | Publishers / Writers | Subscribers / Listeners | Connections |
| :--- | :--- | :--- | :--- | :---: |
| `sessions` | Rtdb Path | `FirebaseManager`, `SaveSession`, `FirebasePaths` | *None* | 111 |
| `lessons` | Rtdb Path | `FirebasePaths`, `SceneMenuController`, `LoadRemoteLesson` | *None* | 43 |
| `pairing_codes` | Rtdb Path | `PairingManager`, `GenerateAndPushPIN`, `CleanupOnQuit` | *None* | 21 |
| `QUEST_STATUS` | Livekit Event | `LiveKitService`, `OnDataReceived`, `make_complete_quest_tool` | `LiveKitService`, `OnDataReceived`, `entrypoint` | 18 |
| `live_sessions` | Rtdb Path | `LiveSessionReporter`, `SendLiveSessionHandshake`, `UpdateCurrentActivity` | *None* | 17 |
| `/api/livekit-token` | Api Route | `GET`, `LiveKitRoomProvider`, `fetchToken` | *None* | 13 |
| `webrtc_signaling` | Rtdb Path | `FirebasePaths`, `subscribeToWebRTCOffer`, `pushWebRTCAnswer` | *None* | 10 |
| `VERBAL_HINT` | Livekit Event | `entrypoint`, `on_data_received`, `useLiveKitDataChannel` | `entrypoint`, `on_data_received` | 9 |
| `QUEST_MATCHED` | Livekit Event | `LiveKitService`, `OnDataReceived`, `make_complete_quest_tool` | `LiveKitService`, `OnDataReceived` | 6 |
| `SPEAK_SCRIPT` | Livekit Event | `entrypoint`, `on_data_received`, `useLiveKitDataChannel` | `entrypoint`, `on_data_received` | 6 |
| `skills` | Rtdb Path | `FirebasePaths`, `callGemini`, `genAI` | *None* | 5 |
| `AGENT_INIT_FAILED` | Livekit Event | `LiveKitService`, `OnDataReceived`, `entrypoint` | `LiveKitService`, `OnDataReceived` | 5 |
| `behavior_snapshots` | Rtdb Path | `TelemetryUploader`, `PushAggregatedSnapshot`, `try` | *None* | 5 |
| `SET_ACTIVE_QUEST` | Livekit Event | `entrypoint`, `on_data_received` | `entrypoint`, `on_data_received` | 4 |
| `ON_REMINDER` | Livekit Event | `entrypoint`, `on_data_received` | `entrypoint`, `on_data_received` | 4 |
| `quest_list` | Rtdb Path | `FirebasePaths` | *None* | 1 |
| `/api/tts` | Api Route | `POST` | *None* | 1 |

## Core Architecture & Ranked Symbols

Top architectural hubs and high-centrality symbols ranked by PageRank importance:

### Unity C# Core Subsystem (`Assets/Project/Scripts`)

- **`RemoteCommandListener`** (class) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:9`] (PR: 0.0065) — *Lắng nghe lệnh điều khiển từ Web Dashboard. Sẽ lắng nghe nhánh RTDB: live_ses...*
- **`LogWarning`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:36`] (PR: 0.0062)
- **`Play`** (method) [`Assets/Project/Scripts/Core/Manager/AudioManager.cs:14`] (PR: 0.0040)
- **`LiveKitService`** (class) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:9`] (PR: 0.0034)
- **`IsAudioSourceValid`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:41`] (PR: 0.0033)
- **`LessonParameters`** (class) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:6`] (PR: 0.0032) — *Tham số cấu hình bài học, áp dụng cho một phiên trị liệu cụ thể. Được lưu tro...*
- **`SetAction`** (method) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:15`] (PR: 0.0030)
- **`Quest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:5`] (PR: 0.0028) — *Abstract base class for all Quest types. Data + minimal lifecycle hooks — sub...*
- **`SessionContext`** (class) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:6`] (PR: 0.0027) — *Dữ liệu phiên học, truyền qua các Scene thông qua DontDestroyOnLoad. Được set...*
- **`NPC`** (class) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:3`] (PR: 0.0026)
- **`RTDBConnection`** (class) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:6`] (PR: 0.0026) — *Singleton gốc — Sở hữu DatabaseReference duy nhất cho toàn bộ module RTDB. Cá...*
- **`QuizController`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:10`] (PR: 0.0021) — *Orchestrates quiz lesson flow. Subscribes to UIController events — has zero d...*
- **`SoundManager`** (class) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:5`] (PR: 0.0021)
- **`TelemetryStreamer`** (class) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:8`] (PR: 0.0021) — *File điều phối vòng lặp thu thập dữ liệu (Tuyến giữa SensorHarvester và Telem...*
- **`QuestController`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:9`] (PR: 0.0019) — *Pure Sequencer — chỉ lo điều phối thứ tự Quest và phát telemetry events. Khôn...*
- **`FirebaseManager`** (class) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:12`] (PR: 0.0018) — *Manages all Firebase read/write for the VR app. Architecture: - Cloud Firesto...*
- **`Clear`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:80`] (PR: 0.0018) — *Xoá toàn bộ context sau khi đã ghi log xong, tránh rác từ phiên cũ dính sang ...*
- **`SensorHarvester`** (class) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:8`] (PR: 0.0017) — *Thu thập dữ liệu cảm biến và hành vi của trẻ trong môi trường VR. Ghi mẫu liê...*
- **`PairingManager`** (class) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:10`] (PR: 0.0017) — *Quản lý vòng đời kết nối Pairing giữa Kính VR và Web Dashboard. Thiết kế theo...*
- **`EventChannel`** (class) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:6`] (PR: 0.0017)
- **`Dz`** (class) [`Assets/Project/Scripts/Core/Observer/Dz.cs:12`] (PR: 0.0016)
- **`SendEvent`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:93`] (PR: 0.0016) — *Posts the event. This will notify all listener that register for this event E...*
- **`TimeManager`** (class) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:15`] (PR: 0.0015)
- **`StopListening`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:107`] (PR: 0.0015)
- **`PromptTeacher`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:112`] (PR: 0.0015)
- **`OnEnd`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:47`] (PR: 0.0015) — *Subclass override để cleanup khi quest kết thúc.*
- **`SilenceCountdown`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:105`] (PR: 0.0014)
- **`TimeUtils`** (class) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:7`] (PR: 0.0014)
- **`Start`** (method) [`Assets/Project/Scripts/Cloud/SessionSyncTracker.cs:9`] (PR: 0.0014)
- **`SetState`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:69`] (PR: 0.0014)
- **`Awake`** (method) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:9`] (PR: 0.0014)
- **`ResetSilenceTimer`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:89`] (PR: 0.0013)
- **`OnBegin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:10`] (PR: 0.0013)
- **`GetRandomAudio`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:152`] (PR: 0.0013)
- **`StopStreaming`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:52`] (PR: 0.0013)
- **`GetDefault`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:119`] (PR: 0.0013) — *Tương thích ngược với code cũ đang gọi GetDefault(). Trả về singleton Default...*
- **`OnCommandChildAdded`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:121`] (PR: 0.0013)
- **`Close`** (method) [`Assets/Project/Scripts/Entities/Door.cs:44`] (PR: 0.0012)
- **`RaiseUIStarted`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:39`] (PR: 0.0012)
- **`IsInFOV`** (method) [`Assets/Project/Scripts/Entities/FlockUnit.cs:231`] (PR: 0.0012)
- **`InitializeRootRef`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:32`] (PR: 0.0012)
- **`End`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:37`] (PR: 0.0011) — *Quest kết thúc — gọi OnEnd rồi xóa controller.*
- **`SpeechRecognition`** (class) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:10`] (PR: 0.0011)
- **`Assert`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:80`] (PR: 0.0011) — *Thown an exception if condition = false, show message on console's log*
- **`OnNextQuestionClicked`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:261`] (PR: 0.0011)
- **`WaitUntil`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:40`] (PR: 0.0011)
- **`LiveSessionReporter`** (class) [`Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs:8`] (PR: 0.0011) — *Báo cáo trạng thái vòng đời phiên học lên nhánh live_sessions/ của RTDB. Life...*
- **`SetOutline`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:112`] (PR: 0.0011)
- **`HideDown`** (method) [`Assets/Project/Scripts/Player/InteractController.cs:24`] (PR: 0.0011)
- **`FirebasePaths`** (class) [`Assets/Project/Scripts/Cloud/FirebasePaths.cs:2`] (PR: 0.0011)
- **`GetRoot`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:226`] (PR: 0.0011)
- **`SmoothLookAtPlayer`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCLookAtPlayer.cs:28`] (PR: 0.0011)
- **`WebRTCManager`** (class) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:7`] (PR: 0.0010) — *Quản lý toàn bộ vòng đời WebRTC stream POV. Chỉ cần gắn vào cùng GameObject v...*
- **`SendEvent`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:173`] (PR: 0.0010) — *Post event with no param (param = null)*
- **`Open`** (method) [`Assets/Project/Scripts/Entities/Door.cs:37`] (PR: 0.0010)
- **`OnBegin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:44`] (PR: 0.0010) — *Subclass override để xử lý khi quest bắt đầu.*
- **`RaiseUIFinished`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:42`] (PR: 0.0010)
- **`Cleanup`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCSignaling.cs:106`] (PR: 0.0010)
- **`PresentQuestion`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:166`] (PR: 0.0010)
- **`Begin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:30`] (PR: 0.0010) — *Quest được kích hoạt — lưu controller rồi gọi OnBegin cho subclass.*
- **`StopLoopingSound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:98`] (PR: 0.0009)
- **`GetPrompt`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:121`] (PR: 0.0009)
- **`Initialize`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:14`] (PR: 0.0009)
- **`StartStreaming`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:35`] (PR: 0.0009)
- **`IQuestHasVisual`** (interface) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:4`] (PR: 0.0009)
- **`PlaySound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:56`] (PR: 0.0009)
- **`GetFilePath`** (method) [`Assets/Project/Scripts/Core/DataLocal.cs:53`] (PR: 0.0009)
- **`CurrentSecond`** (property) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:9`] (PR: 0.0009)
- **`CurrentDay`** (property) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:10`] (PR: 0.0009)
- **`HandleActivityChanged`** (method) [`Assets/Project/Scripts/Cloud/SessionSyncTracker.cs:20`] (PR: 0.0009)
- **`SubscribeListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:68`] (PR: 0.0008) — *Subscribe Listeners for EventID EventID that object want to listen Callback w...*
- **`IsPlaying`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:114`] (PR: 0.0008)
- **`Show`** (method) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:10`] (PR: 0.0008)
- **`MarkQuestStart`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:102`] (PR: 0.0008) — *Call when a new quest begins to capture its start timestamp.*
- **`TelemetryUploader`** (class) [`Assets/Project/Scripts/Cloud/RTDB/TelemetryUploader.cs:7`] (PR: 0.0008) — *Đẩy BehaviorSnapshot lên nhánh behavior_snapshots/ của RTDB. Đứng giữa Teleme...*
- **`AnalyzeSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:44`] (PR: 0.0008)
- **`SetStretchAnchorToSide`** (method) [`Assets/Project/Scripts/Core/TransformLib.cs:30`] (PR: 0.0008)
- **`SetStretchAnchorToFarSide`** (method) [`Assets/Project/Scripts/Core/TransformLib.cs:40`] (PR: 0.0008)
- **`BeginSession`** (method) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:61`] (PR: 0.0008) — *Call this at lesson start to initialise the in-memory session container.*
- **`ProcessCommand`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:165`] (PR: 0.0008)
- **`Disconnect`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:111`] (PR: 0.0008)
- **`StopStream`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:43`] (PR: 0.0008)
- **`GetCurQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:209`] (PR: 0.0008)
- **`LookAtPlayerForDuration`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCLookAtPlayer.cs:19`] (PR: 0.0008)
- **`FadeInAndPlay`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCVoice.cs:42`] (PR: 0.0008)
- **`TriggerVisualHint`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:234`] (PR: 0.0008)
- **`CompleteActiveQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:135`] (PR: 0.0008)
- **`Warning`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:56`] (PR: 0.0008)
- **`ClearAllListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:143`] (PR: 0.0008) — *Clears all the listener.*
- **`Tick`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:18`] (PR: 0.0008)
- **`GetTotalElapsedSeconds`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:190`] (PR: 0.0008)
- **`SetCurrentTarget`** (method) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:118`] (PR: 0.0008)
- **`SampleToBuffer`** (method) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:245`] (PR: 0.0008)
- **`StreamRoutine`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:62`] (PR: 0.0007)
- **`ResetButtonState`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:134`] (PR: 0.0007)
- **`HideNextButton`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:109`] (PR: 0.0007)
- **`EncodeAsWAV`** (method) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:156`] (PR: 0.0007)
- **`SubmitAnswer`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:233`] (PR: 0.0007)
- **`Update`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:270`] (PR: 0.0007)
- **`StopRecording`** (method) [`Assets/Project/Scripts/Core/Manager/RecordAudio.cs:33`] (PR: 0.0007)
- **`IQuestFlowController`** (interface) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/IQuestFlowController.cs:3`] (PR: 0.0007) — *Giao diện tối giản mà Quest subclass cần để điều phối luồng Quest. Tuân thủ I...*
- **`ActionLoop`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:26`] (PR: 0.0007)
- **`TriggerVerbalHint`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:229`] (PR: 0.0007)
- **`TriggerSkipQuest`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:252`] (PR: 0.0007)
- **`TriggerPauseLesson`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:258`] (PR: 0.0007)
- **`TriggerResumeLesson`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:264`] (PR: 0.0007)
- **`SetToDefaultState`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:88`] (PR: 0.0007)
- **`SetProgress`** (method) [`Assets/Project/Scripts/Gameplay/Actions/UI/QuestProgressUI.cs:10`] (PR: 0.0007)
- **`HandleSetVolume`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:107`] (PR: 0.0007)
- **`UnsubscribeListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:121`] (PR: 0.0007) — *Unsubscribe the listener. EventID. Callback.*
- **`LogQuestComplete`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:122`] (PR: 0.0007) — *Call when a quest is completed. Builds a QuestLogData and hands it to Firebas...*
- **`StopListeningAll`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:147`] (PR: 0.0007)
- **`QuestionCollection`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Models/QuestionCollection.cs:4`] (PR: 0.0007) — *Manages quiz question sequencing. Accepts QuizConfig (local SO) or a raw list...*
- **`StartListening`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:80`] (PR: 0.0007)
- **`BubblePosition`** (property) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:6`] (PR: 0.0007)
- **`ProgressBarPosition`** (property) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:7`] (PR: 0.0007)
- **`ActionManager`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:11`] (PR: 0.0007)
- **`FlockUnit`** (class) [`Assets/Project/Scripts/Entities/FlockUnit.cs:4`] (PR: 0.0006)
- **`GeneratePath`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Components/ArrowPath.cs:25`] (PR: 0.0006)
- **`BindAudioTrack`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:510`] (PR: 0.0006)
- **`StartLessonTime`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:93`] (PR: 0.0006)
- **`Awake`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:18`] (PR: 0.0006)
- **`OnApplicationQuit`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:44`] (PR: 0.0006)
- **`Instance`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:14`] (PR: 0.0006)
- **`RootRef`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:15`] (PR: 0.0006)
- **`DeviceId`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:17`] (PR: 0.0006)
- **`BeginStream`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:65`] (PR: 0.0006)
- **`LoadRemoteLesson`** (method) [`Assets/Project/Scripts/Gameplay/WaitingArea/SceneMenuController.cs:36`] (PR: 0.0006)
- **`TryGetSubDict`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:192`] (PR: 0.0006)
- **`GetBool`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:217`] (PR: 0.0006) — *Đọc bool từ dict với fallback snake_case / camelCase. Trả về defaultVal nếu k...*
- **`GetFloat`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:229`] (PR: 0.0006) — *Đọc float từ dict với fallback snake_case / camelCase. Trả về defaultVal (-1f...*
- **`UpdateCurrentActivity`** (method) [`Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs:97`] (PR: 0.0006) — *Update hoạt động hiện tại để Web thay đổi các nút tương tác Hint Remote. Được...*
- **`AnalyzeSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:143`] (PR: 0.0006)
- **`InteractController`** (class) [`Assets/Project/Scripts/Player/InteractController.cs:5`] (PR: 0.0006)
- **`HandleQuestionSounds`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:269`] (PR: 0.0006)
- **`RaiseUIProgressChanged`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:41`] (PR: 0.0006)
- **`CleanupOnQuit`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:122`] (PR: 0.0006) — *Được gọi từ RTDBConnection.OnApplicationQuit để dọn dẹp PIN khi app thoát.*
- **`Awake`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:22`] (PR: 0.0006)
- **`Instance`** (property) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:13`] (PR: 0.0006)
- **`TriggerSetVolume`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:240`] (PR: 0.0006)
- **`TriggerPlayNpcScript`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:246`] (PR: 0.0006)
- **`PlayRemoteText`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCController.cs:52`] (PR: 0.0006)
- **`ToggleAnswerButtons`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:128`] (PR: 0.0006)
- **`Log`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:24`] (PR: 0.0006)
- **`FindBestDirectionToAvoidObstacle`** (method) [`Assets/Project/Scripts/Entities/FlockUnit.cs:194`] (PR: 0.0006)
- **`Connect`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:92`] (PR: 0.0006)
- **`Hide`** (method) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:40`] (PR: 0.0006)
- **`Save`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:22`] (PR: 0.0006)
- **`IceCandidateJson`** (class) [`Assets/Project/Scripts/Core/Telemetry/WebRTCStreamer.cs:245`] (PR: 0.0006)
- **`AccumulateQuestLog`** (method) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:83`] (PR: 0.0006) — *Accumulates a completed quest's data into RAM. Called by QuestController afte...*
- **`AggregatedSnapshot`** (constructor) [`Assets/Project/Scripts/Core/Models/AggregatedSnapshot.cs:43`] (PR: 0.0006)
- **`HandlePlayNpcScript`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCRemoteBridge.cs:29`] (PR: 0.0006)
- **`QuestLogData`** (class) [`Assets/Project/Scripts/Cloud/Models/QuestLogData.cs:6`] (PR: 0.0006) — *Per-quest log entry. Mirrors the QUEST_LOGS schema in DATABASE_SCHEMA_DESIGN....*
- **`SpeedControl`** (method) [`Assets/Project/Scripts/Player/Player/Player.cs:65`] (PR: 0.0006)
- **`SendRecordingCoroutine`** (method) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:102`] (PR: 0.0006)
- **`PlayAudioClip`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:67`] (PR: 0.0006)
- **`VisualQuest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:11`] (PR: 0.0006) — *Quest có visual 3D, âm thanh, trigger vật lý, và UI events. Tự quản lý trigge...*
- **`MoveOnWaypoints`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:37`] (PR: 0.0006)
- **`SpeechBubblePresenter`** (class) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:4`] (PR: 0.0006)
- **`Push`** (method) [`Assets/Project/Scripts/Entities/Objects/DispenserController.cs:8`] (PR: 0.0006)
- **`OnDataReceived`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:365`] (PR: 0.0006)
- **`OnTrackSubscribed`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:493`] (PR: 0.0006)
- **`OnTrackUnsubscribed`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:528`] (PR: 0.0006)
- **`DisablePOVCamera`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:260`] (PR: 0.0006)
- **`ListenEvents`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:16`] (PR: 0.0006)
- **`StopListeningEvents`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:17`] (PR: 0.0006)
- **`OnCharacterEnter`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:16`] (PR: 0.0006)
- **`OnCharacterExit`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:23`] (PR: 0.0006)
- **`CheckSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:30`] (PR: 0.0006)
- **`GiveReminder`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:58`] (PR: 0.0006)
- **`FromDictionary`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:125`] (PR: 0.0006) — *Tạo LessonParameters từ Firestore Dictionary (default_lesson_params). Hỗ trợ ...*
- **`PauseSound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:79`] (PR: 0.0006)
- **`PlaySoundLoop`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:86`] (PR: 0.0006)
- **`QuizQuestionData`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Models/QuizQuestionData.cs:1`] (PR: 0.0006) — *Pure C# data model for a single quiz question. Text and answer fields are JSO...*
- **`ToggleTheDoor`** (method) [`Assets/Project/Scripts/Entities/Door.cs:30`] (PR: 0.0006)
- **`HandleAllQuestsCompleted`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestUIController.cs:102`] (PR: 0.0006)
- **`Save`** (method) [`Assets/Project/Scripts/Core/DataLocal.cs:27`] (PR: 0.0005)
- **`CreateEmpty`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:11`] (PR: 0.0005)
- **`ConvertAndWrite`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:38`] (PR: 0.0005)
- **`WriteHeader`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:59`] (PR: 0.0005)
- **`ProcessSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:40`] (PR: 0.0005)
- **`GetRecognizedSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:78`] (PR: 0.0005)
- **`Awake`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:47`] (PR: 0.0005)
- **`Start`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:57`] (PR: 0.0005)
- **`OnDisable`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:70`] (PR: 0.0005)
- **`OnDestroy`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:75`] (PR: 0.0005)
- **`Instance`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:30`] (PR: 0.0005)
- **`else`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:65`] (PR: 0.0005)
- **`try`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:285`] (PR: 0.0005)
- **`NPCController`** (class) [`Assets/Project/Scripts/Entities/NPC/NPCController.cs:4`] (PR: 0.0005)
- **`VoiceQuest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs:6`] (PR: 0.0005)
- **`EnsureComponents`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:51`] (PR: 0.0005)
- **`Init`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:27`] (PR: 0.0005) — *Khởi tạo ban đầu (gọi 1 lần trong Awake của QuestController).*
- **`ActivateQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:103`] (PR: 0.0005)
- **`BlinkHintOutline`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:122`] (PR: 0.0005)
- **`HandlePinNodeChanged`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:158`] (PR: 0.0005)
- **`PairingData`** (constructor) [`Assets/Project/Scripts/Cloud/Models/PairingData.cs:20`] (PR: 0.0005)
- **`GetPhrasesByQuestIndex`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:58`] (PR: 0.0005)
- **`WebRTCSignaling`** (class) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCSignaling.cs:9`] (PR: 0.0005)
- **`PlayHintSound`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:133`] (PR: 0.0005)
- **`BlinkRoutine`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:145`] (PR: 0.0005)
- **`PushAggregatedSnapshot`** (method) [`Assets/Project/Scripts/Cloud/RTDB/TelemetryUploader.cs:34`] (PR: 0.0005) — *Bắn một mẫu dữ liệu hành vi lên RTDB. Đường dẫn: behavior_snapshots/{sessionI...*
- **`OnVerbalHint`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:61`] (PR: 0.0005) — *Xử lý gợi ý lời nói — mỗi loại Quest tự quyết cách kích hoạt.*
- **`ActionParams`** (property) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:24`] (PR: 0.0005)