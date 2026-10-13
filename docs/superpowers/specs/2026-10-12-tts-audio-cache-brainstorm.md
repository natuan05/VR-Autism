# Brainstorm: Cache audio TTS hai tầng cho VR-Autism

> Historical brainstorming record. The current English specification is [Two-Layer TTS Audio Cache — Design Specification](2026-10-13-tts-audio-cache-design.md). Use that specification for implementation planning.

Ngày: 12/10/2026

Cập nhật: 13/10/2026

Trạng thái: Tổng hợp trao đổi và quyết định đã duyệt. Các điểm chưa chốt được ghi riêng ở cuối tài liệu.

## 1. Vấn đề và mục tiêu

Dialogue và voice quest thường phát lại các câu có sẵn. Việc tổng hợp TTS nhiều lần cho cùng câu và cùng cấu hình giọng gây chi phí không cần thiết. Nội dung có thể gồm câu chung của lesson và câu giáo viên bổ sung riêng cho từng trẻ; tần suất thay đổi dự kiến thấp.

Thiết kế cần đạt hai mục tiêu:

- Tái sử dụng audio đã tạo qua nhiều lần phát và nhiều buổi học.
- Khi agent lỗi, vẫn phát được các câu đã có audio trong cache.

Đối với câu chưa có audio, nếu agent không phục vụ được thì chấp nhận bỏ qua lượt thoại. Không yêu cầu bảo đảm mọi câu đều phát được khi agent lỗi.

## 2. Bối cảnh và phạm vi

- Agent V2 hiện chạy trên LiveKit Cloud. Nơi chạy trong tương lai chưa được quyết định.
- Cấu hình giọng dùng chung theo profile V2 được lesson sử dụng, không tùy chỉnh theo từng trẻ.
- Có thể chỉnh tốc độ, đổi giọng hoặc thêm profile sau khi thử nghiệm thực tế.
- Cache áp dụng cho các câu thoại cố định trong dialogue và voice quest, gồm câu mở đầu, hint và reminder.
- Voice quest đã có đường hoàn thành dự phòng bằng quest tương tác trong cùng node hoặc giáo viên skip. Thiết kế này giữ nguyên đường hoàn thành đó.

## 3. Hai tầng cache

### Tầng 1: Cache tại VR

VR lưu audio tại thiết bị để tái sử dụng, kể cả qua các lần chạy ứng dụng. Khi có bản audio phù hợp, VR phát trực tiếp tại máy.

Giới hạn dung lượng đã duyệt: **256 MB**. Chính sách dọn cụ thể cần được hoàn thiện khi thiết kế chi tiết.

### Tầng 2: Kho audio bền vững bên ngoài agent

Kho này giữ audio qua các phiên và các lần thay instance agent. VR phải có thể tra cứu và lấy audio đã lưu mà không phụ thuộc agent đang hoạt động.

Đã chọn **Firebase Storage** làm kho audio tầng 2, với quyền truy cập tối giản:

- Bucket giữ riêng tư. Agent ghi audio; VR chỉ đọc.
- VR thuộc một phiên hợp lệ được đọc vùng cache TTS dùng chung.
- Không thêm phân quyền theo từng trẻ, lesson, profile hoặc câu audio. Dữ liệu lesson và snapshot của phiên vẫn quyết định những câu ứng dụng chọn để phát.
- Backend cấp URL tải trong vùng cache TTS, độc lập với agent. Cơ chế xác thực phiên và thời hạn URL cụ thể còn cần thiết kế chi tiết.

Phạm vi nội dung hiện gồm số lượng profile và câu hữu hạn trong một số lesson. Vì vậy, không xây thêm ma trận quyền cho từng file cache.

## 4. Luồng lấy và phát audio

1. **Tầng 1 có audio phù hợp:** VR phát tại máy.
2. **Tầng 1 chưa có, tầng 2 có:** VR tải audio, lưu tại thiết bị và phát.
3. **Cả hai tầng chưa có:** VR gửi lệnh tới agent; agent tổng hợp TTS và stream qua LiveKit như hiện tại.
4. **Trong lúc tổng hợp và stream:** thu lại chính audio TTS của lượt đó để phục vụ ghi cache, không chặn luồng stream.
5. **Audio tổng hợp hoàn chỉnh:** ghi kho cache ở nền. VR có thể lấy bản này về sau để dùng ở tầng 1.
6. **Không lấy hoặc tạo được audio:** bỏ qua lượt thoại theo quy tắc ở mục 7.

Một lượt cache miss chỉ tổng hợp TTS một lần. Không tổng hợp lại cùng câu chỉ để tạo file cache.

**Không dùng luồng tạo xong file, tải về rồi mới phát cho cache miss.** Cache hit phát tại VR; cache miss phát bằng stream LiveKit. Đây là cách chọn nguồn phát cho từng lượt thoại, không phải chế độ dự phòng do giáo viên bật hoặc tắt.

Việc ghi cache không được làm chậm hoặc gián đoạn stream. Audio tổng hợp chưa đầy đủ không được công bố thành bản cache hợp lệ.

## 5. Nhận diện audio và quản lý profile

Mỗi profile có phạm vi cache riêng. Không tái sử dụng chéo giữa hai profile khác nhau, kể cả khi cấu hình của chúng vô tình giống nhau.

Trong một profile, audio được nhận diện từ:

- Câu thực tế cần đọc, sau khi điền thông tin cá nhân hóa nếu có.
- Cấu hình TTS thực tế có ảnh hưởng đến audio.
- Phiên bản định dạng khóa để hỗ trợ thay đổi cách nhận diện về sau.

Sửa nội dung, tốc độ hoặc giọng đọc sẽ tạo khóa khác. Giữ nguyên cấu hình và nội dung thì dùng lại audio đã có. Không cần xóa toàn bộ cache khi tinh chỉnh một profile.

