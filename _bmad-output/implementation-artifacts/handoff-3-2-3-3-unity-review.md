# Handoff review trong Unity — Story 3.2 và 3.3

**Ngày:** 2026-09-29

**Trạng thái:** Cả hai story đang `review`; chưa có bằng chứng Unity compile, EditMode tests hoặc kiểm tra thủ công.

**Unity Editor của project:** `6000.3.13f1` (`ProjectSettings/ProjectVersion.txt`).

## 1. Mở đúng project và giữ nguyên phạm vi

Mở **project Unity tại worktree**:

```text
C:\Users\Admin\.codex\worktrees\epic-3-story-3-1\VR-Autism
```

Worktree đang ở branch `codex/story-3-2`, HEAD `8a10eb9b5f98760ecc557f418374875450d048d0`. Story 3.2 và 3.3 cùng nằm trong **working tree chưa commit**. Tên folder `epic-3-story-3-1` đã cũ; không dùng tên folder để suy ra nội dung. `D:\Lab\VR-Autism` là checkout Unity chính, có chỉnh sửa scene/asset của bạn và **chưa chứa** thay đổi của hai story này. Không copy hoặc đồng bộ file giữa hai checkout khi review.

Nếu muốn đối chiếu trước khi mở Unity, chạy `git status --short --branch` trong worktree. Unity sẽ import/compile project; chờ Console hết lỗi biên dịch trước khi chạy Test Runner. Dùng luồng khởi tạo session V2 hiện có khi kiểm tra Play Mode: `LessonGraphRunnerInstaller` từ chối `StartLesson` nếu session/telemetry V2 chưa sẵn sàng hoặc cùng installer đã yêu cầu chạy một lesson trong session đó. Nếu điều kiện này chưa có, ghi manual review là **blocked**, không tính thành lỗi Timeline/Parallel/Loop.

## 2. Bằng chứng cần lấy theo thứ tự

| Bước | Cách kiểm tra | Điều kiện đạt / bằng chứng |
| --- | --- | --- |
| Compile | Mở worktree bằng Unity `6000.3.13f1`; chờ import xong, xem Console. | Không có C# compile error trong `LessonGraphV2`. Ghi phiên bản Editor, thời gian và ảnh hoặc nội dung lỗi nếu có. |
| EditMode Story 3.2 | Test Runner → EditMode; chạy filter fixture `TimelineNodeExecutorTests`, rồi `TimelineExecutionCompositionTests`. | Ghi số pass/fail/skip cho từng fixture. Copy đầy đủ tên test, stack trace và Console của mọi fail. |
| EditMode Story 3.3 | Chạy lần lượt `StructuredFlowConditionTests`, `StructuredFlowExecutionTests`, `LessonGraphVariableStoreTests`, `AdvancedGraphRunnerTests`, `AdvancedGraphValidatorTests`. | Ghi số pass/fail/skip cho từng fixture. Không cần chạy full Unity suite ở bước này. |
| Manual 3.2 | Các ca ở mục 4. | Ảnh cấu hình graph/Director, log `ENTER`/`COMPLETE`, trạng thái playback và binding sau khi kết thúc. |
| Manual 3.3 | Các ca ở mục 5. | Ảnh cấu hình graph/Gate/Loop/store, log kết quả và số lần body/branch chạy. |

Các test coroutine dùng `[UnityTest]` và giới hạn số frame chờ. Nếu test báo không hoàn tất trong giới hạn, gửi nguyên stack trace/Console và tên fixture; không coi đó là pass. Test Runner phải chạy trên **worktree project** nói trên.

## 3. Điểm neo để quan sát

Có thể gắn `LessonGraphDebugLogger` lên GameObject có `LessonGraphRunner` để Console hiện `ENTER node=... activation=...`, `COMPLETE node=... status=... channel=...` và `LESSON COMPLETE`. Khi đánh giá một node, xem `NodeCompleted`/`CurrentState.status`; `LessonResult.IsSuccess` một mình không đủ để phân biệt terminal node có status `Failed` hay `Timeout`.

`LessonGraphRunner` có `RunNodeVisitCounts` và `RunEdgeVisitCounts` (khóa edge dạng `from->to`) để xem qua debugger/test harness. Các owned child của Parallel/Loop **không** phát `NodeEntered`/`NodeCompleted` ở cấp graph; chúng có thể hiện trong log executor hoặc bộ đếm visit. `LessonGraphRunnerInstaller` tự tìm `TimelinePlaybackController` và `LessonGraphVariableStore` trên cùng GameObject nếu Inspector chưa gán; controller vẫn cần tham chiếu `PlayableDirector` riêng.

## 4. Manual review Story 3.2 — Timeline signal

### Chuẩn bị

