# Tài liệu dự án

Thư mục này chứa các báo cáo tiến độ của backend `codebase-rag-web`. Plan gốc nằm ngoài repo (`BACKEND_PLAN.md`); các báo cáo ghi lại cái gì đã làm so với plan đó.

## Báo cáo

Mới nhất ở trên cùng.

| Ngày | Báo cáo | Phạm vi |
| --- | --- | --- |
| 2026-11-06 | [Tiến độ đến Phase E](reports/2026-11-06-progress-phase-a-e.md) | Phase A đến E |

## Cách thêm báo cáo mới

1. Sao chép [reports/TEMPLATE.md](reports/TEMPLATE.md) thành `reports/YYYY-MM-DD-<tên-ngắn>.md`.
2. Điền các mục. Ghi số liệu thật (số test, trạng thái PR), không ghi dự đoán.
3. Thêm một dòng vào bảng ở trên, báo cáo mới nhất ở đầu bảng.
4. Đi qua quy trình bình thường: nhánh `feature/*` hoặc `fix/*`, PR, CI xanh, merge.

Báo cáo là ảnh chụp tại một ngày. Đừng sửa báo cáo cũ cho khớp tình hình mới; hãy viết báo cáo mới.

## Quy ước

- Viết bằng tiếng Việt, câu ngắn, mở đầu mỗi mục bằng điều quan trọng nhất.
- Không đưa secret, API key, token hay dữ liệu cá nhân vào báo cáo.
- Cái chưa kiểm chứng phải nói rõ là chưa kiểm chứng (ví dụ chưa chạy với RAG thật).
