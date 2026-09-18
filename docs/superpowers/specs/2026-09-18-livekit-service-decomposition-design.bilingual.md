# LiveKitService Decomposition Design / Thiết Kế Phân Rã LiveKitService

**Date / Ngày:** 2026-09-18  
**Status / Trạng thái:** Approved design; implementation not started / Thiết kế đã được phê duyệt; chưa bắt đầu triển khai  
**Scope / Phạm vi:** Unity VR client under / Unity VR client tại `Assets/Project/Scripts/Cloud/LiveKit/`

---

## 1. Objective / Mục Tiêu

**EN:** Refactor `LiveKitService` from a god object into cohesive services with single responsibilities while preserving all observable system behavior and existing integration contracts.  
> **VI:** Tái cấu trúc (refactor) `LiveKitService` từ một "god object" thành các service gắn kết với trách nhiệm đơn lẻ (single responsibility), đồng thời bảo toàn toàn bộ hành vi quan sát được của hệ thống và các hợp đồng tích hợp hiện hữu.

The refactor must:  
> Việc tái cấu trúc bắt buộc phải:

- preserve the current `LiveKitService` singleton façade and its public API;  
  > bảo tồn singleton façade hiện tại của `LiveKitService` và toàn bộ public API của nó;
- preserve Inspector configuration, scenes, prefabs, DataPacket topics, payloads, reliability, and event semantics;  
  > bảo tồn cấu hình Inspector, các scene, prefab, DataPacket topic, payload, độ tin cậy (reliability) và ngữ nghĩa sự kiện;
- maintain exclusive microphone ownership within the LiveKit subsystem;  
  > duy trì quyền sở hữu độc quyền đối với microphone trong hệ thống con LiveKit;
- prevent stale callbacks, overlapping asynchronous operations, and client-connection generations from racing;  
  > ngăn chặn race condition từ các callback cũ (stale callbacks), các tác vụ bất đồng bộ chồng chéo và giữa các thế hệ kết nối (connection generations);
- keep all Unity object operations on the Unity main thread;  
  > giữ tất cả thao tác với đối tượng Unity trên main thread của Unity;
- permit incremental migration with verification after every extraction.  
  > cho phép di chuyển từng bước (incremental migration) có xác minh sau mỗi lần bóc tách module.

**EN:** The Unity client does not provision a LiveKit room on the server. It creates a client-side `LiveKit.Room` SDK object and joins the room identified by the server-issued token. This document therefore uses the term **room connection**, not room creation.  
> **VI:** Kính Unity client không tự cấp phát phòng LiveKit trên server. Nó chỉ tạo đối tượng SDK `LiveKit.Room` ở phía client và tham gia vào phòng được chỉ định bởi token do server cấp. Do đó, tài liệu này sử dụng thuật ngữ **kết nối phòng (room connection)**, chứ không dùng thuật ngữ tạo phòng (room creation).

---

## 2. Current Context and Risk / Ngữ Cảnh Hiện Tại và Rủi Ro

`LiveKitService` currently combines:  
> `LiveKitService` hiện đang gộp chung:

- singleton and Unity lifecycle management;  
  > quản lý singleton và vòng đời Unity;
- LiveKit client connection lifecycle;  
  > vòng đời kết nối client LiveKit;
- DataPacket transport and legacy packet parsing;  
  > truyền tải DataPacket và phân tích cú pháp gói tin legacy cũ;
- microphone capture and publication;  
  > thu âm và phát luồng microphone;
- POV camera capture and video publication;  
  > quay camera POV và phát luồng video;
- legacy remote audio playback;  
  > phát âm thanh từ xa theo cơ chế cũ (legacy);
- V2 NPC audio routing, pending-track management, and stream rebinding;  
  > định tuyến âm thanh NPC V2, quản lý track chờ (pending tracks) và rebind luồng âm thanh;
- compatibility APIs and public gameplay events.  
  > các API tương thích ngược và các sự kiện gameplay công khai.

**EN:** GitNexus reports a **HIGH** upstream blast radius: at least eight direct consumers across LiveKit, voice, gameplay, RTDB, and waiting-area code. This is a lower bound because some consumers bind through interfaces. The working tree also contains uncommitted V2 audio-routing work that forms part of the behavioral baseline and must not be overwritten.  
> **VI:** GitNexus báo cáo bán kính ảnh hưởng (blast radius) ngược dòng mức **CAO (HIGH)**: ít nhất 8 bên tiêu thụ trực tiếp trải khắp các module LiveKit, voice, gameplay, RTDB và khu vực sảnh chờ (waiting area). Đây mới chỉ là cận dưới vì một số bên liên kết qua interface. Working tree hiện cũng chứa code định tuyến âm thanh V2 chưa commit; phần code này là một phần của baseline hành vi và tuyệt đối không được ghi đè làm mất.

---

## 3. Compatibility Contract / Hợp Đồng Tương Thích

**EN:** The first refactor phase is zero-touch for existing consumers.  
> **VI:** Giai đoạn tái cấu trúc đầu tiên đảm bảo không cần chạm vào (zero-touch) bất kỳ caller/consumer hiện có nào.

The following remain unchanged:  
> Những thành phần sau đây giữ nguyên không đổi:

- `LiveKitService.Instance` and singleton creation behavior;  
  > `LiveKitService.Instance` và hành vi khởi tạo singleton;
- `LiveKitService` as the only required scene component;  
  > `LiveKitService` là component duy nhất bắt buộc phải có trong scene;
- `ILiveKitRoomClient`, `ILiveKitDataPacketClientV2`, and `INpcAudioRouterV2`;  
  > các interface `ILiveKitRoomClient`, `ILiveKitDataPacketClientV2`, và `INpcAudioRouterV2`;
