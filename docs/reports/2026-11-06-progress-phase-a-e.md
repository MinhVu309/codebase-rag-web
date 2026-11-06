# Báo cáo: Tiến độ backend đến Phase E

Ngày: 2026-11-06
Phạm vi: Phase A đến E của plan

## Tóm tắt

Backend (ASP.NET Core .NET 10) đã xong Phase A đến E: đăng nhập, project và repository, import qua RAG, chat có citation và lịch sử. Có 263 test pass trên máy. CI GitHub chưa chạy được vì tài khoản đang bị khoá do billing. Phần gọi RAG mới chạy với fake và test double, vì repo RAG của Giang vẫn rỗng.

Stack: ASP.NET Core, EF Core + Npgsql (PostgreSQL), FluentValidation, JWT trong cookie HttpOnly, xUnit + Testcontainers.

## Trạng thái

| Phase | Nội dung | Nhánh | Trạng thái GitHub | Tổng số test |
| --- | --- | --- | --- | --- |
| A | Khung repo, /health, Docker, CI | feature/phase-a-foundation | Đã merge (PR #1) | 2 |
| B | Database và auth | feature/phase-b-database-auth | Đã merge (PR #2) | 35 |
| C | RAG client và fake | feature/phase-c-rag-client | Đã push, chưa merge | 101 |
| D | Project và repository | feature/phase-d-projects-repositories | Đã push, chưa merge | 207 |
| E | Chat và lịch sử | feature/phase-e-chat | Đã push, chưa merge | 263 |

## Đã làm

**Phase A: khung và nền tảng**

- Solution gồm Api, Application, Domain, Infrastructure và hai project test.
- `GET /health` trả 200. Dockerfile nhiều tầng, `docker-compose.yml` chạy api cùng postgres.
- `.env.example` và workflow CI (`dotnet build` + `dotnet test`).

**Phase B: database và auth**

- Đủ 6 bảng theo plan, migration `InitialCreate` tự chạy khi API khởi động. `DATABASE_URL` nhận cả chuỗi kết nối lẫn URL `postgres://`.
- Đăng ký, đăng nhập, refresh, đăng xuất, `/me`. Access token JWT 15 phút trong cookie HttpOnly, SameSite=Strict, Secure.
- Refresh token chỉ lưu hash SHA-256, đổi token mỗi lần refresh. Dùng lại token cũ sẽ thu hồi toàn bộ phiên.
- Sai mật khẩu và email không tồn tại trả cùng một lỗi. Mật khẩu và hash không xuất hiện trong response hay log (có test).

**Phase C: RAG client**

- `IRagClient` có 6 hàm đúng Contract v0.2: health, tạo index, xem trạng thái, query, đọc file, xoá index.
- Timeout theo từng endpoint (query 45 giây, status 5 giây). Chỉ `GET status` được retry tối đa 2 lần.
- `FakeRagClient` (`RAG_MODE=fake`) để làm việc khi chưa có RAG. Lỗi RAG được map sang ProblemDetails kèm mã `code` ổn định.
- Không log câu hỏi, API key hay nội dung response.

**Phase D: project và repository**

- CRUD project. Import repository: validate URL GitHub (D21) và git ref, tạo index, poll trạng thái, xoá (gọi RAG xoá trước).
- Mọi truy vấn lọc theo user. Tài nguyên của người khác trả 404, giống hệt id không tồn tại.
- `index_id` của RAG không bao giờ lộ ra frontend.

**Phase E: chat và lịch sử**

- Hội thoại theo project, hỏi đáp qua RAG `/query`, lịch sử tin nhắn.
- Chặn 409 khi repo chưa `ready`. Giới hạn câu hỏi 2000 ký tự, `topK` từ 1 đến 20.
- Citation được lưu kèm `repositoryId` và `commitSha` (D14). Sau khi xoá repo, lịch sử vẫn đọc lại y nguyên (có test).
- Lượt hỏi bị RAG lỗi vẫn được ghi vào lịch sử kèm mã lỗi.

## API hiện có

Tất cả endpoint trừ `/health`, `register`, `login`, `refresh` và `logout` đều cần đăng nhập (cookie `access_token`). Lỗi trả ProblemDetails, nhiều lỗi kèm trường `code` để frontend phân nhánh.

| Method | Đường dẫn | Mô tả |
| --- | --- | --- |
| GET | /health | Kiểm tra sống, trả 200 |
| POST | /api/auth/register | Đăng ký, trả 201 và đặt cookie |
| POST | /api/auth/login | Đăng nhập |
| POST | /api/auth/refresh | Đổi refresh token, cấp access token mới |
| POST | /api/auth/logout | Đăng xuất, thu hồi refresh token (idempotent) |
| GET | /api/auth/me | Thông tin user hiện tại |
| GET, POST | /api/projects | Liệt kê, tạo project |
| GET, PUT, DELETE | /api/projects/{id} | Chi tiết (kèm repository), đổi tên, xoá |
| POST | /api/projects/{id}/repositories | Import repo GitHub, trả 202 ở trạng thái pending |
| GET | /api/repositories/{id} | Trạng thái mới nhất (dùng để poll), có cờ `stale` |
| DELETE | /api/repositories/{id} | Xoá repo và index trên RAG |
| GET, POST | /api/projects/{id}/conversations | Liệt kê, tạo hội thoại |
| GET | /api/conversations/{id}/messages | Lịch sử (mặc định 200 tin gần nhất, tối đa 500) |
| POST | /api/conversations/{id}/messages | Hỏi một câu, trả 201 kèm câu trả lời và citation |

Chưa có endpoint đọc file code (`/api/repositories/{id}/files`), và chưa có xoá hay đổi tên hội thoại.

## Cách chạy và kiểm thử

Chạy local cần Docker đang bật. Test cũng cần Docker vì Testcontainers dựng PostgreSQL thật.

1. Sao chép `.env.example` thành `.env`. Mặc định đã dùng `RAG_MODE=fake` nên không cần RAG.
2. Chạy `docker compose up -d --build`. API ở `localhost:8080`, migration tự áp dụng.
3. Thử nhanh: `curl localhost:8080/health`.
4. Chạy test: vào thư mục `backend` rồi `dotnet test`.

Biến môi trường chính: `DATABASE_URL`, `JWT_SECRET` (ít nhất 32 ký tự), `AUTH_COOKIE_SECURE` (đặt `false` khi chạy http local, phải `true` khi deploy có TLS), `RAG_MODE`, `RAG_BASE_URL`, `RAG_API_KEY` và các biến `RAG_*_TIMEOUT_SECONDS`.

Chuyển sang RAG thật hoặc Prism: đặt `RAG_MODE=http`, `RAG_BASE_URL` (trong compose dùng `host.docker.internal` thay cho `localhost`) và `RAG_API_KEY`.

## Quyết định quan trọng

Phần lớn bám các quyết định đã chốt trong plan. Các điểm sau do người làm chọn.

- **Frontend không gọi RAG (D10, D11).** Backend giữ `RAG_API_KEY`, kiểm quyền user trước khi gọi RAG, và không trả `index_id` ra ngoài.
- **404 cho tài nguyên của người khác**, không phân biệt với id không tồn tại, để không dò được id.
- **Active index (D12):** import mới nhất trở thành active. Xoá repo đang active thì lùi về repo mới nhất còn lại.
- **Chỉ nhận URL GitHub dạng `https://github.com/<owner>/<repo>`** (allowlist), và `ref` không được bắt đầu bằng `-`, vì RAG sẽ chạy `git clone` trên các giá trị này.
- **Xoá thì gọi RAG trước.** Nếu RAG lỗi thì giữ nguyên dữ liệu và trả lỗi để thử lại an toàn, không bị mồ côi index.
- **Polling chịu được lỗi tạm thời:** RAG chập chờn thì trả trạng thái cũ kèm `stale: true`, không báo thất bại.
- **Citation snapshot (D14):** lưu nguyên snippet, vị trí dòng, repository và commit để hiển thị được khi file không còn.
- **Câu hỏi và câu trả lời không bao giờ vào log (D22).** Mật khẩu, hash, API key, refresh token cũng vậy. Các điều này đều có test kiểm tra bằng chuỗi bí mật.
- **Nội dung được lưu nguyên văn.** Escape HTML khi hiển thị là việc của frontend.
- **Lỗi RAG được map sang thông báo tiếng Việt:** 409 "repo chưa sẵn sàng", 502 "hệ thống AI lỗi", 504 "quá hạn". RAG từ chối API key hiển thị như lỗi 502 chung, không lộ chi tiết cấu hình.

Các test cô lập giữa user đã được thử đột biến: khi tạm bỏ điều kiện chủ sở hữu thì test đỏ, rồi code được khôi phục.

## Tồn đọng và rủi ro

Rủi ro lớn nhất là chưa gọi RAG thật lần nào, nên định dạng dữ liệu và mã lỗi mới được kiểm chứng theo Contract v0.2 trong plan, chưa theo `openapi.yaml`.

- **CI GitHub chưa chạy được:** job fail sau 2 giây với thông báo tài khoản bị khoá do billing. Workflow và test trên runner Ubuntu chưa được kiểm chứng. Cần xử lý billing rồi Re-run.
- **Phụ thuộc repo của Giang:** repo `codebase-rag-service` vẫn rỗng. Chưa có `openapi.yaml` nên chưa sinh client bằng NSwag (client đang viết tay) và chưa pin contract. Chưa có Prism.
- **Chưa merge vào `main`:** Phase C, D, E đã push nhưng chưa mở PR. Ba nhánh chồng lên nhau, cần merge theo thứ tự C, D, E.
- **Chưa bật protect-main** trên GitHub (cần quyền admin repo).
- **Chưa có rate limit** cho đăng nhập và endpoint hỏi (dự kiến Phase G). Chưa có dọn refresh token hết hạn, chưa có xoá tài khoản hay đổi mật khẩu.

## Câu hỏi cần xác nhận

- [ ] Hỏi Giang: RAG có hiểu `ref` là `HEAD` hoặc để trống là nhánh mặc định không? Hiện backend mặc định `main`, repo dùng `master` sẽ lỗi.
- [ ] Hỏi Giang: `ref_id` của citation là số nguyên? `usage` có dạng gì? (backend đang giả định số nguyên và giữ nguyên JSON).
- [ ] Xác nhận quy tắc active index (import mới nhất là active) có đúng ý nhóm không.
- [ ] Repo ở trạng thái `failed` hiện vẫn nằm trong danh sách để người dùng thấy và xoá. Có muốn ẩn đi không?
- [ ] Frontend có cần xoá hay đổi tên hội thoại không? Plan mục 4 chưa có.

## Bước tiếp theo

Phase F là việc kế tiếp theo thứ tự ưu tiên của plan. Phần bắt buộc (A đến E) đã xong.

| Phase | Nội dung | Điều kiện |
| --- | --- | --- |
| F | Code Viewer proxy: `GET /api/repositories/{id}/files`, kiểm quyền, giới hạn 2000 dòng, trả rõ khi file 404 để frontend dùng snapshot. Đo p95 của query và status để chốt timeout | Không cần RAG thật để viết, cần để đo p95 |
| G | Đổi sang RAG thật, rà bảo mật, rate limit | Cần RAG chạy và `RAG_API_KEY` từ Giang |
| H | Đóng gói, test tích hợp và E2E theo Definition of Done MVP | Sau G |
| I | Deploy và demo, chuẩn bị ít nhất 1 repo đã index sẵn | Sau H |

Việc cần làm ngay: mở PR cho nhánh C, D, E (theo thứ tự đó), xử lý billing GitHub để CI chạy, rồi nhắn Giang push `contracts/openapi.yaml`.