1. Trong scene thử ở worktree, cấu hình một `LessonGraphRunner`, `LessonGraphRunnerInstaller`, `LessonGraphBindings` và `TimelinePlaybackController`. Gán `PlayableDirector` chuyên dụng vào trường `_director` của controller; gán controller vào trường Timeline của installer hoặc đặt cùng GameObject để installer tự tìm. Director không được đang phát asset khác; để nó sẵn cho controller gán `PlayableAsset` khi node bắt đầu.
2. Tạo graph schema 2 có node `Timeline` trỏ đến `TimelineAsset`. Asset phải có `SignalTrack` với `SignalEmitter` tham chiếu một `SignalAsset`; tên asset phải trùng **chính xác, phân biệt chữ hoa/thường** với `ExpectedSignalName`. Đặt `TimeoutSeconds > 0`, chọn `TimeoutOutcome` là `Timeout` hoặc `Failed`.
3. Dùng session V2 hợp lệ và đường chạy lesson hiện có. Mỗi ca Play Mode nên bắt đầu bằng một session/run mới vì installer chặn lần start thứ hai của cùng session. Không cần sửa scene chính tại `D:\Lab\VR-Autism`.

| Ca | Thao tác | Kết quả mong đợi |
| --- | --- | --- |
| Signal đúng | Cho Timeline phát đến emitter có tên khớp. | Director phát asset đã cấu hình. Node hoàn tất **một lần** với `Success`, `channel=signal`; graph chuyển tiếp theo edge của parent. Director dừng và SignalReceiver tạm được tháo. |
| Signal sai/không biết | Đặt emitter dùng signal khác chạy trước emitter đúng. | Không có `Success` tại signal sai; node chỉ thành công tại signal đúng. |
| Hết thời gian | Đặt emitter đúng sau deadline hoặc không cho nó phát trước deadline; thử cả hai `TimeoutOutcome`. | Trả `Timeout` hoặc `Failed` đúng cấu hình, `channel=timeout`; Director dừng, không có success muộn. |
| Skip/abort | Khi Director đang phát, gọi `RequestSkip()` hoặc `AbortLesson()` qua control V2/test harness của scene. | Skip trả `Skipped`; abort kết thúc lesson ở trạng thái cancelled/aborted. Cả hai giải phóng playback và listener; signal đến sau đó không tạo completion thứ hai. |
| Scene unload/disable | Khi đang phát, unload scene thử hoặc disable GameObject chứa runner/controller. | Playback dừng, listener tạm biến mất, binding `SignalTrack` trước đó được phục hồi, không xuất hiện success muộn. |

Các biến thể signal trùng, signal của activation cũ, hủy đúng lúc callback và Director bị destroy được kiểm tra sâu hơn trong `TimelineNodeExecutorTests`. Nếu một ca thủ công không có control để kích hoạt skip/abort, ghi rõ ca đó là **chưa kiểm tra** và dùng kết quả test tự động tương ứng làm bằng chứng riêng, không đánh dấu manual pass.

## 5. Manual review Story 3.3 — Parallel, Gate, Loop, điều kiện

### Parallel và Gate

Tạo graph schema 2: `Parallel` có **ít nhất hai** branch ID khác nhau, mỗi branch trỏ một child node khác nhau (dễ thử nhất là `Wait` với thời lượng khác nhau). Gate phải khai báo đúng owner Parallel và đủ các branch ID; graph cần edge `Parallel -> Gate`, rồi edge `Gate -> done`. Child không có outgoing edge thông thường. Dùng `LessonGraphDebugLogger` và/hoặc bộ đếm visit để quan sát.

| Ca | Cấu hình/thao tác | Kết quả mong đợi |
| --- | --- | --- |
| `AllSuccess` + Gate `And` | Hai `Wait` child đều hoàn thành. | Parent Parallel phát một result `Success`; Gate phát một result `Success`; child không tự đi theo edge hay phát completion cấp graph. Mỗi child được visit một lần. |
| `FirstCompleted` + Gate `Or` | Một `Wait` ngắn, một `Wait` dài. | Kết quả hợp lệ đầu tiên quyết định join; sibling bị hủy. Gate dùng evidence của branch thắng và đi tiếp một lần. Callback/completion muộn của sibling không làm đổi result. |
| Branch thất bại | Dùng một child có thể trả `Failed`/`Timeout` và edge `Parallel -> recovery` với `StatusCondition("failed")`. | `AllSuccess` thất bại sớm, hủy sibling và đi theo failure route; Gate không chạy. |
| Abort/disable | Hủy lesson hoặc disable runner khi hai child còn hoạt động. | Scope của cả hai child bị hủy; không có Gate join hoặc graph advance sau callback muộn. |