- all existing public methods, properties, and events;  
  > toàn bộ public method, property và event hiện có;
- all serialized fields, default values, and Inspector workflow;  
  > tất cả serialized field, giá trị mặc định và quy trình làm việc trên Inspector;
- test seams such as `StreamFactory`, route counters, and track simulation helpers;  
  > các điểm móc kiểm thử (test seams) như `StreamFactory`, các bộ đếm route và hàm giả lập track;
- DataPacket topics, payload shapes, encoding, and reliability;  
  > topic của DataPacket, cấu trúc payload, bộ mã hóa (encoding) và độ tin cậy (reliability);
- the special raw forwarding behavior for topic `lesson-graph-v2.voice`;  
  > hành vi chuyển tiếp raw payload đặc thù cho topic `lesson-graph-v2.voice`;
- legacy packet handling for `SET_ACTIVE_QUEST`, `VERBAL_HINT`, `ON_REMINDER`, `QUEST_MATCHED`, `AGENT_INIT_FAILED`, and `QUEST_STATUS`;  
  > xử lý gói tin legacy cũ cho các sự kiện `SET_ACTIVE_QUEST`, `VERBAL_HINT`, `ON_REMINDER`, `QUEST_MATCHED`, `AGENT_INIT_FAILED`, và `QUEST_STATUS`;
- the initial-connect behavior that raises `ReconnectedV2` after a successful connection;  
  > hành vi kích hoạt sự kiện `ReconnectedV2` ngay sau lần kết nối đầu tiên thành công;
- the existing POV connection wait limit of 50 attempts at 200 ms each;  
  > giới hạn thời gian chờ kết nối của POV hiện tại là 50 lần thử, mỗi lần cách nhau 200ms;
- legacy NPC `AudioSource` fallback when no V2 route is registered.  
  > cơ chế fallback về `AudioSource` NPC legacy khi chưa có route V2 nào được đăng ký.

**EN:** No Python agent or web-dashboard change is required because this refactor does not change a cross-stack contract.  
> **VI:** Không yêu cầu thay đổi phía Python agent hay Web dashboard vì đợt refactor này không thay đổi hợp đồng liên tầng (cross-stack contract).

---

## 4. Chosen Architecture / Kiến Trúc Lựa Chọn

**EN:** Use a compatibility façade with plain C# services behind it. `LiveKitService` remains a `MonoBehaviour`, Unity lifecycle host, and composition root. It constructs the internal services in `Awake`, drains the LiveKit-scoped main-thread queue in `Update`, and triggers deterministic disposal in `OnDestroy`.  
> **VI:** Sử dụng một façade tương thích với các plain C# service phía sau. `LiveKitService` vẫn là một `MonoBehaviour`, đóng vai trò máy chủ vòng đời Unity và composition root. Nó khởi tạo các service nội bộ trong `Awake`, xả (drain) hàng đợi main-thread phạm vi LiveKit trong `Update`, và kích hoạt dọn dẹp xác định trong `OnDestroy`.

**EN:** No service may call `LiveKitService.Instance`, search the scene for dependencies, or create a second global dispatcher.  
> **VI:** Không service con nào được phép gọi `LiveKitService.Instance`, tìm kiếm dependency trong scene, hoặc tạo ra bộ điều phối toàn cục (global dispatcher) thứ hai.

```text
LiveKitService
|
+-- LiveKitLifecycleCoordinator
|   +-- serializes Connect / Disconnect / Destroy (Tuần tự hóa Connect / Disconnect / Destroy)
|
+-- LiveKitRoomConnection
|   +-- owns the client-side LiveKit.Room object and SDK subscriptions (Sở hữu đối tượng SDK LiveKit.Room và các đăng ký SDK)
|
+-- LiveKitMainThreadExecutor
|   +-- generation-aware FIFO command queue drained from Update (Hàng đợi lệnh FIFO nhận biết generation, được xả từ Update)
|
+-- LiveKitDataPacketTransport
|   +-- sends and receives raw bytes, topics, and reliability (Gửi và nhận raw byte, topic và reliability)
|
+-- LegacyVoicePacketAdapter
|   +-- preserves legacy packet serialization and parsing (Bảo tồn tuần tự hóa và phân tích cú pháp gói tin legacy)
|
+-- LiveKitMicrophonePublisher
|   +-- owns microphone capture and the local audio track (Sở hữu việc thu mic và local audio track)
|
+-- LiveKitPovVideoPublisher
|   +-- owns capture camera, RenderTexture, source, coroutine, and video track (Sở hữu capture camera, RenderTexture, source, coroutine và video track)
|
+-- LiveKitNpcAudioRouter
    +-- owns remote audio routing, pending tracks, and active streams (Sở hữu định tuyến âm thanh từ xa, pending tracks và active streams)
```

### 4.1 LiveKitService
Responsibilities / Trách nhiệm:
- preserve the current public and serialized surface;  
  > bảo tồn bề mặt public API và serialized field hiện tại;
- construct and wire internal services;  
  > khởi tạo và kết nối dây (wire) các service nội bộ;
- forward public API calls to the correct service;  
  > chuyển tiếp các lời gọi public API đến đúng service phụ trách;
- relay internal events through the existing public events;  
  > tiếp sức (relay) các sự kiện nội bộ qua các sự kiện công khai hiện có;
- host `Update`, coroutine operations, and Unity destruction.  
  > làm host cho `Update`, các tác vụ coroutine, và sự kiện hủy của Unity.