Với cùng câu, cùng profile và các tham số khác giữ nguyên, đổi tốc độ từ `1.0` sang `0.9` rồi trở lại `1.0` tương ứng khóa A, khóa B rồi lại khóa A. Nếu audio A còn trong một trong hai tầng thì dùng lại, không tạo bản thứ ba chỉ vì đã chỉnh cấu hình hai lần. Khóa không chứa số lần chỉnh, ID session hoặc phiên bản profile tăng sau mỗi lần sửa. Cách biểu diễn số phải thống nhất để `1` và `1.0` cùng nhận diện một tốc độ; phiên bản định dạng khóa cũng giữ nguyên trong ví dụ này.

Câu riêng của từng trẻ dùng cùng cơ chế này. Việc quản lý trẻ nào được sử dụng câu nào là trách nhiệm của dữ liệu lesson và hồ sơ; không mặc định tạo namespace cache riêng theo `child-id`.

**Profile được giữ cố định trong một buổi học.** Thay đổi cấu hình áp dụng từ buổi tiếp theo. Phương án đồng bộ đã chốt ở mục 9.5; không thêm bước thương lượng khiến cache miss phải chờ trước khi stream.

## 6. Các mặc định đã duyệt

| Nội dung | Quyết định |
| --- | --- |
| File cache | WAV PCM, mono, 24 kHz |
| Kho audio tầng 2 | Firebase Storage |
| Quyền đọc kho audio | Phiên hợp lệ đọc vùng cache TTS dùng chung; không phân quyền từng file theo trẻ hoặc lesson |
| Cấu hình profile | Giữ cố định trong buổi học; thay đổi áp dụng từ buổi tiếp theo |
| Dung lượng cache VR | Tối đa 256 MB |
| Yêu cầu đồng thời cùng khóa | Gộp trong một process agent |
| Đồng thời giữa các instance | Chấp nhận khả năng tạo trùng hiếm gặp; chưa thêm khóa phân tán |
| Buffer thu PCM | Tối đa 4 MiB mỗi lượt; vượt giới hạn thì bỏ nhánh cache, tiếp tục stream |
| Worker upload | Một worker mỗi process; hàng đợi tối đa 8 buffer, đầy thì bỏ cache của lượt mới |
| Timeout tra/tải cache | Một ngân sách thời gian cho bước tra/tải Firebase Storage; giá trị cụ thể chưa chốt |
| Timeout bắt đầu stream | Chưa thêm trong bản đầu; giữ timeout toàn dialogue theo node hiện có |

Quyết định mới nhất thay thế phương án chờ agent bắt đầu trong 5 giây trước đây: chỉ giới hạn bước tra/tải cache, chưa thêm timer bắt đầu stream. Khi chuyển sang stream, dialogue blocking vẫn dùng timeout toàn node hiện có; mặc định trong code là 30 giây, có thể cấu hình theo node. Thời gian ghi cache ở nền không nằm trong lượt chờ lấy audio.

## 7. Hành vi theo loại thoại và khi agent lỗi

### Dialogue

Dialogue dùng luồng hai tầng cache và stream khi miss. Nếu không có audio và agent không phục vụ được, lượt thoại được bỏ qua. Dialogue đang chờ phải nhận trạng thái kết thúc phù hợp để không treo chờ tín hiệu phát xong.

### Hint do giáo viên yêu cầu

VR chọn câu lần lượt trong danh sách hint của quest, giữ cách luân phiên hiện tại nhưng chuyển quyền chọn từ agent sang VR. Câu đã chọn dùng cùng luồng lấy audio: tra cache local, tra/tải kho ngoài, rồi mới yêu cầu agent stream đúng câu đó khi miss. Khi agent lỗi, câu đã có ở một trong hai tầng vẫn phát được. Nếu không có audio và không tạo được thì bỏ lượt nhắc, không làm voice quest tự hoàn thành.

### Hint tự động theo thời gian im lặng

Agent tiếp tục theo dõi hoạt động lời nói và thời gian im lặng để quyết định lúc nhắc, chọn ngẫu nhiên một câu như hiện tại rồi gửi yêu cầu phát sang VR. VR đưa đúng câu đó qua cùng luồng hai tầng cache và stream khi miss; không tự chọn lại câu khác. Khi agent lỗi thì **không có hint tự động**.

Không bổ sung bộ đếm im lặng, VAD hoặc microphone capture thứ hai tại VR để thay agent. Hint do giáo viên yêu cầu và dialogue vẫn có thể dùng audio đã cache.

### Skip, hủy và kết quả đến muộn

Skip, chuyển node hoặc kết thúc lượt thoại phải ngăn audio cũ phát cho trạng thái mới. Audio tạo xong muộn có thể được giữ trong kho để dùng sau, nhưng không được tự phát cho lượt đã kết thúc.

## 8. Các ràng buộc cần bảo đảm khi triển khai

- Stream và ghi cache dùng cùng đầu ra TTS; ghi cache không tạo backpressure làm gián đoạn phát.
- Chỉ công bố file hoàn chỉnh; bản lỗi hoặc không tương thích được xử lý như cache miss.
- Một lượt thoại chỉ có một nguồn phát, tránh audio tại VR và stream LiveKit phát chồng.
- Trạng thái phát tại VR phải được phản ánh đủ để agent không nhắc tự động trong lúc câu đang được chuẩn bị hoặc phát. Giao thức cụ thể chưa chốt.
- Việc phát hint tại VR phải hoạt động được dù agent lỗi; không bị chặn chỉ vì trạng thái đánh giá voice quest đã thất bại.
- Giữ LiveKit cho microphone, stream khi cache miss, đánh giá lời nói và sự kiện realtime.
- Việc tải file cache và phát tại VR là thay đổi có chủ đích so với quy định trước đây về audio NPC chỉ đi qua LiveKit. Không phục hồi luồng HTTP TTS cũ để tổng hợp lại câu.

