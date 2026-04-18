Mỗi thể loại lesson sẽ có manager khác nhau:
- Thể loại tương tác: ActionManager, QuestController để trẻ hoàn thành từng Quest.cs
- Thể loại giải đố: QuizController, data là QuizConfig, QuizQuestionData
- Thể loại khám phá: AnimalManager

Dùng chung:
- TimeManager: Quản lý thời gian
- FirebaseManager: Quản lý kết nối Firebase, lưu Session data sau khi kết thúc
- RealtimeDBManager: Quản lý kết nối Realtime Database, cầu nối với Web
- SessionContext: Lưu thông tin lesson hiện tại đang học