*It must not contain packet parsing, media resource state, route dictionaries, or connection state-machine logic.*  
> *Nó không được chứa logic parse gói tin, trạng thái tài nguyên media, từ điển route, hay máy trạng thái kết nối.*

### 4.2 LiveKitLifecycleCoordinator
Responsibilities / Trách nhiệm:
- serialize connection lifecycle operations;  
  > tuần tự hóa các thao tác vòng đời kết nối;
- issue monotonically increasing connection generations;  
  > phát hành số hiệu thế hệ kết nối tăng dần đều (monotonically increasing generation);
- cancel or invalidate superseded operations;  
  > hủy hoặc vô hiệu hóa các tác vụ đã bị thay thế (superseded);
- coordinate media and routing teardown before connection teardown;  
  > điều phối dọn dẹp media và routing trước khi ngắt kết nối phòng;
- enforce latest-request-wins semantics for overlapping connect requests;  
  > thực thi ngữ nghĩa "yêu cầu mới nhất thắng" (latest-request-wins) đối với các yêu cầu kết nối chồng chéo;
- observe and log every internal task failure.  
  > quan sát và ghi log mọi lỗi tác vụ (task failure) nội bộ.

*It is the only component that decides the cross-service teardown order.*  
> *Đây là component duy nhất quyết định thứ tự dọn dẹp (teardown) liên dịch vụ.*

### 4.3 LiveKitRoomConnection
Responsibilities / Trách nhiệm:
- create and own the Unity client's `LiveKit.Room` SDK object;  
  > tạo và sở hữu đối tượng SDK `LiveKit.Room` phía Unity client;
- join the server-selected room using URL and token;  
  > tham gia phòng do server chỉ định bằng URL và token;
- subscribe and unsubscribe exact SDK event delegates;  
  > đăng ký và hủy đăng ký chính xác các delegate sự kiện của SDK;
- expose a connection-state snapshot and current handle;  
  > cung cấp snapshot trạng thái kết nối và handle hiện tại;
- forward SDK callbacks into the main-thread executor with the captured handle;  
  > chuyển tiếp các callback SDK vào executor main-thread kèm theo handle đã bắt;
- disconnect and dispose only its own connection object.  
  > ngắt kết nối và giải phóng duy nhất đối tượng kết nối của chính nó.

*It does not own microphone, video, remote audio, or gameplay protocol state.*  
> *Nó không sở hữu mic, video, âm thanh từ xa hay trạng thái giao thức gameplay.*

### 4.4 LiveKitMainThreadExecutor
**EN:** This is a LiveKit-scoped executor owned by `LiveKitService`, not an application-wide singleton.  
> **VI:** Đây là một executor nằm trong phạm vi LiveKit do `LiveKitService` sở hữu, không phải là singleton toàn ứng dụng.

Responsibilities / Trách nhiệm:
- accept commands from SDK callback threads;  
  > tiếp nhận các lệnh từ các thread callback của SDK;
- establish a total FIFO processing order at the enqueue boundary;  
  > thiết lập thứ tự xử lý FIFO hoàn toàn tại ranh giới đưa vào hàng đợi (enqueue);
- associate each command with a connection generation;  
  > gắn từng lệnh với một thế hệ kết nối (connection generation);
- execute valid commands from `LiveKitService.Update`;  
  > thực thi các lệnh hợp lệ từ `LiveKitService.Update`;
- reject stale-generation commands and commands posted after shutdown.  
  > từ chối các lệnh thuộc thế hệ cũ (stale-generation) và các lệnh được đẩy vào sau khi đã shutdown.

**EN:** Payload byte arrays are copied at the SDK callback boundary before enqueueing. Unity objects are created, mutated, or destroyed only while draining on the main thread.  
> **VI:** Mảng byte payload phải được copy (clone) tại ranh giới callback của SDK trước khi đưa vào hàng đợi. Các đối tượng Unity chỉ được tạo, biến đổi, hoặc hủy trong khi xả hàng đợi trên main thread.

### 4.5 LiveKitDataPacketTransport
Responsibilities / Trách nhiệm:
- publish raw byte payloads with the requested topic and reliability;  
  > phát (publish) payload raw byte với topic và độ tin cậy được yêu cầu;
- validate that a current connected handle and local participant are available;  
  > kiểm tra xem handle kết nối hiện tại và local participant có sẵn sàng hay không;
- receive raw SDK payloads after main-thread dispatch;  
  > nhận raw payload từ SDK sau khi đã được chuyển tiếp lên main thread;
- forward V2 payloads without understanding their JSON schema.  
  > chuyển tiếp payload V2 mà không cần hiểu cấu trúc schema JSON của chúng.

*It has no dependency on VoiceQuest, dialogue, or legacy packet DTOs.*  
> *Nó không phụ thuộc vào VoiceQuest, dialogue hay các DTO gói tin cũ.*

### 4.6 LegacyVoicePacketAdapter
Responsibilities / Trách nhiệm:
- preserve the current legacy packet byte representation;  
  > bảo tồn cấu trúc biểu diễn byte của gói tin legacy hiện tại;
- build packets used by `SendActiveQuest`, `SendVerbalHint`, and `SendOnReminder`;  
  > xây dựng các gói tin được dùng bởi `SendActiveQuest`, `SendVerbalHint`, và `SendOnReminder`;
- parse non-V2 incoming packets;  
  > phân tích các gói tin đến không thuộc chuẩn V2;
- emit internal speech-matched, agent-error, and quest-status signals.  
  > phát các tín hiệu nội bộ: speech-matched, agent-error và quest-status.