Thiết kế hiện tại chọn `FirstCompleted` theo **kết quả terminal hợp lệ đầu tiên**, kể cả `Failed`; khi kết quả đó không phải `Success`, runner đi theo failure route thay vì vào Gate. `FirstCompleted` + Gate `And` bị validation từ chối. Runtime cũng từ chối nested Parallel/Gate/Loop child và Parallel có hơn một Timeline child vì một controller scene không thể chạy đồng thời nhiều Timeline.

### Loop và variable source

Tạo `Loop` có body child `Wait`, `ExitNodeId` trỏ node `done`, `MaximumIterations > 0`; body không có outgoing edge thông thường. Với ca exit sớm, dùng `StatusCondition("success")`: sau một body success, Loop đi đến `done`. Với ca limit, dùng `VariableCondition(done == true)` và `LessonGraphVariableStore` có initial `done = false`, maximum 2, cộng edge từ Loop đến `recovery` với `StatusCondition("failed")`: body phải chạy đúng hai lần, Loop trả `Failed` với `channel=loop_limit`, rồi đi recovery. Bộ đếm `RunNodeVisitCounts[bodyId]` phải là 2.

Để thử biến runtime, gắn `LessonGraphVariableStore` vào scene và gán ở installer (hoặc đặt cùng GameObject). Initial values được nạp lúc `Awake`; sửa list trong Inspector **sau** `Awake` không cập nhật ngay giá trị đang dùng. Trong khi chạy, dùng API typed `SetBoolean`, `SetInteger`, `SetFloat`, `SetString`. Một graph có `VariableCondition` nhưng không có store/source sẽ bị execution preflight từ chối trước activation. Tên biến so khớp chính xác, phân biệt chữ hoa/thường; biến thiếu hoặc sai type cho kết quả condition `false`.

Các phép so sánh bool/int/float/string, composite AND/OR, Gate input thiếu/trùng, result child sai activation và visit counts được kiểm tra trong các fixture EditMode ở mục 2.

## 6. Trạng thái, file cần review và cách trả bằng chứng

| Story | File runtime/kiểm thử chính |
| --- | --- |
| 3.2 | `Runtime/TimelinePlaybackController.cs`, `Runtime/Executors/TimelineNodeExecutor.cs`, `Runtime/LessonGraphExecutorRegistry.cs`, `Tests/Editor/TimelineNodeExecutorTests.cs`, `Tests/Editor/TimelineExecutionCompositionTests.cs` |
| 3.3 | `Runtime/StructuredFlowExecution.cs`, `Runtime/LessonConditionEvaluator.cs`, `Runtime/LessonVariableValue.cs`, `Runtime/LessonGraphVariableStore.cs`, `Runtime/LessonGraphRunner.cs`, `Runtime/LessonGraphRunnerInstaller.cs`, `Validation/LessonGraphValidator.cs`, `Tests/Editor/StructuredFlowExecutionTests.cs`, `Tests/Editor/StructuredFlowConditionTests.cs`, `Tests/Editor/LessonGraphVariableStoreTests.cs`, `Tests/Editor/AdvancedGraphRunnerTests.cs`, `Tests/Editor/AdvancedGraphValidatorTests.cs` |

Các đường dẫn trong bảng tính từ `Assets/Project/Scripts/Gameplay/LessonGraphV2/`; file `.meta` đi kèm file mới. Spec và plan riêng của mỗi story ở cùng folder `_bmad-output/implementation-artifacts/` với tài liệu này. Story 3.3 dùng lại và mở rộng execution preflight của 3.2; câu trong spec 3.2 nói runtime còn chặn Story 3.3 là trạng thái tại thời điểm riêng của 3.2, **không còn là trạng thái của branch gộp hiện tại**.

Trước handoff này, kiểm tra ngoài Unity có `git diff --check` đạt, `.meta` GUID không trùng, và GitNexus `detect_changes` báo **MEDIUM** trên diff gộp 3.2/3.3 (một flow liên quan `RunAsync`). Đây không phải bằng chứng Unity compile/test. Không có commit/push, không chuyển story sang `done`.

Khi trả kết quả review, gửi: (1) Unity version và đúng project path; (2) compile pass/fail; (3) pass/fail/skip theo fixture; (4) với mọi fail, tên test đầy đủ, stack trace và Console; (5) với ca manual, tên scene/graph, cấu hình liên quan, log `ENTER`/`COMPLETE` hoặc ảnh, kết quả thực tế so với bảng. Nếu một điều kiện session/telemetry chặn test, ghi rõ log chặn và đánh dấu `blocked`. Sau khi có bằng chứng compile, focused EditMode và manual cần thiết, mới xét chuyển từng story khỏi `review`.