## 9. Những điểm còn cần chốt

1. **Cách cấp quyền tải Firebase Storage:** xác thực thiết bị/phiên và thời hạn URL tải. Đã chốt quyền đọc chung cho phiên hợp lệ; không thêm phân quyền theo từng trẻ, lesson hoặc audio.
2. **Thu audio từ luồng TTS:** đã duyệt phương án ở mục 9.1. Các thông số vận hành chưa cố định, như thời gian drain khi job đóng, được cụ thể hóa trong kế hoạch.
3. **Đồng bộ profile:** đã chốt phương án rút gọn ở mục 9.5. Gửi nguyên bộ cấu hình nhỏ trong dữ liệu khởi chạy buổi học; không xây hệ thống phiên bản profile riêng.
4. **Timeout và vòng đời phát:** đã duyệt phạm vi tối giản ở mục 9.3. Còn xác định giá trị và mốc tính ngân sách tra/tải cache; chưa thêm timeout bắt đầu stream.
5. **Luồng hint:** đã duyệt ở mục 9.4. VR chọn hint thủ công; agent chọn và kích hoạt hint tự động. Phần còn lại là chi tiết contract và đồng bộ vòng đời phát khi triển khai.
6. **Dọn và xóa dữ liệu:** phương án tối giản được phân tích ở mục 9.6; chưa được người dùng duyệt.
7. **Đồng bộ contract:** thiết kế giữ các trách nhiệm và quy tắc ở mục 9.7; kế hoạch triển khai sẽ ghi payload, validation và kiểm thử cụ thể cho C#, Python và TypeScript.

### 9.1. Phương án kỹ thuật thu và lưu audio từ TTS streaming

Trạng thái: **Người dùng đã duyệt phương án ngày 13/10/2026, bao gồm giới hạn buffer và hàng đợi bên dưới. Chưa triển khai mã sản phẩm.**

#### Căn cứ lựa chọn

Đã kiểm tra phiên bản trong `uv.lock` và môi trường Python hiện có: `livekit-agents` 1.7.1, `livekit-plugins-google` 1.6.8.

Trong SDK này, `session.say(text)` đi qua `Agent.tts_node` khi không được truyền audio có sẵn. TTS node mặc định dùng `tts.stream()` rồi chuyển các frame PCM vào đường phát của LiveKit.

Google plugin hiện tại tách hai API: `synthesize(text)` dùng lời gọi `synthesize_speech` một lần; `stream()` dùng `streaming_synthesize`. Vì vậy, không gọi thêm `synthesize()` để tạo file và không dùng cách thu đủ audio trước rồi mới gọi `session.say(audio=...)` cho cache miss.

Các nhánh `_synthesize_phrases` hiện tổng hợp trước danh sách quest hoặc một câu hint trước khi phát cũng cần điều chỉnh khi triển khai. Câu cache miss phải đi trực tiếp qua đường streaming được thu audio; không chờ pre-synthesis toàn bộ danh sách.

#### Điểm thu và phạm vi

Đề xuất override `TeacherAgent.tts_node` trong mã ứng dụng. Các lượt thoại cố định được đánh dấu bằng context riêng trước khi gọi `session.say`. Context giữ nội dung đầy đủ, định danh profile, cấu hình hiệu lực và khóa cache dưới dạng snapshot bất biến.

Dùng `ContextVar` riêng của ứng dụng để truyền context vào task của lượt nói, đặt quanh lời gọi đồng bộ `session.say` và reset ngay khi nhận `SpeechHandle`. Mã SDK hiện tại tạo task bằng `asyncio.create_task`, nên context được sao chép vào task phát và task TTS. Không dùng biến toàn cục kiểu "câu đang phát" để gắn audio với metadata.

Lượt có context cache dùng cùng `session.tts.stream()` của pipeline Google hiện tại. TTS node chuyển tiếp đầu vào văn bản ngay, gọi `end_input()` khi đầu vào kết thúc, đọc từng `SynthesizedAudio` và trả nguyên `event.frame` cho pipeline. Việc sở hữu iterator sự kiện ở node này giữ được dấu kết thúc `is_final` của segment, thay vì chỉ quan sát frame ở đầu ra cuối cùng. Lượt không thuộc các câu cố định tiếp tục dùng TTS node mặc định.

Cấu hình thật được áp dụng và chụp tại thời điểm tạo stream; metadata ghi cache dùng snapshot đó. Không đọc lại profile có thể đã thay đổi khi worker upload chạy sau.

#### Thu trong RAM, ghi ở nền

Với mỗi frame, sao chép phần PCM hợp lệ vào buffer của lượt rồi yield nguyên frame. Không await thao tác ghi file, upload hoặc tra kho trong vòng lặp này. Giữ nguyên userdata của frame để tránh làm mất thông tin timing của SDK.

Đề xuất giới hạn buffer **4 MiB cho mỗi lượt**. Nếu vượt giới hạn hoặc nhánh thu gặp lỗi, bỏ việc cache của lượt đó và tiếp tục stream. Có thêm chi phí sao chép RAM, nên cần đo độ trễ; không cam kết overhead bằng không.

Khi tổng hợp kết thúc hợp lệ, đưa buffer hoàn chỉnh vào hàng đợi bằng thao tác không chờ. Đề xuất hàng đợi tối đa **8 buffer**, một worker ghi/upload cho mỗi process. Hàng đợi đầy thì bỏ cache của lượt mới, không chặn phát và không tổng hợp lại để bù cache.