**EN:** The first refactor does not replace string-built legacy JSON with a new serializer. Any wire-format improvement is a separate change with its own compatibility tests.  
> **VI:** Lần refactor đầu tiên không thay thế chuỗi JSON legacy ghép thủ công bằng serializer mới. Mọi cải tiến định dạng truyền tải phải là một thay đổi riêng biệt kèm bài test tương thích riêng.

### 4.7 LiveKitMicrophonePublisher
Responsibilities / Trách nhiệm:
- remain the sole owner of microphone selection and capture;  
  > tiếp tục là chủ sở hữu duy nhất đối với việc chọn và thu âm microphone;
- own the microphone GameObject, `MicrophoneSource`, and `LocalAudioTrack`;  
  > sở hữu GameObject microphone, `MicrophoneSource`, và `LocalAudioTrack`;
- publish once, mute or unmute idempotently, and unpublish from the exact connection handle that created the track;  
  > publish một lần, mute hoặc unmute theo cách lũy đẳng (idempotent), và unpublish từ chính xác connection handle đã tạo ra track đó;
- roll back all temporary resources if publication fails or the generation becomes stale.  
  > hoàn tác (rollback) toàn bộ tài nguyên tạm thời nếu publish thất bại hoặc thế hệ kết nối đã cũ.

*No other component may start competing microphone capture.*  
> *Không component nào khác được phép bắt đầu thu mic tranh chấp.*

### 4.8 LiveKitPovVideoPublisher
Responsibilities / Trách nhiệm:
- resolve or validate the requested VR camera;  
  > phân giải hoặc kiểm tra camera VR được yêu cầu;
- wait for a connected handle using the current bounded wait behavior;  
  > chờ connection handle khả dụng theo đúng hành vi chờ có giới hạn hiện tại;
- own the capture camera, `RenderTexture`, `TextureVideoSource`, coroutine, and `LocalVideoTrack`;  
  > sở hữu camera phụ, `RenderTexture`, `TextureVideoSource`, coroutine và `LocalVideoTrack`;
- publish with the existing dimensions, frame rate, bitrate, and camera source options;  
  > publish với đúng kích thước, frame rate, bitrate và tùy chọn camera nguồn hiện có;
- stop and release resources in the required GPU-safe order;  
  > dừng và giải phóng tài nguyên theo đúng thứ tự an toàn cho GPU;
- unpublish from the exact connection handle that created the track.  
  > unpublish từ đúng connection handle đã tạo ra track.

### 4.9 LiveKitNpcAudioRouter
Responsibilities / Trách nhiệm:
- preserve legacy `SetAudioSource` behavior;  
  > bảo tồn hành vi `SetAudioSource` cũ;
- own V2 route registration and active route selection;  
  > sở hữu việc đăng ký route V2 và chọn route đang hoạt động;
- own pending and active track dictionaries;  
  > sở hữu các từ điển pending track và active track;
- bind, replace, rebind, and dispose remote `AudioStream` instances;  
  > bind, thay thế, rebind và dispose các instance `AudioStream` từ xa;
- preserve multi-NPC isolation, route swapping, pending-track drain, duplicate-SID replacement, and legacy fallback;  
  > bảo tồn việc cô lập đa NPC, đổi route, xả pending-track, thay thế SID trùng và fallback legacy;
- retain the injectable stream factory and current test-facing state queries.  
  > giữ lại stream factory tiêm được (injectable) và các hàm truy vấn trạng thái phục vụ test hiện có.

**EN:** All state mutations occur on the main thread. SDK track callbacks reach the router only through the executor.  
> **VI:** Toàn bộ biến đổi trạng thái đều diễn ra trên main thread. Callback track từ SDK chỉ đến được router thông qua executor.

---

## 5. Connection Handle and Ownership / Handle Kết Nối và Quyền Sở Hữu

**EN:** Every successful or attempted client connection is represented by an immutable handle:  
> **VI:** Mỗi kết nối client thành công hoặc đang thử đều được đại diện bằng một handle bất biến (immutable handle):

```text
RoomConnectionHandle
+-- Generation: monotonically increasing identifier (Định danh số nguyên tăng dần đều)
+-- SdkRoom: the LiveKit.Room instance for that generation (Instance LiveKit.Room của thế hệ đó)
```

**EN:** Media publishers retain the handle used to create their tracks. Cleanup uses that retained handle rather than reading a mutable global `room` field. This prevents a continuation belonging to connection A from unpublishing or mutating resources belonging to later connection B.  
> **VI:** Các publisher media giữ lại handle đã dùng để tạo track của chúng. Khi dọn dẹp sẽ dùng handle giữ lại này thay vì đọc một trường `room` toàn cục có thể bị thay đổi. Điều này ngăn một tác vụ tiếp diễn của kết nối A unpublish hoặc sửa đổi tài nguyên thuộc về kết nối B đến sau.

**EN:** Only `LiveKitRoomConnection` may replace the current SDK room reference. Only the lifecycle coordinator may advance the active generation.  
> **VI:** Chỉ duy nhất `LiveKitRoomConnection` được phép thay thế tham chiếu SDK room hiện tại. Chỉ duy nhất coordinator vòng đời được phép tăng số hiệu generation hoạt động.

---

## 6. Lifecycle State Machine / Máy Trạng Thái Vòng Đời

```text
Disconnected -> Connecting -> Connected -> Disconnecting -> Disconnected

Disconnected / Connecting / Connected / Disconnecting -> Destroyed
```

