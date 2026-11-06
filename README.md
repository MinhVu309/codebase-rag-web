# codebase-rag-web

Ứng dụng web hỏi đáp về một codebase GitHub công khai: người dùng đăng nhập, import một repository, chờ hệ thống index xong rồi đặt câu hỏi bằng ngôn ngữ tự nhiên và nhận câu trả lời kèm trích dẫn (citation) trỏ tới file và dòng code.

Repo này gồm **Web Backend** (đã có) và **Frontend** (do thành viên Web còn lại làm). Phần RAG (index và trả lời) là dịch vụ riêng, nằm ở repo `codebase-rag-service` do Giang phụ trách. Repo này không chứa và không sửa code RAG.

## Phân công

| Phần | Thư mục | Người làm |
| --- | --- | --- |
| Web Backend (.NET 10) | `backend/` | MinhVu309 |
| Frontend (React) | `frontend/` | Thành viên Web còn lại (điền tên) |
| RAG Service | repo `codebase-rag-service` | Giang |

Frontend chỉ nói chuyện với Web Backend qua `/api`. Frontend **không** gọi RAG và không giữ `RAG_API_KEY`.

## Cấu trúc thư mục

```
backend/            Web API (Api, Application, Domain, Infrastructure) và test
frontend/           Giao diện React (chưa có, dành cho thành viên FE)
docs/               Báo cáo tiến độ, xem docs/README.md
contracts/          openapi.yaml của RAG sẽ được pin ở đây (chưa có)
docker-compose.yml  api + postgres
.env.example        mẫu biến môi trường (sao chép thành .env)
.github/workflows/  CI: dotnet build + dotnet test
```

## Chạy nhanh

Cần Docker đang bật.

1. Sao chép `.env.example` thành `.env` (không commit file `.env`).
2. `docker compose up -d --build`
3. API chạy ở `http://localhost:8080`. Kiểm tra: `curl localhost:8080/health`.

Mặc định `.env.example` đặt `RAG_MODE=fake`: backend dùng một RAG giả trong bộ nhớ nên **không cần RAG thật** để phát triển. Muốn nối RAG thật hoặc Prism, đặt `RAG_MODE=http`, `RAG_BASE_URL` và `RAG_API_KEY`. Trong compose, dùng `host.docker.internal` thay cho `localhost` khi RAG chạy trên máy host.

Chạy test (cần Docker vì test dựng PostgreSQL thật bằng Testcontainers):

```
cd backend
dotnet test
```

## Dành cho người làm frontend

Phần này là những gì frontend cần biết để nối với backend hiện tại.

### Kết nối và xác thực

- Mọi API nằm dưới `/api`. Production sẽ chạy cùng một origin (nginx phục vụ React và proxy `/api` sang backend), nên backend **không bật CORS**.
- Khi chạy dev server riêng (Vite hoặc tương tự), hãy cấu hình proxy `/api` sang `http://localhost:8080` để trình duyệt thấy cùng origin và gửi được cookie.
- Đăng nhập bằng cookie HttpOnly, SameSite=Strict: `access_token` (hết hạn sau 15 phút) và `refresh_token` (7 ngày). Frontend không đọc được token và không cần lưu gì.
- Chạy local qua http thì giữ `AUTH_COOKIE_SECURE=false` trong `.env` (đã là mặc định của `.env.example`). Khi deploy có TLS phải là `true`.
- Khi một API trả 401 vì access token hết hạn: gọi `POST /api/auth/refresh`, nếu thành công thì thử lại request đó một lần. Nếu refresh cũng trả 401 thì chuyển về trang đăng nhập.

### Các endpoint hiện có (đến Phase E)

Tất cả cần đăng nhập, trừ `health`, `register`, `login`, `refresh`, `logout`.

| Method | Đường dẫn | Mô tả |
| --- | --- | --- |
| GET | `/health` | Kiểm tra sống |
| POST | `/api/auth/register` | Đăng ký (body: `email`, `password` từ 8 đến 128 ký tự), tự đăng nhập |
| POST | `/api/auth/login` | Đăng nhập |
| POST | `/api/auth/refresh` | Cấp lại token |
| POST | `/api/auth/logout` | Đăng xuất |
| GET | `/api/auth/me` | User hiện tại |
| GET, POST | `/api/projects` | Danh sách, tạo project (body: `name`, tối đa 100 ký tự) |
| GET, PUT, DELETE | `/api/projects/{id}` | Chi tiết (kèm repository), đổi tên, xoá |
| POST | `/api/projects/{id}/repositories` | Import repo GitHub (body: `repoUrl`, `ref` tuỳ chọn, mặc định `main`). Trả 202 |
| GET | `/api/repositories/{id}` | Trạng thái repository, dùng để poll |
| DELETE | `/api/repositories/{id}` | Xoá repository |
| GET, POST | `/api/projects/{id}/conversations` | Danh sách, tạo hội thoại (body: `title` tuỳ chọn) |
| GET | `/api/conversations/{id}/messages` | Lịch sử tin nhắn (`?limit=`, mặc định 200, tối đa 500) |
| POST | `/api/conversations/{id}/messages` | Hỏi một câu (body: `question` tối đa 2000 ký tự, `topK` từ 1 đến 20 tuỳ chọn) |