Worker đóng gói WAV PCM 16-bit, mono, 24 kHz từ dữ liệu frame đã thu. Việc tạo WAV và upload bằng thư viện Storage đồng bộ chạy qua `asyncio.to_thread` để không chặn event loop. Có thể đóng gói trong RAM, không cần ghi file tạm lên ổ đĩa.

Object lưu dưới khóa bất biến, ví dụ `tts-cache/v1/<profile-id>/<cache-key>.wav`, kèm metadata định dạng, số sample, thời lượng và checksum. Upload dùng điều kiện `if_generation_match=0`: chỉ tạo nếu object chưa tồn tại. Nếu instance khác đã ghi bản hợp lệ cùng khóa, giữ bản có sẵn thay vì ghi đè.

#### Tiêu chí hoàn chỉnh và xử lý hủy/lỗi

Chỉ giao cho worker khi đầu vào đã được chuyển tiếp đầy đủ, stream kết thúc hợp lệ với dấu final của segment mong đợi, không có lỗi hoặc hủy trước khi hoàn tất, và có PCM phù hợp định dạng đã chốt.

Không lấy `wait_for_playout()` hoặc `SPEAK_SCRIPT_DONE` làm bằng chứng audio đã được tổng hợp đủ. Tổng hợp và phát là hai tiến trình có thể kết thúc ở thời điểm khác nhau.

- **Hủy, timeout hoặc lỗi khi đang tổng hợp:** bỏ buffer chưa hoàn chỉnh, không upload và không phát thông báo cache sẵn sàng. Không giữ TTS chạy tiếp chỉ để lấp cache sau khi lượt đã bị hủy.
- **Đã tổng hợp hoàn chỉnh, sau đó người nghe ngắt phát:** bản audio đầy đủ vẫn được phép ghi cache. Giữ nguyên trạng thái hủy của lượt phát.
- **Ghi/upload lỗi:** lỗi chỉ thuộc nhánh cache, không sửa kết quả phát và không gọi TTS lần hai. Lượt sau có thể miss và tổng hợp lại bình thường.
- **Job đóng:** đề xuất cho worker một khoảng drain hữu hạn để hoàn tất công việc đã nhận, sau đó đóng. Giá trị drain cần chốt khi triển khai. Process bị dừng đột ngột có thể mất buffer; thiết kế cache không bảo đảm phục hồi buffer chưa upload.

Không chỉ kiểm tra "iterator đã hết" để kết luận thành công. SDK được kiểm tra có nhánh đóng nhẹ khi nhận lỗi API 499; nhánh thu cần kiểm tra dấu final và trạng thái hủy để tránh lưu audio bị cắt.

#### Khi nào VR biết cache sẵn sàng

Chỉ sau khi Storage xác nhận object hoàn chỉnh mới phát DataPacket riêng, đề xuất tên `AUDIO_CACHE_READY`. Thông tin tối thiểu gồm `cache_key`, `object_path` và checksum. Không yêu cầu trường phiên bản profile riêng: cache key đã nhận diện câu và cấu hình TTS trong phạm vi profile. Không gửi PCM, WAV hoặc base64 qua DataPacket; không dùng thông báo này để thay tín hiệu hoàn tất dialogue.

VR nhận thông báo thì lên lịch lấy file qua backend cấp quyền tải, lưu vào bản tạm trong cache thiết bị, kiểm tra định dạng/checksum rồi công bố bản local hoàn chỉnh. Không tự phát audio chỉ vì nhận `AUDIO_CACHE_READY`, kể cả khi node đã đổi hoặc lượt stream vẫn đang phát. Có thể trì hoãn tải nền đến khi lượt đang phát kết thúc để giảm cạnh tranh tài nguyên.

Thông báo là cơ chế giúp VR lưu sớm, không phải nguồn xác định duy nhất. Nếu packet bị mất hoặc agent rời room sau upload, lượt tra kho tiếp theo vẫn tìm thấy object theo khóa. Không cần thêm bảng trạng thái `pending/ready` trong Firestore hoặc polling định kỳ.

Cloud Storage bảo đảm upload object đơn lẻ có tính nguyên tử: object chỉ hiện ra sau khi upload hoàn chỉnh. Cache local cũng cần quy tắc công bố tương tự để không đọc file đang tải dở.

#### Kiểm chứng tập trung khi triển khai

- Chặn worker upload trong test, xác nhận frame vẫn được chuyển tới đường phát ngay.
- Hủy giữa luồng hoặc kết thúc thiếu dấu final: không có object và thông báo ready.
- Tổng hợp đủ rồi ngắt playout: object vẫn hợp lệ, trạng thái dialogue vẫn cancelled.
- Upload lỗi, hàng đợi đầy hoặc buffer vượt giới hạn: stream tiếp tục, không gọi TTS thêm.
- Hai writer cùng khóa: không ghi đè object đã công bố; chấp nhận chi phí tổng hợp trùng như quyết định hiện có.
- Mất thông báo ready: lần tra kho tiếp theo vẫn hit; nhận ready không làm phát trùng.

Nguồn đối chiếu:

- [LiveKit pipeline nodes và TTS node](https://docs.livekit.io/agents/logic/nodes/).
- [LiveKit audio customization và caching](https://docs.livekit.io/agents/multimodality/audio/customization/).
- [Cloud Storage: consistency và atomic upload](https://docs.cloud.google.com/storage/docs/consistency).
- [Cloud Storage: điều kiện generation-match](https://docs.cloud.google.com/storage/docs/request-preconditions).
- [Python: asyncio.to_thread](https://docs.python.org/3/library/asyncio-task.html#asyncio.to_thread).
- Mã SDK và Google plugin đã cài trong `LiveKitAgent/.venv`, đối chiếu với phiên bản khóa trong `LiveKitAgent/uv.lock`; chỉ đọc, không chỉnh vendor.

### 9.2. Vấn đề hiện có, cách xử lý và thứ tự triển khai

Trạng thái: **Đã khảo sát code và chạy probe cục bộ. Người dùng đã duyệt phương án và thứ tự xử lý ngày 13/10/2026; chưa sửa mã sản phẩm.**

Đề xuất đưa việc sửa vòng đời phát vào **bước đầu tiên của kế hoạch TTS cache**, kiểm chứng xong bước này rồi mới thêm cache. Không triển khai cache lên đường phát đang có lỗi hủy; cũng không cần mở một dự án sửa toàn bộ audio riêng.

#### Vấn đề 1: Lệnh hủy có thể chỉ đổi trạng thái, chưa dừng câu đang phát

Ví dụ, Unity kết thúc lượt dialogue và gửi cancel. Agent có thể báo CANCELLED nhưng chưa dừng handle đang chạy. Trong các tình huống không có một thao tác interrupt toàn session khác, câu cũ có thể tiếp tục phát.

Nguyên nhân đã xác định: code agent chờ `await session.say(...)` trước khi lưu handle phát. Với SDK đang cài, lời gọi này chờ tới khi lượt nói kết thúc. Vì vậy, handler cancel chưa có handle để dừng đúng lúc.

**Phương án:** nhận và lưu handle ngay khi gọi `session.say`, sau đó mới chờ phát xong. Handler cancel dừng đúng handle của lượt đó. Kiểm tra lượt còn hiệu lực khi chuẩn bị phát và khi xử lý kết quả; không cho kết quả muộn hoàn tất lại lượt đã hủy.

**Thời điểm:** sửa và test trước khi thêm phần thu PCM hoặc phát cache tại VR. Đây là sửa lỗi luồng hiện tại, không phụ thuộc lựa chọn timeout 5 giây.

#### Vấn đề 2: Kết quả lỗi từ SDK chưa được kiểm tra đầy đủ

SDK ghi rõ await `SpeechHandle` không tự ném lỗi generation; lỗi được lấy qua `handle.exception()`. Handler hiện chưa kiểm tra giá trị này trước khi báo SUCCESS. Vì vậy, có nguy cơ một lượt đã kết thúc vì lỗi nội bộ vẫn bị báo thành công. Đây là thiếu sót trong kiểm tra kết quả; cần test lỗi TTS để xác nhận từng đường cụ thể.

**Phương án:** sau khi handle kết thúc, phân biệt rõ ba trường hợp: bị hủy thì CANCELLED, có lỗi thì FAILED, hoàn tất bình thường thì SUCCESS. Thêm test cho lỗi provider, hủy đang phát và kết quả đến muộn.

**Thời điểm:** thực hiện cùng bước sửa vòng đời phát ở trên. Nhờ đó, phần cache sau này có thể dựa vào kết quả tổng hợp và phát đã được xử lý đúng.

#### Phần còn thiếu cho thiết kế mới: chưa biết chính xác một câu bắt đầu phát tại VR

Unity hiện có timeout chờ toàn bộ dialogue kết thúc, mặc định 30 giây và tùy chỉnh theo node. Nó bắt đầu sau `SpeakAsync`, bao gồm cả chờ agent, tổng hợp và phát. Đây là hành vi hiện tại, **không tự thân là lỗi cần đổi sang 5 giây**.

Contract hiện báo kết quả kết thúc câu, chưa báo bắt đầu audio theo từng yêu cầu. Đăng ký audio track hoặc trạng thái PLAYING của command không chứng minh VR đã bắt đầu nghe câu đó.

Nếu sau này cần timeout chờ stream bắt đầu, phải xác định thêm tín hiệu và mốc tính. Quyết định tối giản mới nhất ở mục 9.3 chưa bổ sung cơ chế này; giữ timeout toàn dialogue hiện tại và chỉ giới hạn bước tra/tải cache.

Kết quả `SPEAK_SCRIPT_DONE` muộn đã được lọc theo lượt. Nhưng router RTC chưa lọc audio theo lượt tương tự; cancel tại Unity chưa tự chặn audio cũ. Khi tích hợp cache, phải kiểm chứng đổi nguồn phát, packet hủy chậm và buffer âm thanh, để tránh stream cũ phát chồng với clip local. Chỉ sửa handle bên agent không bảo đảm mọi trường hợp mạng chậm đều hết audio muộn.

#### Thứ tự đề xuất trong một kế hoạch

1. **Sửa và kiểm chứng luồng phát hiện tại:** lưu handle đúng thời điểm, hủy đúng lượt, kiểm tra lỗi SDK và báo kết quả đúng. Thực hiện thành một thay đổi riêng, trước phần cache.
2. **Thêm thu PCM và ghi Firebase Storage ở nền:** giữ streaming như hiện tại; hủy khi chưa tổng hợp xong thì bỏ bản thu dở.
3. **Thêm phát cache tại VR:** thống nhất hoàn tất/hủy giữa clip local và stream; kiểm chứng audio và kết quả muộn không tác động vào lượt mới.
4. **Đánh giá timeout bắt đầu stream sau khi có số liệu:** đây là khả năng bổ sung về sau, không phải bước bắt buộc của bản đầu.

Không cần thay ngay toàn bộ timeout, xây watchdog cho mọi track hoặc bổ sung VAD tại Unity để xử lý các vấn đề trên.

#### Bằng chứng và giới hạn khảo sát

- Kiểm tra lại ngày 13/10/2026: agent vẫn có thứ tự await rồi mới lưu handle.
- Probe với handle thật, session/room giả: trước cancel chưa có runtime handle; sau cancel command đã CANCELLED nhưng handle chưa interrupted và chưa done. Không gọi dịch vụ TTS trong probe.
- Test Python hiện tại dùng `AsyncMock` cho `session.say`, trả handle ngay sau await; khác với SDK thật nên chưa bao phủ lỗi này.
- Một số đường reset toàn session gọi `session.interrupt()` riêng và có thể dừng tiếng; không kết luận mọi lần skip đều tiếp tục phát.
- SDK có timeout provider mặc định 10 giây và tối đa 3 retry. Đây là timeout gọi TTS, không phải xác nhận VR đã nhận audio. Chưa thay cấu hình này.
- Chưa chạy Unity hoặc tái hiện trên headset. Kiểm chứng âm thanh thực tế sau sửa vẫn cần một lượt test tập trung.

### 9.3. Phương án timeout và vòng đời tối giản đã duyệt

Trạng thái: **Người dùng đã duyệt phương án ngày 13/10/2026. Chưa triển khai mã sản phẩm.**

1. VR giữ một lượt thoại cố định đang xử lý, với ID hiện có (`activation_id`/`sequence_id` cho dialogue hoặc `command_id` cho hint), `cache_key` và nguồn phát được chọn. Cache key xác định audio, ID xác định lượt; không thêm hệ thống ID mới.
2. Audio local hợp lệ thì phát. Khi cần Firebase Storage, đặt một ngân sách thời gian cho cả bước tra và tải; giá trị cụ thể còn cần chốt. Miss, lỗi hoặc quá hạn thì chuyển sang agent stream như hiện tại.
3. Khi đã chọn stream, không chuyển sang clip giữa câu. File tải xong muộn chỉ có thể được lưu để dùng sau, không tự phát hoặc đổi kết quả lượt hiện tại. Mỗi lượt có một nguồn phát.
4. Skip, chuyển node và kết thúc lượt phải hủy đúng nguồn: dừng clip nếu đang phát local; gửi cancel đúng lượt nếu đang stream. Sửa việc giữ SpeechHandle và kiểm tra lỗi SDK trước khi tích hợp cache. Kết quả tới sau khi lượt kết thúc bị bỏ qua.
5. Với streaming của dialogue blocking, giữ timeout toàn node hiện có. Chưa thêm timeout bắt đầu stream, packet báo bắt đầu hoặc cơ chế suy ra câu đang phát từ track RTC. Các giới hạn provider hiện có cũng được giữ trong bản đầu.

Đánh đổi đã được chấp nhận: khi cache miss và agent bị treo, dialogue có thể chờ tới timeout toàn node. Sau khi có số liệu thực tế, mới đánh giá nhu cầu bổ sung timeout bắt đầu stream. Phương án này không bổ sung timeout hoàn tất riêng cho hint.

### 9.4. Luồng hint đã duyệt

Trạng thái: **Người dùng đã duyệt phương án ngày 13/10/2026. Chưa triển khai mã sản phẩm.**

Luồng hiện tại phù hợp khi agent chọn câu và phát toàn bộ audio. Cache tại VR cần bổ sung quyền chọn hint thủ công và thông tin vòng đời phát local, không thay đổi mục đích hoặc quy tắc nhắc.

1. **Hint do giáo viên yêu cầu:** VR chọn câu tuần tự từ danh sách câu đã có của quest. Mỗi yêu cầu mới chọn một câu; cùng một lượt được gửi lại không chọn thêm câu. VR tra hai tầng cache trước, miss mới yêu cầu agent stream đúng câu đã chọn. Không cần chờ agent chọn câu, nên audio cache vẫn dùng được khi agent lỗi.
2. **Hint tự động:** agent giữ bộ đếm im lặng và cách chọn câu ngẫu nhiên hiện tại. Khi đến lúc nhắc, agent gửi yêu cầu phát có câu đã chọn sang VR thay vì tự phát ngay. VR xử lý cache hoặc yêu cầu stream đúng câu đó; không tính im lặng hay chọn lại câu tại thiết bị.
3. **Đồng bộ bộ đếm:** VR phản ánh trạng thái đang chuẩn bị/phát để agent tạm ngưng nhắc tự động, và kết thúc, lỗi hoặc hủy để agent tiếp tục tính im lặng khi activation còn hiệu lực. Áp dụng cho các câu local có thể trùng thời gian với voice quest, không chỉ hint tự động. Không cần xác nhận chính xác frame đầu tiên của stream hoặc thêm timeout bắt đầu stream.
4. **Nhận diện và hủy:** sử dụng ID lượt và activation hiện có để liên kết yêu cầu, trạng thái và thao tác hủy. Skip hoặc chuyển node dừng đúng nguồn phát; kết quả muộn không đổi trạng thái lượt mới. Hint tự động vẫn cần giữ hành vi ngắt khi người dùng bắt đầu nói và chỉ ghi nhận lượt nhắc đã phát hợp lệ.
5. **Agent lỗi:** không có nhắc tự động. Việc thông báo trạng thái tới agent không được trở thành điều kiện chặn hint thủ công đã có audio cache.

Tên packet, payload, đồng bộ thứ tự chọn câu, xử lý trạng thái bị mất khi mất kết nối và xác nhận hoàn tất cho telemetry được cụ thể hóa trong phần đồng bộ contract. Đây là chi tiết triển khai của phương án đã duyệt, không phải yêu cầu thêm một cơ chế nhắc mới.

### 9.5. Phương án đồng bộ profile tối giản

Trạng thái: **Người dùng đã chọn phương án rút gọn ngày 13/10/2026. Chưa triển khai mã sản phẩm.**

VR đã nạp danh sách câu Voice Quest dành cho lesson và trẻ trong buổi học. Hiện cấu hình giọng và ánh xạ NPC nằm trong mã Python. Phần cần bổ sung là đưa cùng một bộ cấu hình giọng cho VR và agent, để hai bên nhận diện đúng audio.

1. **Một nơi lưu cấu hình:** lưu danh sách profile và ánh xạ NPC trong Firestore. Bộ cấu hình nhỏ gồm giọng, tốc độ và các tham số TTS hiệu lực cần để tạo khóa đúng. Không duy trì cấu hình riêng tại mỗi stack.
2. **Gửi nguyên bộ cấu hình khi mở buổi học:** backend đọc một lần, giữ bản đã đọc trong dữ liệu của buổi học và cấp cùng nội dung cho VR và agent. Không chỉ gửi ID rồi yêu cầu mỗi bên tự tra một phiên bản ở nơi khác.
3. **Giữ nguyên suốt buổi:** chỉnh cấu hình chung chỉ áp dụng cho buổi sau. Agent khởi động lại lấy bản cấu hình đã giữ của buổi đang chạy, không đọc cấu hình chung mới để thay thế. VR nhận dữ liệu này độc lập với agent nên vẫn tra và phát cache khi agent lỗi.
4. **Khóa từ câu và cấu hình đã nhận:** trong phạm vi riêng của từng profile, cache key dùng câu thực tế cần đọc và cấu hình TTS hiệu lực, cùng phiên bản định dạng khóa đã thống nhất ở mục 5. Đổi giọng hoặc tốc độ ở buổi sau tự tạo khóa khác. Sửa một profile không đổi khóa của các profile còn lại.
5. **Kiểm tra tại agent:** khi cache miss, agent dùng đúng cấu hình của buổi học và kiểm tra cache key từ câu nhận được. Sai khóa, không biết profile hoặc không áp dụng được cấu hình thì báo lỗi lượt đó; không âm thầm đổi giọng rồi lưu dưới khóa sai. Không có bước trao đổi thêm về profile trong mỗi lượt phát.

Contract sẽ quy định cách biểu diễn cấu hình và số để C# và Python tính cùng khóa, kèm ví dụ kiểm thử chung. Chỉ dùng các tùy chọn thực sự có hiệu lực; không coi `pitch` hoặc `gender` hiện chưa được áp dụng là tham số thay đổi audio. Giữ nguyên nội dung đã chọn để phát khi tính khóa.

"Bản cấu hình của buổi học" chỉ là dữ liệu đi cùng buổi đó và được giữ để khôi phục khi agent restart. Bản đầu không xây hệ thống công bố profile, lịch sử phiên bản riêng, tham chiếu phiên bản hiện hành, giao diện quản lý hoặc quy trình rollback. Phiên bản định dạng cache key là chi tiết định dạng kỹ thuật, không phải hệ thống quản lý phiên bản profile.

### 9.6. Phương án dọn cache và vòng đời audio tối giản

Trạng thái: **Phân tích và đề xuất theo yêu cầu ngày 13/10/2026; chưa được người dùng duyệt hoặc triển khai.**

#### Tại VR: giới hạn dung lượng, dọn theo lần dùng gần nhất

Đề xuất LRU: khi cần chỗ cho file mới, xóa bản audio ít được dùng gần đây nhất. Không chia quota theo trẻ, lesson hoặc profile; không đặt thời hạn hết hiệu lực cho file chỉ vì lâu không dùng. Cấu hình và nội dung nằm trong khóa đã quyết định bản nào phù hợp.

Giới hạn 256 MB bao gồm file audio cache, metadata và file tải tạm. Chỉ bắt đầu lưu khi đã giữ đủ chỗ theo kích thước hợp lệ của file; giới hạn lượt tải ghi xuống đĩa để tránh nhiều tác vụ cùng vượt ngân sách. File đang được đọc/phát hoặc ghi được giữ khỏi LRU trong suốt thao tác. Không giữ toàn bộ audio của lesson khỏi dọn và không bắt buộc tải trước tất cả câu.

Nếu không thể giữ đủ chỗ vì các file đang được dùng, bỏ việc lưu bền vững của lượt mới. Nếu đã lấy được audio hợp lệ trong giới hạn RAM cho phép, vẫn có thể phát mà không lưu; nếu không lấy được thì dùng nhánh miss/stream hiện có. Lỗi lưu hoặc dọn cache không được biến thành yêu cầu tổng hợp TTS lần hai cho một audio đang có.

File tải được ghi dưới tên tạm, kiểm tra độ dài, định dạng và checksum rồi đổi tên trong cùng thư mục để công bố bản hoàn chỉnh. File tạm không phải cache hit. Khi khởi động, dọn file tạm bỏ dở và dựng lại index nếu metadata thiếu/hỏng; file hoàn chỉnh không xác minh được là miss. Index dùng một file nhỏ có thể dựng lại, không cần SQLite hay cơ sở dữ liệu thứ hai. Mọi thao tác dọn chỉ được thực hiện trong thư mục cache TTS của ứng dụng.

#### Trên cloud: giữ audio trong bản đầu

Không tự xóa audio cloud theo tuổi, lần dùng hoặc lúc kết thúc phiên. Phạm vi profile/câu hiện nhỏ; xóa theo tuổi có thể loại đúng câu hiếm dùng cần phát khi agent lỗi. LRU chỉ chạy tại thiết bị. Bản đầu không ghi `last_access` lên Firestore, đếm tham chiếu từ lesson/trẻ hoặc tạo job garbage collection.

Object audio vẫn bất biến như mục 9.1: cùng khóa chỉ tạo nếu chưa có. Việc bỏ một câu khỏi dữ liệu lesson hoặc hồ sơ chỉ ngừng chọn câu đó, không tự xóa file cloud hay bản local có thể đang được dùng nơi khác. Chỉnh giọng tạo khóa mới, bản cũ tiếp tục phục vụ buổi đang dùng cấu hình cũ.

Xóa audio cloud thật sự là thao tác bảo trì có chủ đích, tách khỏi xóa câu trong lesson. Bản đầu không bổ sung giao diện xóa, API xóa từ VR hoặc quyền xóa cho worker agent. Nếu cần bảo trì, kiểm tra phạm vi và bản cấu hình/phiên liên quan trước; xóa có điều kiện theo generation đã đọc để không xóa nhầm object vừa được tạo lại. Xóa cloud không đồng nghĩa các bản đã tải trên VR đã được thu hồi; thiết kế này không cung cấp cơ chế xóa đồng loạt các thiết bị.

Audio bị mất trên cloud hoặc local được xử lý như cache miss. File đang tải không hợp lệ không được công bố. Không khẳng định các thiết bị luôn có cùng tập file: tính nhất quán cần bảo đảm ở đây là cùng khóa nhận diện đúng cùng cấu hình và câu, và chỉ sử dụng bản audio hoàn chỉnh.

Căn cứ kỹ thuật: Cloud Storage công bố upload object đơn lẻ một cách nguyên tử và cung cấp read-after-write consistency; precondition cho phép tạo nếu chưa có và xóa đúng generation. Lifecycle theo tuổi chỉ biết điều kiện đã cấu hình, không biết câu nào còn cần trong buổi học. Vì vậy đề xuất không bật tự xóa theo tuổi cho vùng audio TTS trong bản đầu.

Nguồn đối chiếu: [Cloud Storage consistency](https://docs.cloud.google.com/storage/docs/consistency), [request preconditions](https://docs.cloud.google.com/storage/docs/request-preconditions), [object lifecycle management](https://docs.cloud.google.com/storage/docs/lifecycle).

### 9.7. Mức chi tiết contract trong thiết kế và kế hoạch

Thiết kế cần giữ rõ trách nhiệm đã thống nhất: VR chọn hint thủ công; agent kích hoạt và chọn hint tự động; VR tra cache; agent stream đúng câu/profile khi miss và ghi cache nền; agent nhận vòng đời phát local để quản lý im lặng. Cache key nhận diện asset; ID lượt và activation nhận diện thao tác. Kết quả muộn, hủy và nguồn phát phải tuân thủ các mục 9.3–9.4.

Kế hoạch triển khai sẽ cụ thể hóa tên event, trường bắt buộc, kiểu dữ liệu, schema version, validation, trạng thái kết thúc, lỗi không tương thích, thứ tự gửi/nhận và xử lý reconnect. Ghi đầy đủ contract trước các task viết handler, kèm ví dụ JSON và fixtures kiểm thử chung. Xác định những model C#, Python, TypeScript và metadata phiên cần sửa cùng nhau; không để từng task tự đặt tên hoặc diễn giải trạng thái khác nhau.

Nếu phân tích chi tiết contract khi lập kế hoạch làm thay đổi hành vi đã duyệt, cập nhật thiết kế và nêu rõ thay đổi đó trước khi thực hiện. Các mục 9.1–9.5 đã chốt; mục 9.6 còn là đề xuất. Chưa triển khai mã sản phẩm.

## 10. Kiểm chứng dự kiến

- Cùng câu và profile đã cache không gọi TTS thêm khi phát lại hoặc vào buổi học mới.
- Cache miss bắt đầu stream mà không đợi ghi xong file.
- Lỗi ghi cache không làm gián đoạn stream.
- Agent lỗi vẫn phát được dialogue và hint được yêu cầu nếu audio đã có.
- Tra/tải cache lỗi hoặc quá hạn thì chuyển sang stream; cache tải xong muộn không phát chồng với stream.
- Khi lượt đã timeout hoặc bị hủy, kết quả muộn không làm hoàn tất lại lượt hoặc phát nhầm.
- Không có hint tự động khi agent lỗi.
- Hint thủ công chọn câu tuần tự tại VR; gửi lại cùng lượt không chọn thêm câu. Cache miss stream đúng câu đã chọn.
- Hint tự động dùng câu agent đã chọn và đi qua cache trước; không có lần phát thứ hai trực tiếp từ agent.
- Agent không nhắc tự động khi VR đang chuẩn bị/phát câu local; kết thúc hoặc hủy đúng lượt cho phép tính im lặng trở lại.
- Người dùng bắt đầu nói hoặc activation kết thúc phải hủy hint tự động đang chuẩn bị/phát; thông báo muộn không tác động vào activation mới.
- Chỉnh giọng hoặc tốc độ dùng đúng bản audio mới từ buổi tiếp theo.
- C# và Python tạo cùng khóa với cùng câu/profile; sửa profile của một NPC không đổi khóa của NPC khác.
- Đổi tốc độ `1.0`, `0.9`, rồi `1.0` cho cùng câu/profile quay lại khóa đầu và dùng lại audio nếu còn; số `1` và `1.0` cho cùng khóa.
- Buổi đang chạy và agent khởi động lại vẫn dùng nguyên bộ cấu hình đi cùng dữ liệu buổi học; chỉnh cấu hình chung chỉ tác động buổi sau. Không âm thầm fallback giọng khi cấu hình không tương thích.
- LRU giữ file đang đọc/phát/ghi, tính cả tải tạm trong giới hạn 256 MB và không dọn ngoài thư mục cache TTS.
- File tải dở, checksum sai hoặc index hỏng không trở thành cache hit; hết chỗ không làm gián đoạn audio đã có.
- Skip, hủy, chuyển node và kết thúc phiên không để audio hoặc kết quả cũ tác động vào lượt mới.

Tài liệu này ghi nhận brainstorm và các quyết định đã duyệt; những mục còn mở cần được hoàn thiện trước khi lập kế hoạch triển khai.