**EN:** Internally, lifecycle methods return `Task`; internal logic does not use `async void`. The existing public `void` signatures submit tasks to the coordinator, which observes and logs completion and failure.  
> **VI:** Về mặt nội bộ, các method vòng đời trả về `Task`; logic nội bộ không dùng `async void`. Các signature `void` công khai hiện có sẽ gửi task cho coordinator, và coordinator sẽ theo dõi, ghi log việc hoàn thành hoặc thất bại.

### 6.1 Connect
1. Assign the request a new generation.  
   > Gán cho request một generation mới.
2. Invalidate and cancel the previous generation.  
   > Vô hiệu hóa và hủy bỏ generation trước đó.
3. Serialize behind any active lifecycle operation.  
   > Xếp hàng tuần tự sau bất kỳ thao tác vòng đời nào đang diễn ra.
4. Tear down the previous handle if necessary.  
   > Dọn dẹp handle trước nếu cần thiết.
5. Create a client-side SDK room object and attach captured-handle callbacks.  
   > Tạo đối tượng SDK room phía client và gắn các callback mang handle đã bắt.
6. Await SDK connection.  
   > Chờ (await) kết nối SDK hoàn tất.
7. Verify that the generation is still current.  
   > Xác minh lại xem generation này có còn là hiện tại hay không.
8. Commit the handle and publish the connected state.  
   > Cam kết (commit) handle và phát trạng thái đã kết nối (Connected).
9. Raise `ReconnectedV2` on the main thread, preserving current semantics.  
   > Kích hoạt sự kiện `ReconnectedV2` trên main thread, bảo tồn đúng ngữ nghĩa hiện tại.

**EN:** If a newer connect request arrives while an earlier connect is awaiting, the newest request wins. The earlier continuation may only clean up resources tied to its own handle.  
> **VI:** Nếu có một yêu cầu connect mới hơn đến trong khi connect trước đó đang chờ (awaiting), yêu cầu mới nhất sẽ thắng. Đoạn code tiếp diễn của yêu cầu cũ chỉ được dọn dẹp các tài nguyên gắn với handle của chính nó.

### 6.2 Disconnect
**EN:** Disconnect immediately invalidates the current generation, cancels pending operations where supported, and schedules the following teardown order:  
> **VI:** Disconnect lập tức vô hiệu hóa generation hiện tại, hủy các tác vụ đang chờ nếu được hỗ trợ, và thực hiện theo đúng thứ tự dọn dẹp sau:

1. reject new commands for the old generation;  
   > từ chối các lệnh mới dành cho generation cũ;
2. stop and unpublish the POV track;  
   > dừng và unpublish POV video track;
3. mute/stop and unpublish the microphone track;  
   > mute/dừng và unpublish microphone track;
4. dispose remote audio streams and clear pending routing state;  
   > giải phóng (dispose) các audio stream từ xa và xóa trạng thái routing đang chờ;
5. detach SDK event handlers;  
   > gỡ bỏ các event handler của SDK;
6. disconnect the client-side SDK room object;  
   > ngắt kết nối đối tượng SDK room phía client;
7. publish `Disconnected` state.  
   > phát trạng thái `Disconnected`.

**EN:** The operation is idempotent. A cleanup failure is logged and does not prevent the remaining cleanup stages.  
> **VI:** Thao tác này là lũy đẳng (idempotent). Lỗi trong một giai đoạn dọn dẹp sẽ được ghi log và không ngăn cản các giai đoạn dọn dẹp còn lại.

### 6.3 Destroy
**EN:** `OnDestroy` closes the executor and generation immediately, performs best-effort synchronous cleanup of owned Unity resources, detaches callbacks, and disconnects the SDK object. It does not block the Unity main thread indefinitely waiting for a network task.  
> **VI:** `OnDestroy` đóng executor và generation ngay lập tức, nỗ lực tối đa dọn dẹp đồng bộ các tài nguyên Unity thuộc sở hữu, gỡ callback, và ngắt kết nối SDK. Nó không block main thread của Unity vô tận để chờ một tác vụ mạng.

**EN:** Any asynchronous continuation that completes afterward must validate its captured generation before taking action.  
> **VI:** Bất kỳ đoạn mã tiếp diễn bất đồng bộ nào hoàn thành sau đó đều phải xác thực generation đã bắt trước khi thực hiện hành động.

---

## 7. Data and Event Flows / Luồng Dữ Liệu và Sự Kiện

### 7.1 Incoming DataPacket / Gói Tin Đến
```text
Room.DataReceived
  -> capture handle and copy payload (Bắt handle và copy payload)
  -> LiveKitMainThreadExecutor
  -> LiveKitDataPacketTransport
       |-- topic == "lesson-graph-v2.voice"
       |      -> LiveKitService.DataReceivedV2
       |      -> return without legacy parsing (Return mà không parse legacy)
       |
       +-- any other/legacy topic (Topic khác hoặc topic mặc định legacy)
              -> LegacyVoicePacketAdapter
              -> existing public legacy events (Phát các event legacy hiện có)
```

**EN:** Both voice quest V2 and dialogue V2 continue to share the exact topic `lesson-graph-v2.voice`.  
> **VI:** Cả Voice Quest V2 và Dialogue V2 tiếp tục chia sẻ chính xác topic `lesson-graph-v2.voice`.

### 7.2 Outgoing DataPacket / Gói Tin Đi
```text
PublishDataV2
  -> LiveKitDataPacketTransport

SendActiveQuest / SendVerbalHint / SendOnReminder
  -> LegacyVoicePacketAdapter
  -> LiveKitDataPacketTransport
```

