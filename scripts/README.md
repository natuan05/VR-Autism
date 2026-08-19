# VR-Autism CKG, RepoMap & JIT Context Toolset

Hệ thống phân tích tĩnh và truy xuất ngữ cảnh liên phân hệ (Cross-Stack Code Knowledge Graph) cho nền tảng VR-Autism gồm 3 thành phần: Unity C#, Python Voice Agent và Next.js Web Dashboard.

## 📁 Cấu trúc Toolset

```
scripts/
├── ast_parsers.py         # Trích xuất AST & Symbol Model cho C#, Python, TS/TSX
├── graph_builder.py       # Xây dựng Code Knowledge Graph bằng NetworkX & PageRank
├── repomap_generator.py   # Sinh tự động REPOMAP.md (< 3k tokens) và repomap.json
├── jit_context.py         # CLI trích xuất ngữ cảnh on-demand và phân tích tác động (--impact)
└── tests/                 # Bộ kiểm thử tự động toàn diện (E2E & Adversarial)
```

## 🚀 Hướng dẫn Sử dụng

### 1. Cập nhật RepoMap toàn dự án
Chạy lệnh sau để quét lại mã nguồn và cập nhật `REPOMAP.md` cùng `repomap.json`:
```bash
python scripts/repomap_generator.py
```
- Cấu hình ngân sách token, thư mục quét và trọng số tại: `repomap.config.json`.

### 2. Trích xuất ngữ cảnh JIT theo Tính năng / Từ khóa
Lấy lát cắt ngữ cảnh theo ngân sách token phục vụ prompt cho AI:
```bash
python scripts/jit_context.py --query "LiveKit DataPacket" --budget 1500
```

### 3. Phân tích Phạm vi Tác động khi Sửa Code (Blast Radius)
Kiểm tra xem khi sửa một Class / Function / DataPacket thì những file nào ở cả 3 phân hệ bị ảnh hưởng:
```bash
python scripts/jit_context.py --impact "VoiceQuest"
```

## 🧪 Chạy Bộ Kiểm thử
```bash
python scripts/tests/test_repomap.py
```