Chưa có: endpoint đọc file cho code viewer (`/api/repositories/{id}/files`, dự kiến Phase F), xoá hoặc đổi tên hội thoại. Cần gì thêm thì báo để thống nhất trước khi backend làm.

`repoUrl` chỉ nhận đúng dạng `https://github.com/<owner>/<repo>`.

### Luồng import và polling

1. `POST /api/projects/{id}/repositories` trả 202 với `status: "pending"`.
2. Poll `GET /api/repositories/{id}` khoảng 2 giây một lần cho tới khi `status` là `ready` hoặc `failed`. Các trạng thái: `pending`, `cloning`, `parsing`, `embedding`, `ready`, `failed`. Có `filesDone` và `totalFiles` để vẽ thanh tiến độ.
3. Nếu response có `stale: true` thì backend tạm thời chưa hỏi được RAG và đang trả trạng thái cũ. Cứ tiếp tục poll, đừng coi là lỗi.
4. Khi `failed`, `errorCode` cho biết lý do (ví dụ `REPO_TOO_LARGE`). Người dùng có thể xoá repository rồi import lại.

Chỉ hỏi được khi repository `ready`.

### Định dạng lỗi

Mọi lỗi là ProblemDetails (JSON). Lỗi validate (400) có thêm `errors` theo từng trường. Nhiều lỗi có thêm trường `code` ổn định để frontend quyết định hiển thị gì:

| `code` | HTTP | Ý nghĩa và gợi ý UI |
| --- | --- | --- |
| `INDEX_NOT_READY` | 409 | Repository chưa index xong, bảo người dùng chờ |
| `NO_REPOSITORY` | 409 | Project chưa import repository nào |
| `REPOSITORY_FAILED` | 409 | Index thất bại, gợi ý xoá và import lại |
| `INVALID_REPO`, `REPO_TOO_LARGE` | 422 | URL không hỗ trợ hoặc repository quá lớn |
| `LLM_ERROR` | 502 | Hệ thống AI lỗi tạm thời, cho thử lại |
| `RAG_UNAVAILABLE` | 502 | Không kết nối được hệ thống AI |
| `TIMEOUT` | 504 | Hệ thống AI trả lời quá hạn, cho thử lại |
| `FILE_NOT_FOUND` | 404 | File không còn (chuẩn bị cho code viewer): hiển thị snippet đã lưu kèm nhãn "phiên bản cũ" |

Timeout từ frontend tới backend nên đặt 60 giây.

### Hiển thị câu trả lời

- Mỗi tin nhắn assistant có `content` và `citations`. Trong `content` có marker dạng `[1]`, `[2]`, khớp với `refId` của citation.
- Citation lưu sẵn `filePath`, `startLine`, `endLine`, `snippet`, `repositoryId` và `commitSha`. Dữ liệu này là bản chụp tại thời điểm trả lời và đọc lại được kể cả khi repository đã bị xoá.
- Tin nhắn assistant của một lượt bị lỗi có `content` rỗng và `errorCode`. Hiển thị lỗi tương ứng thay cho câu trả lời.
- **Backend lưu và trả nội dung nguyên văn** (kể cả `<script>` hay markdown). Frontend phải escape HTML và markdown khi render.

### Thử luồng mà không cần RAG thật

Với `RAG_MODE=fake`: index tự chuyển `cloning`, `parsing`, `embedding`, `ready` qua 4 lần poll. Câu trả lời là văn bản mẫu kèm một citation vào `src/Program.cs`. Dùng URL chứa `too-large` để thấy trường hợp `failed` (`REPO_TOO_LARGE`), và URL chứa `invalid` để thấy lỗi 422.

## Quy trình làm việc chung

- Nhánh `main` luôn ở trạng thái chạy được. Làm trên nhánh `feature/*` hoặc `fix/*`, mở PR, chờ CI xanh rồi merge. Không push thẳng vào `main`.
- Frontend làm trong `frontend/`, nên dùng nhánh dạng `feature/fe-*` để tránh đụng nhánh backend.
- Không commit secret. Mọi cấu hình nhạy cảm nằm trong `.env` (đã được git bỏ qua), mẫu ở `.env.example`.
- Thay đổi API (đường dẫn, field, mã lỗi) cần nêu rõ trong PR và cập nhật README này để hai bên không lệch nhau.
- Không sửa `contracts/openapi.yaml` hay repo RAG. Cần đổi contract thì báo Giang.

## Tài liệu và tiến độ

Báo cáo tiến độ nằm trong [docs/](docs/README.md). Báo cáo mới nhất: [Tiến độ đến Phase E](docs/reports/2026-11-06-progress-phase-a-e.md).