### 7.3 Remote Audio / Âm Thanh Từ Xa
```text
Room.TrackSubscribed
  -> capture handle and track metadata (Bắt handle và metadata của track)
  -> main-thread executor
  -> LiveKitNpcAudioRouter
       |-- active V2 route exists: bind stream (Route V2 active tồn tại: bind stream)
       |-- route absent: retain pending track by SID (Chưa có route: giữ pending track theo SID)
       +-- no V2 routes: use legacy AudioSource fallback (Không có route V2: fallback AudioSource legacy)

Room.TrackUnsubscribed
  -> main-thread executor
  -> dispose matching active stream (Dispose stream active tương ứng)
  -> remove matching active and pending entries (Xóa các mục active và pending tương ứng)
```

### 7.4 Public API Routing / Điều Hướng Public API

| Existing API / API Hiện Có | Internal target / Module Đích Nội Bộ |
|---|---|
| `Connect`, `Disconnect` | `LiveKitLifecycleCoordinator` |
| `EnableMicrophone` | coordinator and `LiveKitMicrophonePublisher` |
| `EnablePOVCamera`, `DisablePOVCamera` | coordinator and `LiveKitPovVideoPublisher` |
| `PublishDataV2` | `LiveKitDataPacketTransport` |
| legacy send methods | `LegacyVoicePacketAdapter` |
| route and legacy audio-source methods | `LiveKitNpcAudioRouter` |

---

## 8. Concurrency Invariants / Bất Biến Đồng Thời (Concurrency)

The implementation must preserve these invariants:  
> Bản triển khai bắt buộc phải giữ vững các bất biến sau:

1. Unity API calls occur only on the Unity main thread.  
   > Lời gọi Unity API chỉ diễn ra trên Unity main thread.
2. There is one serialized lifecycle lane for connect, disconnect, and destroy.  
   > Chỉ có duy nhất một làn (lane) vòng đời được tuần tự hóa cho connect, disconnect và destroy.
3. No lock is held across an `await`.  
   > Không giữ lock xuyên qua lệnh `await`.
4. No public event is raised while an internal lock is held.  
   > Không kích hoạt public event khi đang giữ lock nội bộ.
5. A callback or continuation may mutate state only when its captured generation is current.  
   > Một callback hoặc continuation chỉ được biến đổi trạng thái khi generation đã bắt của nó khớp với generation hiện tại.
6. Media cleanup targets the exact connection handle that created the media.  
   > Việc dọn dẹp media nhắm chính xác vào connection handle đã tạo ra media đó.
7. Internal task exceptions are always observed.  
   > Các exception của task nội bộ luôn luôn được quan sát (observed).
8. A failed cleanup stage cannot suppress later cleanup stages.  
   > Một giai đoạn cleanup bị thất bại không được phép triệt tiêu các giai đoạn cleanup sau nó.
9. Remote audio state is mutated only by the main-thread router.  
   > Trạng thái âm thanh từ xa chỉ được biến đổi bởi router trên main thread.
10. The executor is the only component requiring cross-thread queue synchronization.  
    > Executor là component duy nhất cần đồng bộ hóa hàng đợi liên thread.
11. Public events are relayed on the Unity main thread.  
    > Các public event được tiếp sức (relay) trên Unity main thread.
12. POV frame production does not traverse the command queue; only lifecycle and SDK callbacks do.  
    > Việc tạo frame video POV không đi qua command queue; chỉ có vòng đời và callback SDK mới đi qua.

**EN:** Existing Unity-facing public calls, including synchronous router queries, are main-thread APIs. The façade records its owning thread during `Awake` and rejects or development-asserts invalid off-thread Unity-facing calls rather than mutating Unity state concurrently. Calls originating from LiveKit SDK threads are always marshalled through the executor. This makes the thread-affinity contract explicit without introducing a blocking cross-thread `Invoke` that could deadlock while Unity is not pumping `Update`.  
> **VI:** Các lệnh gọi công khai hướng về Unity hiện có (kể cả truy vấn router đồng bộ) đều là API main-thread. Façade ghi nhận thread sở hữu của nó trong `Awake`, từ chối hoặc assert khi dev nếu có lời gọi ngoài main-thread thay vì biến đổi trạng thái Unity đồng thời. Các lời gọi bắt nguồn từ LiveKit SDK thread luôn được gom chuyển qua executor. Điều này làm rõ ràng hợp đồng về thread mà không cần dùng `Invoke` chặn liên thread (blocking cross-thread) dễ gây deadlock khi Unity chưa chạy `Update`.

---

## 9. Resource and Error Policy / Chính Sách Tài Nguyên và Xử Lý Lỗi

Media acquisition is transactional:  
> Việc khởi tạo media mang tính giao dịch (transactional):

```text
create temporary resources (Tạo tài nguyên tạm)
  -> publish track
       |-- success and current generation: commit active state (Thành công & đúng generation: commit active state)
       +-- failure or stale generation: roll back every temporary resource (Thất bại hoặc stale generation: rollback toàn bộ tài nguyên tạm)
```

Required idempotent operations include:  
> Các thao tác bắt buộc phải có tính lũy đẳng (idempotent) gồm:

- repeated disconnect;  
  > disconnect nhiều lần;
- disabling POV before or after it is active;  
  > tắt POV trước hoặc sau khi nó đang chạy;
- repeated microphone enable or mute requests;  
  > lặp lại yêu cầu bật mic hoặc mute;
- repeated subscribe for the same track SID;  
  > subscribe lặp lại cho cùng một track SID;
- unsubscribe for an absent or already-cleaned track;  
  > unsubscribe cho một track không tồn tại hoặc đã dọn dẹp xong;
- re-registering the same route/source;  
  > đăng ký lại cùng một route/source;
