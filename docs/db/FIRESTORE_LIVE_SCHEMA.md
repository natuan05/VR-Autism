# 🌲 Cấu trúc Chi Tiết Cây Dữ Liệu Cloud Firestore (Deep Schema Snapshot)

> Quét tự động từ Project: `vra-project-96d9c` vào lúc: 23:23:01 2/9/2026

## 📌 Kiến trúc Tổng Quan

- **Mô hình**: Flat Top-Level Collections (Tất cả 12 collections nằm ở cấp gốc để tối ưu truy vấn chéo).
- **Tầng lồng dữ liệu (Nesting Layer)**: Nằm ở các **Embedded Maps & Arrays** bên trong Document (như `quick_phrases`, `default_lesson_params`, `quests`, `quest_logs`, `auto_alerts`).

---

## 📁 Collection: `ai_recommendations` (2 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `childId` | `string` | Giá trị đơn (`XrtGTcnPz4yZPFwUKBiE`) |
| `model` | `string` | Giá trị đơn (`gemini-2.5-flash`) |
| `generatedAt` | `string` | Giá trị đơn (`2026-09-01T09:00:15.889Z`) |
| `generatedBy` | `string` | Giá trị đơn (`NzIspIBjNtRdH0l92IQ1rUyft272`) |
| `basedOnSessionIds` | `Array<string> (3 items)` | Giá trị đơn (`3a4f4a87-018f-4969-860a-a9c370d30a85,81a5965d-07db-4c25-9e53-57622d5b0a71,01906fd9-76e5-47dc-b24d-46613972c788`) |
| `status` | `string` | Giá trị đơn (`draft`) |
| `insufficientData` | `boolean` | Giá trị đơn (`false`) |
| `summary` | `string` | Giá trị đơn (`Trẻ đã hoàn thành bài học 'Khá...`) |
| `recommendations` | `Array<Object> (4 items)` | Danh sách Object con (gồm các key: `lessonId`, `lessonTitle`, `levelName`, `type`, `targetSkill`, `priority`, `confidence`, `reason`, `expectedBenefit`, `specialistNotes`, `thumbnailUrl`, `sceneName`, `difficultyLevel`) |
| `isDemo` | `boolean` | Giá trị đơn (`false`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "childId": "XrtGTcnPz4yZPFwUKBiE",
  "model": "gemini-2.5-flash",
  "generatedAt": "2026-09-01T09:00:15.889Z",
  "generatedBy": "NzIspIBjNtRdH0l92IQ1rUyft272",
  "basedOnSessionIds": [
    "3a4f4a87-018f-4969-860a-a9c370d30a85",
    "81a5965d-07db-4c25-9e53-57622d5b0a71",
    "01906fd9-76e5-47dc-b24d-46613972c788"
  ],
  "status": "draft",
  "insufficientData": false,
  "summary": "Trẻ đã hoàn thành bài học 'Khám phá động vật nông trại' một cách thành công. Tuy nhiên, trong hai buổi học gần nhất về 'Bài học rửa tay', trẻ liên tục có dấu hiệu lắc đầu mạnh (stimming) và xao nhãng mức độ cao, mặc dù điểm số cuối cùng vẫn đạt tối đa. Mục tiêu chính hiện tại của trẻ là cải thiện kỹ năng giao tiếp.",
  "recommendations": [
    {
      "lessonId": "Greet_1",
      "lessonTitle": "Làm quen bạn mới",
      "levelName": "Làm quen bạn mới",
      "type": "theoretical",
      "targetSkill": "Giao tiếp xã hội, Bắt đầu cuộc hội thoại",
      "priority": "high",
      "confidence": 0.95,
      "reason": "Bài học này trực tiếp đáp ứng mục tiêu 'Bài học Giao tiếp' của trẻ, với mức độ hiện tại là 1/5. Việc thực hành làm quen và bắt đầu cuộc hội thoại là nền tảng quan trọng để phát triển kỹ năng giao tiếp xã hội.",
      "expectedBenefit": "Giúp trẻ tự tin hơn khi gặp gỡ người mới, rèn luyện kỹ năng luân phiên lượt lời và các mẫu câu giao tiếp cơ bản, từ đó tăng cường khả năng giao tiếp của trẻ.",
      "specialistNotes": "Mặc dù là bài học mức độ Trung bình, nhưng đây là kỹ năng mục tiêu cốt lõi. Cần quan sát chặt chẽ phản ứng của trẻ trong quá trình tương tác, đặc biệt là khi trẻ phải phát âm qua micro. Trị liệu viên có thể sử dụng gợi ý (hints) để hỗ trợ nếu trẻ gặp khó khăn, và đảm bảo thời lượng phù hợp với khả năng chú ý của trẻ (15 phút).",
      "thumbnailUrl": "https://firebasestorage.googleapis.com/v0/b/vra-project-96d9c.firebasestorage.app/o/lesson_image%2FHelloFriend.png?alt=media&token=6af5cb2a-21ba-49bd-ae19-e35a4a234b0f",
      "sceneName": "HelloFriend",
      "difficultyLevel": "Trung bình"
    },
    {
      "lessonId": "Farm_Quiz_1",
      "lessonTitle": "Quiz khám phá nông trại",
      "levelName": "Quiz",
      "type": "practical",
      "targetSkill": "Tiếp nhận ngôn ngữ, Nhận biết âm thanh, Trí nhớ dài hạn, Ra quyết định",
      "priority": "medium",
      "confidence": 0.9,
      "reason": "Trẻ đã hoàn thành xuất sắc bài 'Khám phá động vật nông trại' ở cấp độ Dễ. Việc chuyển sang bài Quiz khám phá nông trại ở mức độ Trung bình là bước tiếp theo hợp lý để củng cố kiến thức đã học, khuyến khích sự tương tác và phát triển kỹ năng ra quyết định.",
      "expectedBenefit": "Giúp trẻ ôn tập và ghi nhớ các loài vật đã học, rèn luyện khả năng nhận biết âm thanh và hình ảnh, đồng thời phát triển kỹ năng đưa ra lựa chọn và phản hồi trong môi trường tương tác.",
      "specialistNotes": "Bài học có sử dụng âm thanh đặt câu hỏi và tiếng kêu lặp lại của con vật. Cần theo dõi phản ứng của trẻ về độ nhạy cảm âm thanh (mức 3), tuy nhiên bài 'Khám phá động vật nông trại' trước đó đã được hoàn thành tốt với âm thanh có kiểm soát. Nếu trẻ có dấu hiệu khó chịu với âm thanh, trị liệu viên cần điều chỉnh âm lượng hoặc dừng bài học kịp thời.",
      "thumbnailUrl": "https://firebasestorage.googleapis.com/v0/b/vra-project-96d9c.firebasestorage.app/o/lesson_image%2Ffarm_quiz.png?alt=media&token=67bfbf7b-3f6b-4fef-9abe-f3b1f0e2aa80",
      "sceneName": "Farm-Quiz",
      "difficultyLevel": "Trung bình"
    },
    {
      "lessonId": "WashingHand_2",
      "lessonTitle": "Bài học rửa tay",
      "levelName": "Chỉ dẫn và không mẫu",
      "type": "theoretical",
      "targetSkill": "Tự phục vụ, Hành động theo bước, Vệ sinh cá nhân",
      "priority": "medium",
      "confidence": 0.8,
      "reason": "Trẻ đã hoàn thành bài 'Bài học rửa tay' cấp độ 'Chỉ dẫn và có mẫu' hai lần liên tiếp với điểm số cao, nhưng đồng thời ghi nhận nhiều cảnh báo lắc đầu mạnh và xao nhãng. Bài 'Chỉ dẫn và không mẫu' này lược bỏ bước báo cáo bằng giọng nói ở cuối, có thể giúp giảm áp lực và yếu tố gây lo âu cho trẻ, từ đó cải thiện sự tập trung và giảm hành vi tự kích thích.",
      "expectedBenefit": "Củng cố chuỗi hành động rửa tay đúng cách mà không có sự phụ thuộc vào hướng dẫn giọng nói. Việc loại bỏ yêu cầu ngôn ngữ có thể giúp trẻ tập trung hơn vào các bước vận động và giảm thiểu các yếu tố gây căng thẳng, từ đó cải thiện sự thoải mái và giảm các hành vi tự kích thích trong quá trình học.",
      "specialistNotes": "Đây là một điều chỉnh nhằm giải quyết các hành vi tự kích thích và xao nhãng đã được ghi nhận trong các buổi học trước. Trị liệu viên cần quan sát cẩn thận xem việc loại bỏ yếu tố ngôn ngữ có giúp trẻ bớt căng thẳng và tập trung hơn không. Nếu trẻ vẫn tiếp tục có dấu hiệu khó chịu, cần xem xét nguyên nhân khác hoặc tạm dừng các bài học tự phục vụ trong môi trường VR.",
      "thumbnailUrl": "https://firebasestorage.googleapis.com/v0/b/vra-project-96d9c.firebasestorage.app/o/lesson_image%2Fbathroom.png?alt=media&token=6febc81d-1f58-4ae9-ac6a-3b03f68ffac3",
      "sceneName": "Bathroom",
      "difficultyLevel": "Trung bình"
    },
    {
      "lessonId": "Intro_1",
      "lessonTitle": "Tập giới thiệu bản thân",
      "levelName": "Chỉ dẫn và có mẫu",
      "type": "theoretical",
      "targetSkill": "Giao tiếp xã hội, Tự nhận thức bản thân, Ngôn ngữ biểu đạt, Giao tiếp có cấu trúc",
      "priority": "low",
      "confidence": 0.75,
      "reason": "Bài học này là tiền đề cho các kỹ năng giao tiếp phức tạp hơn, yêu cầu trẻ thực hành tự giới thiệu bản thân sau khi quan sát một nhân vật mẫu. Đây là bước tiếp theo tự nhiên sau khi trẻ đã làm quen với việc giao tiếp cơ bản thông qua bài 'Làm quen bạn mới' (Làm quen bạn mới_1).",
      "expectedBenefit": "Giúp trẻ rèn luyện khả năng tự giới thiệu một cách rõ ràng và mạch lạc, tăng cường sự tự tin khi nói chuyện trước một nhóm nhỏ, và phát triển kỹ năng ngôn ngữ biểu đạt có cấu trúc.",
      "specialistNotes": "Bài học này có điều kiện tiên quyết là 'Làm quen bạn mới'. Chỉ nên giới thiệu bài học này nếu trẻ đã đạt được sự tiến bộ đáng kể trong bài 'Làm quen bạn mới'. Trị liệu viên cần chuẩn bị để hỗ trợ trẻ trong việc đứng đúng vị trí và hướng dẫn lời nói nếu cần, đồng thời chú ý đến thời lượng để phù hợp với khả năng tập trung của trẻ.",
      "thumbnailUrl": "https://firebasestorage.googleapis.com/v0/b/vra-project-96d9c.firebasestorage.app/o/lesson_image%2Fintro.png?alt=media&token=32544f11-dd86-4a3a-9fbe-496c1a8d9cec",
      "sceneName": "LearnToAsk",
      "difficultyLevel": "Trung bình"
    }
  ],
  "isDemo": false
}
```

---

## 📁 Collection: `center_managers` (2 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `centerId` | `string` | Giá trị đơn (`CT-TQMDC`) |
| `role` | `string` | Giá trị đơn (`center`) |
| `name` | `string` | Giá trị đơn (`Phùng Thanh Độ`) |
| `email` | `string` | Giá trị đơn (`anhdomixi@gmail.com`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-03-31T11:12:49.351Z`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "centerId": "CT-TQMDC",
  "role": "center",
  "name": "Phùng Thanh Độ",
  "email": "anhdomixi@gmail.com",
  "updatedAt": "2026-03-31T11:12:49.351Z"
}
```

---

## 📁 Collection: `centers` (2 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `name` | `string` | Giá trị đơn (`Trung Tâm Ánh Sáng`) |
| `email` | `string` | Giá trị đơn (`light_center@gmail.com`) |
| `address` | `string` | Giá trị đơn (`144 Xuân Thủy, Cầu Giấy, Hà Nộ...`) |
| `phone` | `string` | Giá trị đơn (`0985471236`) |
| `centerId` | `string` | Giá trị đơn (`CT-7B5O5`) |
| `ownerUid` | `string` | Giá trị đơn (`Glm8eoDiJkY8VU20hbASEgimKSF3`) |
| `managerUids` | `Array<string> (1 items)` | Giá trị đơn (`Glm8eoDiJkY8VU20hbASEgimKSF3`) |
| `createdAt` | `string` | Giá trị đơn (`2026-04-04T16:30:53.249Z`) |
| `sessionCount` | `number` | Giá trị đơn (`0`) |
| `expertCount` | `number` | Giá trị đơn (`1`) |
| `totalChildren` | `number` | Giá trị đơn (`2`) |
| `status` | `string` | Giá trị đơn (`Active`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-08-23T10:00:08.061Z`) |
| `ExpertCount` | `number` | Giá trị đơn (`1`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "name": "Trung Tâm Ánh Sáng",
  "email": "light_center@gmail.com",
  "address": "144 Xuân Thủy, Cầu Giấy, Hà Nội",
  "phone": "0985471236",
  "centerId": "CT-7B5O5",
  "ownerUid": "Glm8eoDiJkY8VU20hbASEgimKSF3",
  "managerUids": [
    "Glm8eoDiJkY8VU20hbASEgimKSF3"
  ],
  "createdAt": "2026-04-04T16:30:53.249Z",
  "sessionCount": 0,
  "expertCount": 1,
  "totalChildren": 2,
  "status": "Active",
  "updatedAt": "2026-08-23T10:00:08.061Z"
}
```

---

## 📁 Collection: `child_profiles` (4 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `id` | `string` | Giá trị đơn (`DiF02X6e7A1JmP03fWQo`) |
| `name` | `string` | Giá trị đơn (`Đặng Cao Bồ`) |
| `age` | `number` | Giá trị đơn (`6`) |
| `gender` | `string` | Giá trị đơn (`male`) |
| `condition` | `string` | Giá trị đơn (`ASD - Mức độ 1`) |
| `height_cm` | `number` | Giá trị đơn (`110`) |
| `weight_kg` | `number` | Giá trị đơn (`20`) |
| `sound_sensitivity` | `number` | Giá trị đơn (`3`) |
| `attention_span_min` | `number` | Giá trị đơn (`15`) |
| `anxiety_triggers` | `Array (empty)` | Giá trị đơn (``) |
| `diagnosis_notes` | `string` | Giá trị đơn (``) |
| `centerId` | `string` | Giá trị đơn (`CT-TQMDC`) |
| `linkCode` | `string` | Giá trị đơn (`1MMLCL`) |
| `linkCodeExpires` | `string` | Giá trị đơn (`2026-04-03T14:01:34.593Z`) |
| `linkCodeUsed` | `boolean` | Giá trị đơn (`false`) |
| `createdAt` | `string` | Giá trị đơn (`2026-04-01T14:01:34.593Z`) |
| `expertUids` | `Array<string> (1 items)` | Giá trị đơn (`wCQwZYdgkVQhjv4RvYRPfctJjIT2`) |
| `parentUid` | `string` | Giá trị đơn (`8kvYLnuBNpQVp2KOp83RwxnI7Dn2`) |
| `status` | `string` | Giá trị đơn (`Active`) |
| `expertUid` | `string` | Giá trị đơn (`wCQwZYdgkVQhjv4RvYRPfctJjIT2`) |
| `default_lesson_params` | `Map/Object` | Map lồng nhau (gồm các key: `actions`, `quiz`, `exploration`) |
| `quick_phrases` | `Map/Object` | Map lồng nhau (gồm các key: `general`, `WashingHand_1`, `WashingHand_2`, `Farm_1`, `Farm_Quiz_1...`) |
| `lastSessionAt` | `string` | Giá trị đơn (`15/8/2026`) |
| `sessionCount` | `number` | Giá trị đơn (`5`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-08-15T15:07:45.965Z`) |
| `goals` | `Array<Object> (2 items)` | Danh sách Object con (gồm các key: `id`, `type`, `title`, `targetValue`, `unit`, `currentValue`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "id": "DiF02X6e7A1JmP03fWQo",
  "name": "Đặng Cao Bồ",
  "age": 6,
  "gender": "male",
  "condition": "ASD - Mức độ 1",
  "height_cm": 110,
  "weight_kg": 20,
  "sound_sensitivity": 3,
  "attention_span_min": 15,
  "anxiety_triggers": [],
  "diagnosis_notes": "",
  "centerId": "CT-TQMDC",
  "linkCode": "1MMLCL",
  "linkCodeExpires": "2026-04-03T14:01:34.593Z",
  "linkCodeUsed": false,
  "createdAt": "2026-04-01T14:01:34.593Z",
  "expertUids": [
    "wCQwZYdgkVQhjv4RvYRPfctJjIT2"
  ],
  "parentUid": "8kvYLnuBNpQVp2KOp83RwxnI7Dn2",
  "status": "Active",
  "expertUid": "wCQwZYdgkVQhjv4RvYRPfctJjIT2",
  "default_lesson_params": {
    "actions": {
      "enable_auto_hint": true,
      "enable_visual_guidance": true,
      "enable_bubble_hints": true,
      "speech_silence_timeout": -1,
      "action_reminder_cycle": 10,
      "gaze_cone_angle": 10
    },
    "quiz": {
      "quiz_intro_delay": -1,
      "quiz_sound_gap": -1,
      "quiz_end_delay": -1
    },
    "exploration": {
      "camera_move_speed": 4,
      "sound_to_description_gap": -1
    }
  },
  "quick_phrases": {
    "general": [
      "Con làm tốt lắm!",
      "Tuyệt vời!",
      "Cố lên con!"
    ],
    "WashingHand_1": [
      {
        "quest_name": "Bật vòi nước",
        "phrases": [
          "Con hãy bật vòi nước đi nào.",
          "Mở vòi nước đi con."
        ]
      },
      {
        "quest_name": "Làm ướt tay",
        "phrases": [
          "Hãy cho tay dưới vòi nước để làm ướt nhé.",
          "Làm ướt hai bàn tay đi con."
        ]
      },
      {
        "quest_name": "Xịt xà phòng",
        "phrases": [
          "Con hãy nhấn vòi để lấy xà phòng nhé.",
          "Thoa đều xà phòng khắp bàn tay nào con."
        ]
      },
      {
        "quest_name": "Rửa tay",
        "phrases": [
          "Hãy xoa và rửa sạch xà phòng dưới vòi nước nào.",
          "Kỳ cọ hai bàn tay cho sạch nhé con."
        ]
      },
      {
        "quest_name": "Tắt vòi nước",
        "phrases": [
          "Con nhớ tắt vòi nước sau khi rửa xong nhé.",
          "Khóa vòi nước lại đi con."
        ]
      },
      {
        "quest_name": "Lau tay với khăn",
        "phrases": []
      }
    ],
    "WashingHand_2": [
      {
        "quest_name": "Bật vòi nước",
        "phrases": [
          "Tự bật vòi nước đi con.",
          "Bật vòi nước nào."
        ]
      },
      {
        "quest_name": "Làm ướt tay",
        "phrases": [
          "Con tự làm ướt tay nhé.",
          "Làm ướt tay đi con."
        ]
      },
      {
        "quest_name": "Xịt xà phòng",
        "phrases": [
          "Con tự lấy xà phòng nhé.",
          "Xoa đều xà phòng đi con."
        ]
      },
      {
        "quest_name": "Rửa tay",
        "phrases": [
          "Hãy tự rửa sạch xà phòng đi con.",
          "Rửa sạch tay dưới vòi nước nào."
        ]
      },
      {
        "quest_name": "Tắt vòi nước",
        "phrases": [
          "Tự khóa vòi nước lại nhé con.",
          "Tắt vòi nước đi con."
        ]
      },
      {
        "quest_name": "Lau tay với khăn",
        "phrases": [
          "Con tự lấy khăn lau khô tay nhé.",
          "Lau tay đi con."
        ]
      }
    ],
    "Farm_1": [
      {
        "quest_name": "Explore",
        "phrases": [
          "Con có nhìn thấy con vật nào xung quanh không?",
          "Lắng nghe tiếng kêu của con vật nhé con.",
          "Hãy chỉ vào con vật mà con thích đi con."
        ]
      }
    ],
    "Farm_Quiz_1": [
      {
        "quest_name": "Quiz_Q1",
        "phrases": [
          "Con đoán xem đây là con gì nhé.",
          "Chỉ con vật tương ứng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q2",
        "phrases": [
          "Đây là con vật gì thế con?",
          "Con vật này kêu thế nào nhỉ?"
        ]
      },
      {
        "quest_name": "Quiz_Q3",
        "phrases": [
          "Chỉ con vật đúng đi con.",
          "Nhìn kỹ xem đây là con gì nào."
        ]
      },
      {
        "quest_name": "Quiz_Q4",
        "phrases": [
          "Đố con đây là con vật gì?",
          "Chọn đáp án đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q5",
        "phrases": [
          "Con vật này sống ở đâu thế con?",
          "Chỉ vào con vật đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q6",
        "phrases": [
          "Con đếm xem con bò có mấy chân nhé.",
          "Chọn số chân của con bò đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q7",
        "phrases": [
          "Lông của con bò có màu gì vậy con?",
          "Nhìn kỹ màu lông con bò đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q8",
        "phrases": [
          "Con cừu thích ăn món gì nhất nhỉ?",
          "Chọn thức ăn cho cừu đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q9",
        "phrases": [
          "Con đếm xem có bao nhiêu chú gà con phía trước nhé.",
          "Có mấy con gà con vậy con?"
        ]
      },
      {
        "quest_name": "Quiz_Q10",
        "phrases": [
          "Con hãy đếm số lượng cừu phía trước nhé.",
          "Có mấy con cừu vậy con?"
        ]
      }
    ],
    "Grassland_1": [
      {
        "quest_name": "Explore",
        "phrases": [
          "Con có nhìn thấy sư tử đang ở đâu không?",
          "Nghe tiếng gầm của sư tử nhé.",
          "Chỉ vào con ngựa vằn nào con."
        ]
      }
    ],
    "Grassland_Quiz_1": [
      {
        "quest_name": "Quiz_Q1",
        "phrases": [
          "Con nhìn xem đây là con gì nào.",
          "Chỉ con vật đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q2",
        "phrases": [
          "Đây là con gì thế con?",
          "Chọn con vật đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q3",
        "phrases": [
          "Nhìn xem đây là con gì con nhé.",
          "Chỉ vào con vật đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q4",
        "phrases": [
          "Đố con đây là con gì nào.",
          "Chọn đáp án đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q5",
        "phrases": [
          "Chỉ con vật đúng trên đồng cỏ nhé.",
          "Con vật này tên là gì nhỉ?"
        ]
      },
      {
        "quest_name": "Quiz_Q6",
        "phrases": [
          "Thỏ có đôi tai hay cái đuôi như thế nào nhỉ?",
          "Thỏ có gì đặc biệt thế con?"
        ]
      },
      {
        "quest_name": "Quiz_Q7",
        "phrases": [
          "Tiếng gầm của sư tử nghe thế nào con nhỉ?",
          "Sư tử gầm to hay nhỏ vậy con?"
        ]
      },
      {
        "quest_name": "Quiz_Q8",
        "phrases": [
          "Ngựa vằn có bộ lông sọc màu gì thế con?",
          "Điểm đặc biệt của ngựa vằn là gì nào?"
        ]
      },
      {
        "quest_name": "Quiz_Q9",
        "phrases": [
          "Hươu cao cổ thường ăn lá cây hay ăn thịt con nhỉ?",
          "Hươu cao cổ thích ăn gì nào?"
        ]
      },
      {
        "quest_name": "Quiz_Q10",
        "phrases": [
          "Sư tử thường đi săn mồi vào lúc nào con nhỉ?",
          "Sư tử săn mồi ban ngày hay ban đêm?"
        ]
      }
    ],
    "Ocean_1": [
      {
        "quest_name": "Explore",
        "phrases": [
          "Quan sát các sinh vật biển bơi lội xung quanh nhé con.",
          "Con có thấy con cá heo bơi ở đâu không?",
          "Con rùa biển có màu gì đẹp quá con nhỉ."
        ]
      }
    ],
    "Ocean_Quiz_1": [
      {
        "quest_name": "Quiz_Q1",
        "phrases": [
          "Nhìn xem sinh vật biển này là con gì nhé con.",
          "Chỉ con vật đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q2",
        "phrases": [
          "Đây là con gì thế con?",
          "Chỉ vào con vật dưới biển đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q3",
        "phrases": [
          "Đố con đây là loài cá nào nhé.",
          "Chọn con vật đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q4",
        "phrases": [
          "Con gì bơi lội trước mặt con vậy?",
          "Chọn đáp án đúng đi con."
        ]
      },
      {
        "quest_name": "Quiz_Q5",
        "phrases": [
          "Rùa biển dùng bộ phận nào để tự bảo vệ mình vậy con?",
          "Mai của rùa biển như thế nào con nhỉ?"
        ]
      },
      {
        "quest_name": "Quiz_Q6",
        "phrases": [
          "Hàm răng của cá mập sắc nhọn hay mềm mại thế con?",
          "Răng cá mập như thế nào nhỉ?"
        ]
      },
      {
        "quest_name": "Quiz_Q7",
        "phrases": [
          "Cá heo là loài vật hung dữ hay thân thiện con nhỉ?",
          "Con thấy cá heo thế nào?"
        ]
      },
      {
        "quest_name": "Quiz_Q8",
        "phrases": [
          "Thân của con sứa mềm hay cứng vậy con?",
          "Sứa biển có màu sắc thế nào nhỉ?"
        ]
      },
      {
        "quest_name": "Quiz_Q9",
        "phrases": [
          "If lỡ chạm vào sứa thì da bé sẽ bị ngứa hoặc đau đó con.",
          "Có nên chạm vào sứa biển không con?"
        ]
      },
      {
        "quest_name": "Quiz_Q10",
        "phrases": [
          "Cá heo sống một mình hay thích đi theo bầy đàn con nhỉ?",
          "Cá heo thích bơi cùng nhau không con?"
        ]
      }
    ],
    "Intro_1": [
      {
        "quest_name": "Walk to Stage",
        "phrases": [
          "Con hãy đi lên bục giảng nào.",
          "Bước lên phía trước đi con."
        ]
      },
      {
        "quest_name": "Greet Class",
        "phrases": [
          "Con hãy chào thầy cô và các bạn đi.",
          "Cúi đầu chào mọi người nào con."
        ]
      },
      {
        "quest_name": "Say Name",
        "phrases": [
          "Con hãy tự tin nói to tên mình nhé.",
          "Nói tên của con đi nào."
        ]
      },
      {
        "quest_name": "Say Age & Hobbies",
        "phrases": [
          "Hãy giới thiệu tuổi và sở thích của con đi nào.",
          "Nói cho các bạn biết con thích gì nhé."
        ]
      }
    ],
    "Intro_2": [
      {
        "quest_name": "Walk to Stage",
        "phrases": [
          "Con tự đi lên bục giảng giới thiệu nhé.",
          "Hãy đứng trước lớp học đi con."
        ]
      },
      {
        "quest_name": "Greet Class",
        "phrases": [
          "Hãy tự chào cả lớp nào con.",
          "Chào thầy cô và các bạn đi con."
        ]
      },
      {
        "quest_name": "Say Name",
        "phrases": [
          "Con tự giới thiệu tên mình cho mọi người biết nhé.",
          "Hãy nói to tên con đi."
        ]
      },
      {
        "quest_name": "Say Age & Hobbies",
        "phrases": [
          "Hãy chia sẻ tuổi và sở thích của con với lớp nào.",
          "Nói về những điều con yêu thích nhé."
        ]
      }
    ],
    "Greet_1": [
      {
        "quest_name": "Walk to Friend",
        "phrases": [
          "Con hãy tiến lại gần bạn mới đi nào.",
          "Đi lại gần bạn nào con."
        ]
      },
      {
        "quest_name": "Wave Hand",
        "phrases": [
          "Hãy vẫy tay chào bạn nhé con.",
          "Vẫy tay nào con."
        ]
      },
      {
        "quest_name": "Say Hello",
        "phrases": [
          "Con hãy nói xin chào bạn đi.",
          "Chào bạn đi con."
        ]
      },
      {
        "quest_name": "Ask Name",
        "phrases": [
          "Con thử hỏi tên bạn là gì đi con.",
          "Hỏi tên của bạn đi con."
        ]
      },
      {
        "quest_name": "Shake Hand",
        "phrases": [
          "Con hãy bắt tay bạn thật thân thiện nào.",
          "Bắt tay bạn đi con."
        ]
      }
    ]
  },
  "lastSessionAt": "15/8/2026",
  "sessionCount": 5,
  "updatedAt": "2026-08-15T15:07:45.965Z"
}
```

---

## 📁 Collection: `experts` (2 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `uid` | `string` | Giá trị đơn (`NzIspIBjNtRdH0l92IQ1rUyft272`) |
| `name` | `string` | Giá trị đơn (`Gia Hân`) |
| `email` | `string` | Giá trị đơn (`giahan@gmail.com`) |
| `role` | `string` | Giá trị đơn (`expert`) |
| `centerId` | `string` | Giá trị đơn (`CT-7B5O5`) |
| `specialization` | `string` | Giá trị đơn (`Âm ngữ trị liệu`) |
| `createdAt` | `string` | Giá trị đơn (`2026-04-04T16:31:42.193Z`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-04-04T16:31:42.193Z`) |
| `status` | `string` | Giá trị đơn (`Active`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "uid": "NzIspIBjNtRdH0l92IQ1rUyft272",
  "name": "Gia Hân",
  "email": "giahan@gmail.com",
  "role": "expert",
  "centerId": "CT-7B5O5",
  "specialization": "Âm ngữ trị liệu",
  "createdAt": "2026-04-04T16:31:42.193Z",
  "updatedAt": "2026-04-04T16:31:42.193Z",
  "status": "Active"
}
```

---

## 📁 Collection: `lessons` (11 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `lesson_id` | `string` | Giá trị đơn (`Farm`) |
| `scene_name` | `string` | Giá trị đơn (`Farm`) |
| `lesson_name` | `string` | Giá trị đơn (`Khám phá động vật nông trại`) |
| `level_name` | `string` | Giá trị đơn (`Học lý thuyết`) |
| `lesson_index` | `number` | Giá trị đơn (`4`) |
| `level_index` | `number` | Giá trị đơn (`0`) |
| `type` | `string` | Giá trị đơn (`practical`) |
| `level_id` | `string` | Giá trị đơn (`1`) |
| `min_age` | `number` | Giá trị đơn (`3`) |
| `duration_min` | `number` | Giá trị đơn (`15`) |
| `thumbnail_url` | `string` | Giá trị đơn (`https://firebasestorage.google...`) |
| `description` | `string` | Giá trị đơn (`Một chuyến tham quan nông trại...`) |
| `target_skills` | `Array<string> (4 items)` | Giá trị đơn (`Tập trung chú ý,Nhận biết sự vật,Lắng nghe thụ động`) |
| `difficulty_level` | `string` | Giá trị đơn (`Dễ`) |
| `prerequisites` | `string` | Giá trị đơn (``) |
| `quests` | `Array<Object> (1 items)` | Danh sách Object con (gồm các key: `title`, `default_phrases`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-08-06T11:22:24.902Z`) |
| `scenario` | `string` | Giá trị đơn (`Bài học lý thuyết thụ động — t...`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "lesson_id": "Farm",
  "scene_name": "Farm",
  "lesson_name": "Khám phá động vật nông trại",
  "level_name": "Học lý thuyết",
  "lesson_index": 4,
  "level_index": 0,
  "type": "practical",
  "level_id": "1",
  "min_age": 3,
  "duration_min": 15,
  "thumbnail_url": "https://firebasestorage.googleapis.com/v0/b/vra-project-96d9c.firebasestorage.app/o/lesson_image%2FFarm-cover.png?alt=media&token=63479a63-01e4-4710-8ed5-e89af71ff428",
  "description": "Một chuyến tham quan nông trại thú vị, nơi trẻ làm quen với các con vật quen thuộc như chó, bò, heo và gà. Trẻ sẽ được nghe tiếng kêu và tìm hiểu về cách các con vật này chung sống trong môi trường nông nghiệp.",
  "target_skills": [
    "Tập trung chú ý",
    "Nhận biết sự vật",
    "Lắng nghe thụ động",
    "Xử lý cảm giác âm thanh"
  ],
  "difficulty_level": "Dễ",
  "prerequisites": "",
  "quests": [
    {
      "title": "Khám phá nông trại",
      "default_phrases": [
        "Con có nhìn thấy con vật nào xung quanh không?",
        "Lắng nghe tiếng kêu của con vật nhé con.",
        "Hãy chỉ vào con vật mà con thích đi con."
      ]
    }
  ],
  "updatedAt": "2026-08-06T11:22:24.902Z",
  "scenario": "Bài học lý thuyết thụ động — trẻ KHÔNG cần tương tác, chỉ quan sát và lắng nghe. Camera VR tự động bay đến từng con vật theo thứ tự cố định: (1) Cừu (Sheep): camera bay đến chuồng cừu, hệ thống phát âm thanh mô tả \"đây là con cừu\", hiện bảng thông tin, chờ ~11 giây rồi phát tiếng kêu \"baa\"; (2) Gà (Chicken): tương tự, tiếng cục tác, ~12 giây; (3) Chó (Dog): tiếng sủa, ~10 giây; (4) Heo (Pig): tiếng kêu \"oink\", ~12 giây; (5) Bò (Cow): tiếng \"moo\", ~11 giây. Sau mỗi con vật, có khoảng dừng 4 giây trước khi chuyển tiếp. Bài kết thúc tự động sau khi xem đủ 5 con vật, nhạc kết thúc phát và camera trở về menu. Phù hợp với trẻ nhạy cảm âm thanh nhẹ vì âm thanh có kiểm soát; tuy nhiên có nhạc nền liên tục cần lưu ý."
}
```

---

## 📁 Collection: `messages` (6 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `roomId` | `string` | Giá trị đơn (`8kvYLnuBNpQVp2KOp83RwxnI7Dn2_D...`) |
| `participants` | `Array<string> (2 items)` | Giá trị đơn (`wCQwZYdgkVQhjv4RvYRPfctJjIT2,8kvYLnuBNpQVp2KOp83RwxnI7Dn2`) |
| `senderId` | `string` | Giá trị đơn (`wCQwZYdgkVQhjv4RvYRPfctJjIT2`) |
| `receiverId` | `string` | Giá trị đơn (`8kvYLnuBNpQVp2KOp83RwxnI7Dn2`) |
| `childId` | `string` | Giá trị đơn (`DiF02X6e7A1JmP03fWQo`) |
| `content` | `string` | Giá trị đơn (`alo`) |
| `timestamp` | `string` | Giá trị đơn (`2026-04-04T13:42:56.733Z`) |
| `read` | `boolean` | Giá trị đơn (`true`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "roomId": "8kvYLnuBNpQVp2KOp83RwxnI7Dn2_DiF02X6e7A1JmP03fWQo_wCQwZYdgkVQhjv4RvYRPfctJjIT2",
  "participants": [
    "wCQwZYdgkVQhjv4RvYRPfctJjIT2",
    "8kvYLnuBNpQVp2KOp83RwxnI7Dn2"
  ],
  "senderId": "wCQwZYdgkVQhjv4RvYRPfctJjIT2",
  "receiverId": "8kvYLnuBNpQVp2KOp83RwxnI7Dn2",
  "childId": "DiF02X6e7A1JmP03fWQo",
  "content": "alo",
  "timestamp": "2026-04-04T13:42:56.733Z",
  "read": true
}
```

---

## 📁 Collection: `parent_ai_insights` (1 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `basedOnSessionIds` | `Array<string> (5 items)` | Giá trị đơn (`3a4f4a87-018f-4969-860a-a9c370d30a85,81a5965d-07db-4c25-9e53-57622d5b0a71,01906fd9-76e5-47dc-b24d-46613972c788`) |
| `isDemo` | `boolean` | Giá trị đơn (`false`) |
| `areasOfConcern` | `string` | Giá trị đơn (`Bên cạnh những điểm sáng, chún...`) |
| `goodNews` | `string` | Giá trị đơn (`Chào Anh/Chị, chúng tôi rất vu...`) |
| `childId` | `string` | Giá trị đơn (`XrtGTcnPz4yZPFwUKBiE`) |
| `generatedAt` | `string` | Giá trị đơn (`2026-05-24T09:26:24.372Z`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "basedOnSessionIds": [
    "3a4f4a87-018f-4969-860a-a9c370d30a85",
    "81a5965d-07db-4c25-9e53-57622d5b0a71",
    "01906fd9-76e5-47dc-b24d-46613972c788",
    "14f91665-7f2a-4c48-8154-42b56155777b",
    "c050d9a8-656c-4da9-be2f-6becde7cb51e"
  ],
  "isDemo": false,
  "areasOfConcern": "Bên cạnh những điểm sáng, chúng tôi cũng muốn cùng Anh/Chị quan tâm đến một số khía cạnh để hỗ trợ bé tốt hơn. Cụ thể, trong chủ đề 'Bài học rửa tay', có hai buổi học ngày 10/5 bé chưa đạt được điểm số như mong muốn (0/10). Điều này có thể cho thấy bé vẫn đang cần thêm thời gian hoặc một phương pháp tiếp cận khác để củng cố kỹ năng này. Ngoài ra, trong hai buổi học mà bé đạt điểm cao, hệ thống có ghi nhận bé có lúc thể hiện các hành vi tự kích thích (stimming) hoặc bị phân tâm. Dù bé vẫn hoàn thành tốt nhiệm vụ, những tín hiệu này cho thấy bé có thể đang nỗ lực rất nhiều để duy trì sự tập trung và điều hòa cảm xúc. Anh/Chị có thể hỗ trợ bé tại nhà bằng cách cùng bé ôn lại các bước rửa tay đơn giản, thực hành trong môi trường quen thuộc, ít yếu tố gây xao nhãng. Nếu bé có dấu hiệu không thoải mái, hãy nhẹ nhàng tạm dừng và khuyến khích bé thư giãn trước khi tiếp tục. Sự kiên nhẫn và đồng hành của Anh/Chị là nguồn động lực to lớn giúp bé vượt qua những thử thách nhỏ này.",
  "goodNews": "Chào Anh/Chị, chúng tôi rất vui được chia sẻ những tin tức tích cực về quá trình học của bé trong 5 buổi gần đây. Bé nhà mình đã thể hiện sự tiến bộ đáng khen ngợi! Đặc biệt, trong buổi học 'Khám phá động vật nông trại' vào ngày 24/5, bé đã xuất sắc đạt điểm tuyệt đối 10/10, cho thấy khả năng tiếp thu và ghi nhớ kiến thức lý thuyết rất tốt. Hơn nữa, với chủ đề 'Bài học rửa tay', bé cũng đã có 2 buổi học đạt điểm số tối đa 10/10 và hoàn thành nhiệm vụ rất nhanh chóng chỉ trong 1 phút mà không cần bất kỳ gợi ý nào. Điều này chứng tỏ bé có tiềm năng rất lớn trong việc nắm bắt và thực hiện các kỹ năng tự lập khi có sự tập trung cao độ. Đây thực sự là những bước tiến đáng tự hào của bé!",
  "childId": "XrtGTcnPz4yZPFwUKBiE",
  "generatedAt": "2026-05-24T09:26:24.372Z"
}
```

---

## 📁 Collection: `parents` (2 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `uid` | `string` | Giá trị đơn (`8kvYLnuBNpQVp2KOp83RwxnI7Dn2`) |
| `name` | `string` | Giá trị đơn (`Phạm Ngọc Thạch`) |
| `email` | `string` | Giá trị đơn (`parent@gmail.com`) |
| `role` | `string` | Giá trị đơn (`parent`) |
| `centerId` | `string` | Giá trị đơn (`CT-TQMDC`) |
| `createdAt` | `string` | Giá trị đơn (`2026-04-01T13:52:38.873Z`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-04-01T13:52:38.873Z`) |
| `status` | `string` | Giá trị đơn (`Active`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "uid": "8kvYLnuBNpQVp2KOp83RwxnI7Dn2",
  "name": "Phạm Ngọc Thạch",
  "email": "parent@gmail.com",
  "role": "parent",
  "centerId": "CT-TQMDC",
  "createdAt": "2026-04-01T13:52:38.873Z",
  "updatedAt": "2026-04-01T13:52:38.873Z",
  "status": "Active"
}
```

---

## 📁 Collection: `schedules` (3 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `childId` | `string` | Giá trị đơn (`XrtGTcnPz4yZPFwUKBiE`) |
| `expertUid` | `string` | Giá trị đơn (`expert123`) |
| `lessonId` | `string` | Giá trị đơn (`Intro_1`) |
| `dayOfWeek` | `number` | Giá trị đơn (`6`) |
| `startHour` | `number` | Giá trị đơn (`14`) |
| `startMinute` | `number` | Giá trị đơn (`0`) |
| `durationMinutes` | `number` | Giá trị đơn (`30`) |
| `color` | `string` | Giá trị đơn (`bg-blue-100 text-blue-700 dark...`) |
| `createdAt` | `string` | Giá trị đơn (`2026-06-14T10:04:02.579Z`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-06-14T10:04:02.579Z`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "childId": "XrtGTcnPz4yZPFwUKBiE",
  "expertUid": "expert123",
  "lessonId": "Intro_1",
  "dayOfWeek": 6,
  "startHour": 14,
  "startMinute": 0,
  "durationMinutes": 30,
  "color": "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 border-blue-200 dark:border-blue-800",
  "createdAt": "2026-06-14T10:04:02.579Z",
  "updatedAt": "2026-06-14T10:04:02.579Z"
}
```

---

## 📁 Collection: `sessions` (59 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `child_profile_id` | `string` | Giá trị đơn (`XrtGTcnPz4yZPFwUKBiE`) |
| `completion_status` | `string` | Giá trị đơn (`success`) |
| `device_id` | `string` | Giá trị đơn (`faeaf9ca0927c95e03b85a8d93aae3...`) |
| `hosted_by` | `string` | Giá trị đơn (`NzIspIBjNtRdH0l92IQ1rUyft272`) |
| `lesson_id` | `string` | Giá trị đơn (`WashingHand_1`) |
| `lesson_name` | `string` | Giá trị đơn (`Bài học rửa tay`) |
| `level_index` | `number` | Giá trị đơn (`0`) |
| `level_name` | `string` | Giá trị đơn (`Chỉ dẫn và có mẫu`) |
| `quest_logs` | `Array<Object> (6 items)` | Danh sách Object con (gồm các key: `completion_status`, `hints_physical`, `hints_verbal`, `hints_visual`, `index`, `quest_name`, `response_time`) |
| `session_id` | `string` | Giá trị đơn (`01906fd9-76e5-47dc-b24d-466139...`) |
| `start_time` | `string` | Giá trị đơn (`2026-05-10T20:00:56`) |
| `type` | `string` | Giá trị đơn (`theoretical`) |
| `video_url` | `null` | Giá trị đơn (`null`) |
| `duration` | `number` | Giá trị đơn (`74`) |
| `evaluation` | `string` | Giá trị đơn (``) |
| `score` | `number` | Giá trị đơn (`10`) |
| `auto_alerts` | `Array<Object> (7 items)` | Danh sách Object con (gồm các key: `id`, `type`, `group`, `quest_index`, `severity`, `timestamp`, `time_offset`, `duration_sec`, `message`, `auto_detected`, `suppressed`, `note`) |
| `behavior_logs` | `Array (empty)` | Giá trị đơn (``) |
| `finish_time` | `string` | Giá trị đơn (`2026-05-10T13:02:18.079Z`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-05-10T13:02:18.079Z`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "child_profile_id": "XrtGTcnPz4yZPFwUKBiE",
  "completion_status": "success",
  "device_id": "faeaf9ca0927c95e03b85a8d93aae35907de133e",
  "hosted_by": "NzIspIBjNtRdH0l92IQ1rUyft272",
  "lesson_id": "WashingHand_1",
  "lesson_name": "Bài học rửa tay",
  "level_index": 0,
  "level_name": "Chỉ dẫn và có mẫu",
  "quest_logs": [
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 0,
      "quest_name": "Bật vòi nước",
      "response_time": 7.556962251663208
    },
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 1,
      "quest_name": "Làm ướt tay",
      "response_time": 4.334716081619263
    },
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 2,
      "quest_name": "Xịt xà phòng",
      "response_time": 16.018412828445435
    },
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 3,
      "quest_name": "Rửa tay",
      "response_time": 7.909572601318359
    },
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 4,
      "quest_name": "Tắt vòi nước",
      "response_time": 1.9577717781066895
    },
    {
      "completion_status": "success",
      "hints_physical": 0,
      "hints_verbal": 0,
      "hints_visual": 0,
      "index": 5,
      "quest_name": "Lau tay với khăn",
      "response_time": 5.489886999130249
    }
  ],
  "session_id": "01906fd9-76e5-47dc-b24d-46613972c788",
  "start_time": "2026-05-10T20:00:56",
  "type": "theoretical",
  "video_url": null,
  "duration": 74,
  "evaluation": "",
  "score": 10,
  "auto_alerts": [
    {
      "id": "stimming_1778418060748",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418060748,
      "time_offset": 4.1,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "distraction_1778418064758",
      "type": "distraction",
      "group": "distraction",
      "quest_index": 0,
      "severity": "medium",
      "timestamp": 1778418064758,
      "time_offset": 2.1,
      "duration_sec": 6,
      "message": "Xao nhãng (Không nhìn mục tiêu > 6s)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "stimming_1778418080802",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418080802,
      "time_offset": 24.2,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "stimming_1778418100879",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418100879,
      "time_offset": 44.3,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "stimming_1778418110899",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418110899,
      "time_offset": 54.3,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "stimming_1778418118926",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418118926,
      "time_offset": 62.3,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    },
    {
      "id": "stimming_1778418128962",
      "type": "stimming",
      "group": "distraction",
      "quest_index": 0,
      "severity": "high",
      "timestamp": 1778418128962,
      "time_offset": 72.4,
      "duration_sec": 2,
      "message": "Lắc đầu mạnh (Stimming / Meltdown)",
      "auto_detected": true,
      "suppressed": false,
      "note": ""
    }
  ],
  "behavior_logs": [],
  "finish_time": "2026-05-10T13:02:18.079Z",
  "updatedAt": "2026-05-10T13:02:18.079Z"
}
```

---

## 📁 Collection: `system_admins` (1 documents)

### 1. Bảng kiểu dữ liệu các trường (Field Types)

| Trường (Field) | Kiểu (Type) | Mô tả cấu trúc con |
| :--- | :--- | :--- |
| `email` | `string` | Giá trị đơn (`admin@gmail.com`) |
| `updatedAt` | `string` | Giá trị đơn (`2026-03-31T10:04:44.383Z`) |
| `name` | `string` | Giá trị đơn (`Lê Duy Vũ`) |
| `role` | `string` | Giá trị đơn (`admin`) |

### 2. Cấu trúc JSON mẫu thực tế (Sample Document)

```json
{
  "email": "admin@gmail.com",
  "updatedAt": "2026-03-31T10:04:44.383Z",
  "name": "Lê Duy Vũ",
  "role": "admin"
}
```

---

