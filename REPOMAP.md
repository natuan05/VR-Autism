# VR-Autism Repository Map

> **Multi-Platform Architecture**: Unity C# (`Assets/Project/Scripts`), Python LiveKit Voice Agent (`LiveKitAgent/src`), and Next.js Web Dashboard (`src`).

- **Graph Stats**: 859 symbols/nodes, 1506 relations/edges across 3 subsystems.
- **Subsystem Counts**: Unity: 820, Python: 24, Web: 0, Contracts: 15.

## Cross-Stack Communication Bridges

Cross-boundary coordination contracts linking Unity C# clients, Python Voice Agent, and Web Dashboard:

| Contract | Type | Publishers / Writers | Subscribers / Listeners | Connections |
| :--- | :--- | :--- | :--- | :---: |
| `QUEST_STATUS` | Livekit Event | `LiveKitService`, `OnDataReceived`, `TeacherAgent` | `LiveKitService`, `OnDataReceived`, `entrypoint` | 12 |
| `live_sessions` | Rtdb Path | `LiveSessionReporter`, `SendLiveSessionHandshake`, `UpdateCurrentActivity` | *None* | 7 |
| `QUEST_MATCHED` | Livekit Event | `LiveKitService`, `OnDataReceived`, `TeacherAgent` | `LiveKitService`, `OnDataReceived` | 6 |
| `SET_ACTIVE_QUEST` | Livekit Event | `entrypoint`, `on_data_received`, `_process_packet` | `entrypoint`, `on_data_received`, `_process_packet` | 6 |
| `VERBAL_HINT` | Livekit Event | `entrypoint`, `on_data_received`, `_process_packet` | `entrypoint`, `on_data_received`, `_process_packet` | 6 |
| `ON_REMINDER` | Livekit Event | `entrypoint`, `on_data_received`, `_process_packet` | `entrypoint`, `on_data_received`, `_process_packet` | 6 |
| `SPEAK_SCRIPT` | Livekit Event | `entrypoint`, `on_data_received`, `_process_packet` | `entrypoint`, `on_data_received`, `_process_packet` | 6 |
| `AGENT_INIT_FAILED` | Livekit Event | `LiveKitService`, `OnDataReceived`, `entrypoint` | `LiveKitService`, `OnDataReceived` | 5 |
| `pairing_codes` | Rtdb Path | `PairingManager`, `GenerateAndPushPIN`, `CleanupOnQuit` | *None* | 5 |
| `sessions` | Rtdb Path | `FirebaseManager`, `SaveSession`, `FirebasePaths` | *None* | 4 |
| `lessons` | Rtdb Path | `FirebasePaths`, `SceneMenuController`, `LoadRemoteLesson` | *None* | 3 |
| `behavior_snapshots` | Rtdb Path | `TelemetryUploader`, `PushAggregatedSnapshot`, `try` | *None* | 3 |
| `quest_list` | Rtdb Path | `FirebasePaths` | *None* | 1 |
| `skills` | Rtdb Path | `FirebasePaths` | *None* | 1 |
| `webrtc_signaling` | Rtdb Path | `FirebasePaths` | *None* | 1 |

## Core Architecture & Ranked Symbols

Top architectural hubs and high-centrality symbols ranked by PageRank importance:

### Unity C# Core Subsystem (`Assets/Project/Scripts`)