- `OnDestroy` following manual disconnect.  
  > `OnDestroy` xảy ra sau khi đã gọi disconnect thủ công.

Error behavior / Hành vi khi gặp lỗi:
- connect failure detaches callbacks, cleans the temporary SDK object, returns to `Disconnected`, and does not raise `ReconnectedV2`;  
  > lỗi connect sẽ gỡ callback, dọn đối tượng SDK tạm, trở về `Disconnected`, và không kích hoạt `ReconnectedV2`;
- media failure does not terminate the room connection or packet transport;  
  > lỗi media không làm đứt kết nối phòng hay packet transport;
- packet parse failure drops only the invalid packet;  
  > lỗi parse gói tin chỉ bỏ rơi gói tin không hợp lệ đó;
- stale-generation callbacks are intentionally ignored;  
  > các callback thuộc generation cũ cố tình bị bỏ qua;
- logs include the generation and operation name where useful;  
  > log kèm số generation và tên thao tác khi cần thiết;
- public gameplay events preserve their current meaning.  
  > các event gameplay công khai giữ nguyên ý nghĩa hiện tại.

---

## 10. Testing Strategy / Chiến Lược Kiểm Thử

### 10.1 Characterization Tests / Kiểm Thử Định Tính Baseline
Lock down before extraction / Khóa chặt trước khi bóc tách:
- exact legacy packet bytes, V2 topic, and reliability;  
  > định dạng byte chính xác của gói legacy, topic V2 và độ tin cậy;
- initial-connect `ReconnectedV2` behavior;  
  > hành vi phát `ReconnectedV2` ở lần kết nối đầu;
- POV wait bound;  
  > giới hạn chờ kết nối của POV;
- microphone enable/mute semantics;  
  > ngữ nghĩa bật/mute microphone;
- V2 route registration, swapping, isolation, pending-track drain, and active-route switching;  
  > đăng ký route V2, tráo route, cô lập route, xả pending-track và chuyển đổi active route;
- legacy audio fallback;  
  > fallback âm thanh legacy;
- duplicate SID replacement and teardown order.  
  > thay thế SID trùng và thứ tự dọn dẹp.

### 10.2 SDK Adapter and Deterministic Fakes / Adapter SDK và Fake Xác Định
Introduce narrow internal adapter/factory around SDK room operations. Fake implementation must be able to:  
> Đưa vào adapter/factory hẹp bao quanh các thao tác SDK room. Bản fake phải có khả năng:
- pause and complete connect or track publication using controlled tasks;  
  > tạm dừng và hoàn tất connect hoặc publish track bằng task kiểm soát được;
- trigger SDK callbacks explicitly;  
  > kích hoạt trực tiếp các callback của SDK;
- record publish, unpublish, disconnect, and subscription order;  
  > ghi nhận thứ tự publish, unpublish, disconnect và subscription;
- simulate failures without network access.  
  > mô phỏng lỗi mà không cần mạng.

### 10.3 Concurrency Matrix / Ma Trận Đồng Thời
Deterministically test / Kiểm thử xác định:
- connect A awaiting, followed by connect B;  
  > connect A đang chờ, nối tiếp bởi connect B;
- disconnect during connect;  
  > disconnect trong khi đang connect;
- disconnect during microphone publication;  
  > disconnect trong khi đang publish mic;
- destroy during POV publication;  
  > destroy trong khi đang publish POV;
- room-A callback after room B becomes current;  
  > callback của room A đến sau khi room B đã trở thành hiện tại;
- subscribe, route swap, and unsubscribe interleavings;  
  > các kịch bản đan xen giữa subscribe, đổi route và unsubscribe;
- queued subscribe followed by unsubscribe;  
  > subscribe trong hàng đợi nối tiếp ngay bởi unsubscribe;
- repeated reconnect callbacks;  
  > các callback reconnect lặp lại;
- packet delivery after generation rollover.  
  > chuyển phát gói tin sau khi đổi generation.

Assertions must prove / Các assertion bắt buộc phải chứng minh:
- stale generations emit no public event;  
  > generation cũ không phát bất kỳ public event nào;
- only the latest generation becomes connected;  
  > chỉ có generation mới nhất được kết nối;
- no track, stream, coroutine, camera, or texture leaks;  
  > không rò rỉ track, stream, coroutine, camera hay texture;
- no double-unpublish or double-dispose;  
  > không bị unpublish hoặc dispose 2 lần;
- teardown order is stable;  
  > thứ tự teardown luôn ổn định;
- public callbacks run on the Unity main thread.  
  > public callback luôn chạy trên Unity main thread.

### 10.4 Verification Layers / Các Tầng Xác Minh
- **Unit:** lifecycle state machine, executor, packet adapter, and audio router.  
  > Unit: máy trạng thái vòng đời, executor, packet adapter và audio router.
- **EditMode:** façade/interface compatibility and Unity object ownership.  
  > EditMode: tính tương thích façade/interface và quyền sở hữu đối tượng Unity.
- **PlayMode:** `Awake`, `Update`, `OnDestroy`, coroutine, and resource cleanup behavior.  
  > PlayMode: hành vi `Awake`, `Update`, `OnDestroy`, coroutine và dọn dẹp tài nguyên.
- **Device smoke:** Meta Quest and HTC Vive microphone permission/exclusivity, reconnect, NPC routing, and POV 720p at 30 FPS.  
  > Test thực tế trên kính: quyền/độc quyền microphone trên Meta Quest và HTC Vive, reconnect, định tuyến NPC và POV 720p 30fps.
- **LiveKit integration smoke:** real join/leave, audio/video publish/unpublish, and DataPacket round trip.  
  > Test tích hợp LiveKit thật: join/leave, publish/unpublish audio/video và vòng gửi/nhận DataPacket.