- **`RemoteCommandListener`** (class) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:9`] (PR: 0.0158) — *Lắng nghe lệnh điều khiển từ Web Dashboard. Sẽ lắng nghe nhánh RTDB: live_ses...*
- **`LogWarning`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:36`] (PR: 0.0148)
- **`Play`** (method) [`Assets/Project/Scripts/Core/Manager/AudioManager.cs:14`] (PR: 0.0091)
- **`LiveKitService`** (class) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:9`] (PR: 0.0082)
- **`LessonParameters`** (class) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:6`] (PR: 0.0075) — *Tham số cấu hình bài học, áp dụng cho một phiên trị liệu cụ thể. Được lưu tro...*
- **`IsAudioSourceValid`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:41`] (PR: 0.0074)
- **`Quest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:5`] (PR: 0.0069) — *Abstract base class for all Quest types. Data + minimal lifecycle hooks — sub...*
- **`RTDBConnection`** (class) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:6`] (PR: 0.0067) — *Singleton gốc — Sở hữu DatabaseReference duy nhất cho toàn bộ module RTDB. Cá...*
- **`SessionContext`** (class) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:6`] (PR: 0.0064) — *Dữ liệu phiên học, truyền qua các Scene thông qua DontDestroyOnLoad. Được set...*
- **`SetAction`** (method) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:15`] (PR: 0.0063)
- **`NPC`** (class) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:3`] (PR: 0.0055)
- **`SensorHarvester`** (class) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:8`] (PR: 0.0050) — *Thu thập dữ liệu cảm biến và hành vi của trẻ trong môi trường VR. Ghi mẫu liê...*
- **`TelemetryStreamer`** (class) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:8`] (PR: 0.0050) — *File điều phối vòng lặp thu thập dữ liệu (Tuyến giữa SensorHarvester và Telem...*
- **`QuizController`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:10`] (PR: 0.0049) — *Orchestrates quiz lesson flow. Subscribes to UIController events — has zero d...*
- **`SoundManager`** (class) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:5`] (PR: 0.0047)
- **`QuestController`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:9`] (PR: 0.0044) — *Pure Sequencer — chỉ lo điều phối thứ tự Quest và phát telemetry events. Khôn...*
- **`PairingManager`** (class) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:10`] (PR: 0.0043) — *Quản lý vòng đời kết nối Pairing giữa Kính VR và Web Dashboard. Thiết kế theo...*
- **`FirebaseManager`** (class) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:12`] (PR: 0.0043) — *Manages all Firebase read/write for the VR app. Architecture: - Cloud Firesto...*
- **`Clear`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:80`] (PR: 0.0041) — *Xoá toàn bộ context sau khi đã ghi log xong, tránh rác từ phiên cũ dính sang ...*
- **`PromptTeacher`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:112`] (PR: 0.0038)
- **`EventChannel`** (class) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:6`] (PR: 0.0038)
- **`SilenceCountdown`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:105`] (PR: 0.0037)
- **`Dz`** (class) [`Assets/Project/Scripts/Core/Observer/Dz.cs:12`] (PR: 0.0037)
- **`StopListening`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:107`] (PR: 0.0037)
- **`ResetSilenceTimer`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:89`] (PR: 0.0036)
- **`SendEvent`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:93`] (PR: 0.0036) — *Posts the event. This will notify all listener that register for this event E...*
- **`TimeManager`** (class) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:15`] (PR: 0.0036)
- **`OnEnd`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:47`] (PR: 0.0033) — *Subclass override để cleanup khi quest kết thúc.*
- **`TimeUtils`** (class) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:7`] (PR: 0.0032)
- **`SetState`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:69`] (PR: 0.0032)
- **`Start`** (method) [`Assets/Project/Scripts/Cloud/SessionSyncTracker.cs:9`] (PR: 0.0032)
- **`GetRandomAudio`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:152`] (PR: 0.0031)
- **`OnCommandChildAdded`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:121`] (PR: 0.0031)
- **`OnBegin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:10`] (PR: 0.0030)
- **`GetRoot`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:226`] (PR: 0.0030)
- **`StopStreaming`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:52`] (PR: 0.0029)
- **`Awake`** (method) [`Assets/Project/Scripts/Entities/NPC/NPC.cs:9`] (PR: 0.0029)
- **`GetDefault`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:119`] (PR: 0.0029) — *Tương thích ngược với code cũ đang gọi GetDefault(). Trả về singleton Default...*
- **`InitializeRootRef`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:32`] (PR: 0.0029)
- **`RaiseUIStarted`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:39`] (PR: 0.0028)
- **`Close`** (method) [`Assets/Project/Scripts/Entities/Door.cs:44`] (PR: 0.0028)
- **`FirebasePaths`** (class) [`Assets/Project/Scripts/Cloud/FirebasePaths.cs:2`] (PR: 0.0028)
- **`IsInFOV`** (method) [`Assets/Project/Scripts/Entities/FlockUnit.cs:231`] (PR: 0.0028)
- **`LiveSessionReporter`** (class) [`Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs:8`] (PR: 0.0027) — *Báo cáo trạng thái vòng đời phiên học lên nhánh live_sessions/ của RTDB. Life...*
- **`Assert`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:80`] (PR: 0.0025) — *Thown an exception if condition = false, show message on console's log*
- **`End`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:37`] (PR: 0.0025) — *Quest kết thúc — gọi OnEnd rồi xóa controller.*
- **`OnNextQuestionClicked`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:261`] (PR: 0.0025)
- **`WaitUntil`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:40`] (PR: 0.0025)
- **`SetOutline`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:112`] (PR: 0.0025)
- **`HideDown`** (method) [`Assets/Project/Scripts/Player/InteractController.cs:24`] (PR: 0.0025)
- **`SmoothLookAtPlayer`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCLookAtPlayer.cs:28`] (PR: 0.0024)
- **`OnBegin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:44`] (PR: 0.0023) — *Subclass override để xử lý khi quest bắt đầu.*
- **`WebRTCManager`** (class) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:7`] (PR: 0.0023) — *Quản lý toàn bộ vòng đời WebRTC stream POV. Chỉ cần gắn vào cùng GameObject v...*
- **`SendEvent`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:173`] (PR: 0.0023) — *Post event with no param (param = null)*
- **`Cleanup`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCSignaling.cs:106`] (PR: 0.0023)
- **`Open`** (method) [`Assets/Project/Scripts/Entities/Door.cs:37`] (PR: 0.0023)
- **`RaiseUIFinished`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:42`] (PR: 0.0023)
- **`GetPrompt`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:121`] (PR: 0.0023)
- **`Begin`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:30`] (PR: 0.0023) — *Quest được kích hoạt — lưu controller rồi gọi OnBegin cho subclass.*
- **`PresentQuestion`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:166`] (PR: 0.0022)
- **`ProcessCommand`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:165`] (PR: 0.0021)
- **`StopLoopingSound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:98`] (PR: 0.0021)
- **`StartStreaming`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:35`] (PR: 0.0021)
- **`Initialize`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:14`] (PR: 0.0021)
- **`TelemetryUploader`** (class) [`Assets/Project/Scripts/Cloud/RTDB/TelemetryUploader.cs:7`] (PR: 0.0021) — *Đẩy BehaviorSnapshot lên nhánh behavior_snapshots/ của RTDB. Đứng giữa Teleme...*
- **`IQuestHasVisual`** (interface) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:4`] (PR: 0.0020)
- **`GetFilePath`** (method) [`Assets/Project/Scripts/Core/DataLocal.cs:53`] (PR: 0.0020)
- **`PlaySound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:56`] (PR: 0.0020)
- **`CurrentSecond`** (property) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:9`] (PR: 0.0020)
- **`CurrentDay`** (property) [`Assets/Project/Scripts/Core/Utils/TimeUtils.cs:10`] (PR: 0.0020)
- **`BeginSession`** (method) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:61`] (PR: 0.0020) — *Call this at lesson start to initialise the in-memory session container.*
- **`HandleActivityChanged`** (method) [`Assets/Project/Scripts/Cloud/SessionSyncTracker.cs:20`] (PR: 0.0019)
- **`SubscribeListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:68`] (PR: 0.0019) — *Subscribe Listeners for EventID EventID that object want to listen Callback w...*
- **`Disconnect`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:111`] (PR: 0.0019)
- **`IsPlaying`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:114`] (PR: 0.0019)
- **`MarkQuestStart`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:102`] (PR: 0.0019) — *Call when a new quest begins to capture its start timestamp.*
- **`StreamRoutine`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:62`] (PR: 0.0019)
- **`Show`** (method) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:10`] (PR: 0.0019)
- **`TriggerVisualHint`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:234`] (PR: 0.0019)
- **`SetStretchAnchorToSide`** (method) [`Assets/Project/Scripts/Core/TransformLib.cs:30`] (PR: 0.0019)
- **`SetStretchAnchorToFarSide`** (method) [`Assets/Project/Scripts/Core/TransformLib.cs:40`] (PR: 0.0019)
- **`SetCurrentTarget`** (method) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:118`] (PR: 0.0019)
- **`SampleToBuffer`** (method) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:245`] (PR: 0.0019)
- **`StopStream`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:43`] (PR: 0.0018)
- **`GetCurQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:209`] (PR: 0.0018)
- **`LookAtPlayerForDuration`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCLookAtPlayer.cs:19`] (PR: 0.0018)
- **`FadeInAndPlay`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCVoice.cs:42`] (PR: 0.0018)
- **`CompleteActiveQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:135`] (PR: 0.0018)
- **`AnalyzeSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:44`] (PR: 0.0017)
- **`GetTotalElapsedSeconds`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:190`] (PR: 0.0017)
- **`Warning`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:56`] (PR: 0.0017)
- **`ClearAllListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:143`] (PR: 0.0017) — *Clears all the listener.*
- **`Tick`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:18`] (PR: 0.0017)
- **`Update`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:270`] (PR: 0.0017)
- **`TriggerVerbalHint`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:229`] (PR: 0.0017)
- **`TriggerSkipQuest`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:252`] (PR: 0.0017)
- **`TriggerPauseLesson`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:258`] (PR: 0.0017)
- **`TriggerResumeLesson`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:264`] (PR: 0.0017)
- **`ResetButtonState`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:134`] (PR: 0.0017)
- **`HideNextButton`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:109`] (PR: 0.0017)
- **`SubmitAnswer`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:233`] (PR: 0.0017)
- **`IQuestFlowController`** (interface) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/IQuestFlowController.cs:3`] (PR: 0.0017) — *Giao diện tối giản mà Quest subclass cần để điều phối luồng Quest. Tuân thủ I...*
- **`StopRecording`** (method) [`Assets/Project/Scripts/Core/Manager/RecordAudio.cs:33`] (PR: 0.0016)
- **`QuizQuestionData`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Models/QuizQuestionData.cs:1`] (PR: 0.0016) — *Pure C# data model for a single quiz question. Text and answer fields are JSO...*
- **`StopListeningAll`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:147`] (PR: 0.0016)
- **`ActionLoop`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:26`] (PR: 0.0016)
- **`SetToDefaultState`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:88`] (PR: 0.0016)
- **`SetProgress`** (method) [`Assets/Project/Scripts/Gameplay/Actions/UI/QuestProgressUI.cs:10`] (PR: 0.0016)
- **`HandleSetVolume`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:107`] (PR: 0.0016)
- **`UnsubscribeListener`** (method) [`Assets/Project/Scripts/Core/Observer/EventChannel.cs:121`] (PR: 0.0016) — *Unsubscribe the listener. EventID. Callback.*
- **`LogQuestComplete`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:122`] (PR: 0.0016) — *Call when a quest is completed. Builds a QuestLogData and hands it to Firebas...*
- **`Awake`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:18`] (PR: 0.0016)
- **`OnApplicationQuit`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:44`] (PR: 0.0016)
- **`Instance`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:14`] (PR: 0.0016)
- **`RootRef`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:15`] (PR: 0.0016)
- **`DeviceId`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RTDBConnection.cs:17`] (PR: 0.0016)
- **`StartListening`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:80`] (PR: 0.0016)
- **`SpeechRecognition`** (class) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:10`] (PR: 0.0015)
- **`EncodeAsWAV`** (method) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:156`] (PR: 0.0015)
- **`QuestionCollection`** (class) [`Assets/Project/Scripts/Gameplay/Quizzes/Models/QuestionCollection.cs:4`] (PR: 0.0015) — *Manages quiz question sequencing. Accepts QuizConfig (local SO) or a raw list...*
- **`BindAudioTrack`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:510`] (PR: 0.0015)
- **`QuestLogData`** (class) [`Assets/Project/Scripts/Cloud/Models/QuestLogData.cs:6`] (PR: 0.0015) — *Per-quest log entry. Mirrors the QUEST_LOGS schema in DATABASE_SCHEMA_DESIGN....*
- **`ActionManager`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/ActionManager.cs:11`] (PR: 0.0015)
- **`CleanupOnQuit`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:122`] (PR: 0.0015) — *Được gọi từ RTDBConnection.OnApplicationQuit để dọn dẹp PIN khi app thoát.*
- **`TriggerSetVolume`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:240`] (PR: 0.0015)
- **`TriggerPlayNpcScript`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:246`] (PR: 0.0015)
- **`TryGetSubDict`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:192`] (PR: 0.0015)
- **`GetBool`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:217`] (PR: 0.0015) — *Đọc bool từ dict với fallback snake_case / camelCase. Trả về defaultVal nếu k...*
- **`GetFloat`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:229`] (PR: 0.0015) — *Đọc float từ dict với fallback snake_case / camelCase. Trả về defaultVal (-1f...*
- **`BubblePosition`** (property) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:6`] (PR: 0.0015)
- **`ProgressBarPosition`** (property) [`Assets/Project/Scripts/Gameplay/Actions/Models/IQuestHasVisual.cs:7`] (PR: 0.0015)
- **`FlockUnit`** (class) [`Assets/Project/Scripts/Entities/FlockUnit.cs:4`] (PR: 0.0015)
- **`GeneratePath`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Components/ArrowPath.cs:25`] (PR: 0.0015)
- **`StartLessonTime`** (method) [`Assets/Project/Scripts/Core/Manager/TimeManager.cs:93`] (PR: 0.0015)
- **`Awake`** (method) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:22`] (PR: 0.0015)
- **`Instance`** (property) [`Assets/Project/Scripts/Core/Telemetry/TelemetryStreamer.cs:13`] (PR: 0.0015)
- **`LoadRemoteLesson`** (method) [`Assets/Project/Scripts/Gameplay/WaitingArea/SceneMenuController.cs:36`] (PR: 0.0014)
- **`UpdateCurrentActivity`** (method) [`Assets/Project/Scripts/Cloud/RTDB/LiveSessionReporter.cs:97`] (PR: 0.0014) — *Update hoạt động hiện tại để Web thay đổi các nút tương tác Hint Remote. Được...*
- **`BeginStream`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:65`] (PR: 0.0014)
- **`Connect`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:92`] (PR: 0.0014)
- **`OnDataReceived`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:365`] (PR: 0.0014)
- **`OnTrackSubscribed`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:493`] (PR: 0.0014)
- **`OnTrackUnsubscribed`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:528`] (PR: 0.0014)
- **`InteractController`** (class) [`Assets/Project/Scripts/Player/InteractController.cs:5`] (PR: 0.0014)
- **`HandleQuestionSounds`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/Controllers/QuizController.cs:269`] (PR: 0.0014)
- **`AccumulateQuestLog`** (method) [`Assets/Project/Scripts/Cloud/FirebaseManager.cs:83`] (PR: 0.0014) — *Accumulates a completed quest's data into RAM. Called by QuestController afte...*
- **`RaiseUIProgressChanged`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:41`] (PR: 0.0014)
- **`CheckFinish`** (method) [`Assets/Project/Scripts/Player/Player/SpeechResponser.cs:80`] (PR: 0.0014)
- **`RawSample`** (struct) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:45`] (PR: 0.0014)
- **`Log`** (method) [`Assets/Project/Scripts/Core/Observer/Dz.cs:24`] (PR: 0.0014)
- **`ToggleAnswerButtons`** (method) [`Assets/Project/Scripts/Gameplay/Quizzes/UI/QuizUIController.cs:128`] (PR: 0.0014)
- **`PlayRemoteText`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCController.cs:52`] (PR: 0.0014)
- **`DisablePOVCamera`** (method) [`Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs:260`] (PR: 0.0014)
- **`FindBestDirectionToAvoidObstacle`** (method) [`Assets/Project/Scripts/Entities/FlockUnit.cs:194`] (PR: 0.0014)
- **`Hide`** (method) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:40`] (PR: 0.0014)
- **`Save`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:22`] (PR: 0.0014)
- **`IceCandidateJson`** (class) [`Assets/Project/Scripts/Core/Telemetry/WebRTCStreamer.cs:245`] (PR: 0.0014)
- **`Init`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:27`] (PR: 0.0014) — *Khởi tạo ban đầu (gọi 1 lần trong Awake của QuestController).*
- **`AggregatedSnapshot`** (constructor) [`Assets/Project/Scripts/Core/Models/AggregatedSnapshot.cs:43`] (PR: 0.0014)
- **`HandlePinNodeChanged`** (method) [`Assets/Project/Scripts/Cloud/RTDB/PairingManager.cs:158`] (PR: 0.0014)
- **`WebRTCSignaling`** (class) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCSignaling.cs:9`] (PR: 0.0014)
- **`HandlePlayNpcScript`** (method) [`Assets/Project/Scripts/Entities/NPC/NPCRemoteBridge.cs:29`] (PR: 0.0013)
- **`Push`** (method) [`Assets/Project/Scripts/Entities/Objects/DispenserController.cs:8`] (PR: 0.0013)
- **`FromDictionary`** (method) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:125`] (PR: 0.0013) — *Tạo LessonParameters từ Firestore Dictionary (default_lesson_params). Hỗ trợ ...*
- **`SpeedControl`** (method) [`Assets/Project/Scripts/Player/Player/Player.cs:65`] (PR: 0.0013)
- **`PlayAudioClip`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:67`] (PR: 0.0013)
- **`VisualQuest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:11`] (PR: 0.0013) — *Quest có visual 3D, âm thanh, trigger vật lý, và UI events. Tự quản lý trigge...*
- **`MoveOnWaypoints`** (method) [`Assets/Project/Scripts/Entities/NPCInteraction.cs:37`] (PR: 0.0013)
- **`SpeechBubblePresenter`** (class) [`Assets/Project/Scripts/Entities/NPC/SpeechBubblePresenter.cs:4`] (PR: 0.0013)
- **`Awake`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:47`] (PR: 0.0013)
- **`Start`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:57`] (PR: 0.0013)
- **`OnDisable`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:70`] (PR: 0.0013)
- **`OnDestroy`** (method) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:75`] (PR: 0.0013)
- **`Instance`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:30`] (PR: 0.0013)
- **`else`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:65`] (PR: 0.0013)
- **`try`** (property) [`Assets/Project/Scripts/Cloud/RTDB/RemoteCommandListener.cs:285`] (PR: 0.0013)
- **`ListenEvents`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:16`] (PR: 0.0013)
- **`StopListeningEvents`** (method) [`Assets/Project/Scripts/Core/BaseMono.cs:17`] (PR: 0.0013)
- **`OnCharacterEnter`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:16`] (PR: 0.0013)
- **`OnCharacterExit`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/HoldTouchQuest.cs:23`] (PR: 0.0013)
- **`CheckSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:30`] (PR: 0.0013)
- **`GiveReminder`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:58`] (PR: 0.0013)
- **`PauseSound`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:79`] (PR: 0.0013)
- **`PlaySoundLoop`** (method) [`Assets/Project/Scripts/Core/Manager/SoundManager.cs:86`] (PR: 0.0013)
- **`AggregateAndFlush`** (method) [`Assets/Project/Scripts/Core/Telemetry/SensorHarvester.cs:139`] (PR: 0.0013) — *Được gọi mỗi 2 giây từ TelemetryStreamer. Tổng hợp toàn bộ mẫu trong bộ đệm t...*
- **`AnalyzeSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechRecognition.cs:143`] (PR: 0.0013)
- **`ToggleTheDoor`** (method) [`Assets/Project/Scripts/Entities/Door.cs:30`] (PR: 0.0013)
- **`HandleAllQuestsCompleted`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestUIController.cs:102`] (PR: 0.0012)
- **`Save`** (method) [`Assets/Project/Scripts/Core/DataLocal.cs:27`] (PR: 0.0012)
- **`CreateEmpty`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:11`] (PR: 0.0012)
- **`ConvertAndWrite`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:38`] (PR: 0.0012)
- **`WriteHeader`** (method) [`Assets/Project/Scripts/Core/Utils/WavUtils.cs:59`] (PR: 0.0012)
- **`PushAggregatedSnapshot`** (method) [`Assets/Project/Scripts/Cloud/RTDB/TelemetryUploader.cs:34`] (PR: 0.0012) — *Bắn một mẫu dữ liệu hành vi lên RTDB. Đường dẫn: behavior_snapshots/{sessionI...*
- **`ActivateQuest`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Controllers/QuestController.cs:103`] (PR: 0.0012)
- **`NPCController`** (class) [`Assets/Project/Scripts/Entities/NPC/NPCController.cs:4`] (PR: 0.0012)
- **`ProcessSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:40`] (PR: 0.0012)
- **`GetRecognizedSpeech`** (method) [`Assets/Project/Scripts/Player/Player/SpeechReminderSystem.cs:78`] (PR: 0.0012)
- **`EnsureComponents`** (method) [`Assets/Project/Scripts/Cloud/RTDB/WebRTCManager.cs:51`] (PR: 0.0012)
- **`VoiceQuest`** (class) [`Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs:6`] (PR: 0.0012)
- **`BlinkHintOutline`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/VisualQuest.cs:122`] (PR: 0.0012)
- **`PairingData`** (constructor) [`Assets/Project/Scripts/Cloud/Models/PairingData.cs:20`] (PR: 0.0012)
- **`GetPhrasesByQuestIndex`** (method) [`Assets/Project/Scripts/Core/Manager/SessionContext.cs:58`] (PR: 0.0012)
- **`OnVerbalHint`** (method) [`Assets/Project/Scripts/Gameplay/Actions/Models/Quest.cs:61`] (PR: 0.0012) — *Xử lý gợi ý lời nói — mỗi loại Quest tự quyết cách kích hoạt.*
- **`ActionParams`** (property) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:24`] (PR: 0.0012)
- **`QuizParams`** (property) [`Assets/Project/Scripts/Core/Models/LessonParameters.cs:65`] (PR: 0.0012)