---

## 11. Incremental Migration Plan / Kế Hoạch Di Chuyển Từng Bước

**EN:** The refactor must not be a big-bang rewrite.  
> **VI:** Việc tái cấu trúc tuyệt đối không được viết lại kiểu đập đi xây lại (big-bang rewrite).

1. Add characterization tests and establish the current dirty working tree as the behavioral baseline.  
   > Thêm các test đặc tả và xác lập working tree hiện tại làm baseline hành vi.
2. Add SDK adapter/factory seams and deterministic fakes without moving production behavior.  
   > Thêm seam adapter/factory cho SDK và fake xác định mà không di chuyển code production.
3. Extract `LiveKitNpcAudioRouter`; preserve façade delegation and all current routing tests.  
   > Bóc tách `LiveKitNpcAudioRouter`; bảo tồn việc ủy quyền của façade và toàn bộ test routing hiện có.
4. Extract `LiveKitDataPacketTransport` and `LegacyVoicePacketAdapter`.  
   > Bóc tách `LiveKitDataPacketTransport` và `LegacyVoicePacketAdapter`.
5. Extract `LiveKitMicrophonePublisher`.  
   > Bóc tách `LiveKitMicrophonePublisher`.
6. Extract `LiveKitPovVideoPublisher`.  
   > Bóc tách `LiveKitPovVideoPublisher`.
7. Add `RoomConnectionHandle`, `LiveKitMainThreadExecutor`, and `LiveKitRoomConnection`.  
   > Bổ sung `RoomConnectionHandle`, `LiveKitMainThreadExecutor`, và `LiveKitRoomConnection`.
8. Move lifecycle orchestration into `LiveKitLifecycleCoordinator`.  
   > Chuyển việc điều phối vòng đời vào `LiveKitLifecycleCoordinator`.
9. Reduce `LiveKitService` to its approved façade and lifecycle-host responsibilities.  
   > Thu gọn `LiveKitService` về đúng vai trò façade đã duyệt và host vòng đời Unity.
10. Run full regression, GitNexus change detection, and device/integration smoke tests.  
    > Chạy toàn bộ test hồi quy, kiểm tra thay đổi GitNexus và test thực tế trên thiết bị/tích hợp.

After every slice / Sau mỗi lát cắt:
- compile the Unity project;  
  > biên dịch project Unity;
- run targeted tests;  
  > chạy các bài test mục tiêu;
- run the complete relevant EditMode suite;  
  > chạy toàn bộ EditMode suite liên quan;
- inspect GitNexus `detect_changes` output;  
  > kiểm tra kết quả `detect_changes` của GitNexus;
- stop if the affected flows exceed the expected slice.  
  > dừng lại ngay nếu phạm vi ảnh hưởng vượt quá lát cắt dự kiến.

---

## 12. Acceptance Criteria / Tiêu Chí Nghiệm Thu

The refactor is complete only when:  
> Việc tái cấu trúc chỉ hoàn thành khi:

- existing scenes, prefabs, and consumers require no migration;  
  > các scene, prefab và caller hiện có không cần phải di chuyển hay chỉnh sửa gì;
- public APIs, interfaces, events, and wire contracts remain unchanged;  
  > public API, interface, event và hợp đồng gói tin giữ nguyên vẹn;
- all existing and new tests pass;  
  > toàn bộ test cũ và mới đều PASS;
- every Unity operation is main-thread confined;  
  > mọi thao tác Unity đều được giới hạn trên main thread;
- there are no unobserved task exceptions;  
  > không có bất kỳ exception nào của task bị bỏ sót (unobserved);
- stale generations cannot emit events or mutate current state;  
  > generation cũ không thể phát event hoặc làm biến đổi trạng thái hiện tại;
- repeated connect/disconnect leaves no owned resource behind;  
  > connect/disconnect nhiều lần không để lại tài nguyên rò rỉ;
- microphone capture remains exclusive;  
  > thu âm microphone vẫn được độc quyền hoàn toàn;
- GitNexus reports only expected affected flows;  
  > GitNexus chỉ báo cáo các luồng bị ảnh hưởng đúng như dự kiến;
- Meta Quest/HTC Vive device smoke tests pass;  
  > test thực tế trên Meta Quest/HTC Vive đều PASS;
- LiveKit integration smoke tests pass.  
  > test tích hợp LiveKit thực tế PASS.

---

## 13. Explicit Non-Goals / Những Thứ Cố Tình Không Làm (Non-Goals)

This refactor does not:  
> Đợt refactor này cố tình không thực hiện:

- change server-side LiveKit room provisioning or token issuance;  
  > thay đổi việc cấp phát phòng hoặc phát hành token phía server;
- change Python agent or web-dashboard contracts;  
  > thay đổi hợp đồng giao tiếp với Python agent hoặc Web dashboard;
- replace legacy packet JSON construction;  
  > thay thế cách ghép chuỗi JSON gói tin legacy cũ;
- remove legacy `ILiveKitRoomClient` or `SetAudioSource` behavior;  
  > gỡ bỏ interface `ILiveKitRoomClient` cũ hoặc hành vi `SetAudioSource`;
- redesign gameplay voice/dialogue transports;  
  > thiết kế lại các transport voice/dialogue của gameplay;
- introduce a global event bus or global main-thread dispatcher;  
  > đưa vào event bus toàn cục hay main-thread dispatcher toàn cục;
- add sibling `MonoBehaviour` components to scenes.  
  > thêm các component `MonoBehaviour` anh em vào các scene.
