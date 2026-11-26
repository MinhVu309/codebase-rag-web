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
contracts/          openapi.yaml của RAG (bản pin, không sửa) và README ghi nguồn/commit/sha256
docker-compose.yml  api + postgres (+ mock-rag, profile "mock")
.env.example        mẫu biến môi trường (sao chép thành .env)
.github/workflows/  CI: dotnet build + dotnet test
```

## Chạy nhanh

Cần Docker đang bật.

1. Sao chép `.env.example` thành `.env` (không commit file `.env`).
2. `docker compose up -d --build`
3. API chạy ở `http://localhost:8080`. Kiểm tra: `curl localhost:8080/health`.

Mặc định `.env.example` đặt `RAG_MODE=fake`: backend dùng một RAG giả trong bộ nhớ nên **không cần RAG thật** để phát triển. Muốn nối Prism hoặc RAG thật, xem mục "Mock RAG" bên dưới.

### Mock RAG (Prism)

`contracts/openapi.yaml` là bản pin của contract Web <-> RAG (nguồn, commit và sha256 ghi ở [contracts/README.md](contracts/README.md)). Service `mock-rag` trong compose chạy [Prism](https://github.com/stoplightio/prism) 5.16.0 trên file đó, ở profile `mock` nên `docker compose up` mặc định không chạy nó (lần đầu cần mạng để `npx` tải Prism).

```
docker compose --profile mock up -d mock-rag     # http://localhost:4010
# trong .env:
#   RAG_MODE=http
#   RAG_BASE_URL=http://mock-rag:4010     # cùng mạng compose, không dùng host.docker.internal
#   RAG_API_KEY=bat-ky-chuoi-nao          # Prism chỉ kiểm tra có header, không kiểm tra giá trị
docker compose up -d --build api postgres
```

Thử thẳng mock bằng curl (mọi endpoint trừ `/v1/health` cần `X-API-Key`):

```
curl localhost:4010/v1/health
curl -X POST localhost:4010/v1/indexes -H 'X-API-Key: test-key' -H 'Content-Type: application/json' \
     -d '{"repo_url":"https://github.com/pallets/flask"}'
curl localhost:4010/v1/indexes/idx_3f9a1c -H 'X-API-Key: test-key' -H 'Prefer: example=embedding'   # ready | embedding | failed
curl -X POST localhost:4010/v1/query -H 'X-API-Key: test-key' -H 'Content-Type: application/json' \
     -d '{"index_id":"idx_3f9a1c","question":"JWT kiem tra o dau?"}'
curl 'localhost:4010/v1/indexes/idx_3f9a1c/files?path=src/flask/app.py&start=34&end=36' -H 'X-API-Key: test-key'
curl -i 'localhost:4010/v1/indexes/idx_3f9a1c/files?path=a' -H 'X-API-Key: test-key' -H 'Prefer: code=409'
```

Chọn `fake` hay Prism:

| | `RAG_MODE=fake` | Prism (`mock-rag`) |
| --- | --- | --- |
| Có trạng thái | Có: index đi `pending` -> `ready` qua các lần poll, xoá rồi thì mất | Không: ví dụ cố định, tạo xong GET vẫn ra `ready` |
| Kiểm tra contract | Không | Có: request sai định dạng bị từ chối theo yaml |
| Chọn tình huống | URL chứa `too-large` / `invalid` | Header `Prefer: example=...` hoặc `Prefer: code=409` |
| Dùng khi | Phát triển luồng import/chat end-to-end | Kiểm tra backend đọc đúng JSON và mã lỗi của contract |

Giới hạn của Prism: request sai định dạng có thể ra ví dụ lỗi cố định hoặc 422 `application/problem+json` (không theo `ErrorResponse`; backend rơi về mã theo status). **Không dùng Prism để đánh giá index hay chất lượng RAG**, và không dùng để đo timeout (p95 chỉ đo được trên RAG thật).

### RAG thật chạy local

Repo RAG (`codebase-rag-service`, nhánh `work/wp6-retrieval-reranking`) chạy trên máy ở `http://127.0.0.1:8000` bằng đúng một process uvicorn (không `--workers`). Hướng dẫn dựng nằm trong `docs/web-team-local-handoff.md` của repo đó. Phía web chỉ cần:

```
# trong .env của web
RAG_MODE=http
RAG_BASE_URL=http://host.docker.internal:8000   # api chạy trong Docker; dùng http://127.0.0.1:8000 nếu chạy dotnet run trên máy
RAG_API_KEY=<cùng giá trị RAG_API_KEY trong .env của RAG>
RAG_QUERY_MODE=hybrid                            # dense | hybrid
RAG_QUERY_RERANK=false                           # true chỉ hợp lệ khi mode=hybrid
```

- `RAG_QUERY_MODE` (mặc định `hybrid`) và `RAG_QUERY_RERANK` (mặc định `false`) được gửi trong `options` của mọi `POST /v1/query`. Giá trị sai (`hybird`, `RAG_QUERY_RERANK=yes`, `rerank=true` với `dense`) làm API không khởi động. Rerank nạp cross-encoder và chậm đáng kể nên chỉ bật khi có quyết định riêng.
- `RAG_QUERY_TIMEOUT_SECONDS` (mặc định **65**): RAG đặt `LLM_TIMEOUT_MS=60000`, nên web phải chờ lâu hơn mức đó, nếu không sẽ tự ngắt trước khi RAG kịp trả 504 `TIMEOUT`. Giá trị là max(2 lần P95, `LLM_TIMEOUT_MS` + 5s); đo 18 câu thật trên model free cho P95 khoảng 25 đến 32 giây (xem [báo cáo E2E](docs/reports/e2e-local-2026-11-21.md)). Frontend phải đặt timeout gọi `POST .../messages` lớn hơn 65s (khuyến nghị 75s); mức 60s ghi trong kế hoạch cũ là không đủ. Đo lại khi đổi model hoặc bật rerank.
- Từ container không gọi được `host.docker.internal:8000` thì chạy RAG với `--host 0.0.0.0` (chỉ làm khi cần, vì khi đó máy khác trong mạng cũng gọi được cổng 8000, tuy vẫn bị chặn bởi `X-API-Key`).
- Mỗi người dùng RAG cục bộ riêng: dữ liệu index nằm ở `storage/` của máy chạy RAG, không chia sẻ.

### D40: nhiều repository trong một hội thoại (đang làm)

**D40 đang làm; luồng hỏi chưa đổi.** Đã có phía gọi RAG (`IRagClient.QueryMultiAsync` gọi `POST /v1/query/multi`, hợp đồng v0.3 trong [contracts/](contracts/README.md)), bảng Scope (repository của một hội thoại) và ba endpoint quản lý Scope (xem "Conversation Scope" ở phần dành cho frontend). Việc hỏi đáp vẫn dùng `POST /v1/query` trên repository đang active của project; Focus và bản chụp lịch sử (snapshot) làm ở bước tiếp theo. Các endpoint cũ không đổi; thay đổi duy nhất là `POST /api/projects/{id}/conversations` nhận thêm `initialRepositoryId` tuỳ chọn.

| Biến | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `RAG_QUERY_API` | `single` | `single` = `POST /v1/query` (như hiện nay, cũng là đường lùi). `multi` = `POST /v1/query/multi`. Giá trị khác làm API không khởi động. Hiện chỉ có một tác dụng: ở `multi`, tạo hội thoại **bắt buộc** có `initialRepositoryId`. Luồng hỏi đáp chưa đọc biến này. |
| `CONVERSATION_SCOPE_MAX` | `5` | Số repository tối đa trong một hội thoại, từ 1 đến 5 (yaml v0.3 giới hạn `scope` tối đa 5 phần tử, nên lớn hơn 5 bị từ chối lúc khởi động). Dùng khi thêm repository vào hội thoại (409 `SCOPE_LIMIT_REACHED` khi đủ). |

`/v1/query/multi` dùng chung `RAG_QUERY_MODE`, `RAG_QUERY_RERANK` và `RAG_QUERY_TIMEOUT_SECONDS` (65s) với `/v1/query`, không tự retry, luôn gửi `debug=false`, và không gửi `focus_repository_ids` khi Focus rỗng.

`RAG_MODE=fake` hỗ trợ `/v1/query/multi` với kịch bản chọn bằng dấu hiệu trong câu hỏi (không phân biệt hoa thường; có nhiều dấu hiệu thì lấy theo thứ tự dưới đây):

| Dấu hiệu trong câu hỏi | Kết quả của fake |
| --- | --- |
| `fake:unavailable` | 503 `ALL_INDEXES_UNAVAILABLE`, mọi repository `unavailable/index_not_ready` |
| `fake:no_evidence` | `completed` + `no_evidence`, không có citation (có Focus thì kèm fallback) |
| `fake:fallback` | Focus chỉ có 1 chunk nên tìm tiếp các repository khác trong Scope (cần Focus khác rỗng và còn repository ngoài Focus, nếu không thì như mặc định) |
| `fake:partial` | `partial` + `answered`, một repository đã tìm bị `failed/timeout` (cần ít nhất hai repository được tìm, nếu không thì như mặc định) |
| không có dấu hiệu | `completed` + `answered`, citation từ repository Focus đầu tiên (hoặc repository đầu của Scope khi Focus rỗng); repository ngoài Focus là `not_searched/focus_evidence_sufficient` |

Fake kiểm tra Scope như RAG thật (rỗng, quá 5, trùng id, Focus ngoài Scope đều ra 422 `INVALID_REQUEST`). Index do fake tạo mà chưa `ready` thì là `unavailable/index_not_ready`; index fake chưa từng thấy (ví dụ tạo trước khi khởi động lại API) được coi là sẵn sàng.

Thử mock Prism với yaml v0.3 (đã pin):

```
curl -X POST localhost:4010/v1/query/multi -H 'X-API-Key: test-key' -H 'Content-Type: application/json' \
     -d '{"question":"Trace how an order response is produced and consumed.","scope":[{"repository_id":"web-repository-guid-a","index_id":"idx_backend_a","display_name":"orders-api"},{"repository_id":"web-repository-guid-b","index_id":"idx_client_b","display_name":"orders-client"}],"focus_repository_ids":["web-repository-guid-a"],"top_k":8,"options":{"mode":"hybrid","rerank":false},"debug":false}'
curl -i -X POST localhost:4010/v1/query/multi -H 'X-API-Key: test-key' -H 'Content-Type: application/json' -H 'Prefer: code=503' \
     -d '{"question":"q","scope":[{"repository_id":"web-repository-guid-a","index_id":"idx_backend_a","display_name":"orders-api"}]}'
```

Chạy test (cần Docker vì test dựng PostgreSQL thật bằng Testcontainers):

```
cd backend
dotnet test
```

## Dành cho người làm frontend

Phần này là những gì frontend cần biết để nối với backend hiện tại.

### Kết nối và xác thực

- Mọi API nằm dưới `/api`. Production sẽ chạy cùng một origin (nginx phục vụ React và proxy `/api` sang backend), nên backend mặc định **không bật CORS**.
- **Khi chạy dev server riêng (Vite hoặc tương tự), cách khuyến nghị là proxy:** cấu hình proxy `/api` sang `http://localhost:8080` để trình duyệt thấy cùng origin và gửi cookie như production. Ví dụ Vite: `server: { proxy: { '/api': 'http://localhost:8080' } }`. Với cách này không cần CORS và `fetch` không cần `credentials`.
- **CORS là phương án dự phòng** (khi không dùng được proxy): đặt `CORS_ORIGINS=http://localhost:5173` (đúng origin của dev server, có thể nhiều origin cách nhau bằng dấu phẩy, không dùng `*`) trong `.env` của backend rồi chạy lại `api`. Khi đó backend cho phép đúng origin đó, có credentials, các method GET/POST/PUT/PATCH/DELETE và header `Content-Type`, `X-Request-ID`. Frontend phải gọi `fetch(url, { credentials: 'include' })`. Cookie là `SameSite=Strict` nên chỉ hoạt động khi dev server và API cùng *site* (cùng host, khác cổng thì được: `localhost:5173` gọi `localhost:8080`; `127.0.0.1` gọi `localhost` thì không).
- Đăng nhập bằng cookie HttpOnly, SameSite=Strict: `access_token` (hết hạn sau 15 phút) và `refresh_token` (7 ngày). Frontend không đọc được token và không cần lưu gì.
- Chạy local qua http thì giữ `AUTH_COOKIE_SECURE=false` trong `.env` (đã là mặc định của `.env.example`). Khi deploy có TLS phải là `true`.
- Khi một API trả 401 vì access token hết hạn: gọi `POST /api/auth/refresh`, nếu thành công thì thử lại request đó một lần. Nếu refresh cũng trả 401 thì chuyển về trang đăng nhập.

### Các endpoint hiện có

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
| POST | `/api/projects/{id}/repositories` | Import repo GitHub (body: `repoUrl`, `ref` tuỳ chọn; bỏ `ref` thì index nhánh mặc định của repo). Trả 202 |
| GET | `/api/repositories/{id}` | Trạng thái repository, dùng để poll |
| GET | `/api/repositories/{id}/files` | Code viewer: đọc một đoạn file của bản đã index (`?path=&start=&end=`, xem "Code viewer" bên dưới) |
| DELETE | `/api/repositories/{id}` | Xoá repository |
| GET, POST | `/api/projects/{id}/conversations` | Danh sách, tạo hội thoại (body: `title` tuỳ chọn, cùng luật title với đổi tên, xem "Quản lý hội thoại"; `initialRepositoryId` tuỳ chọn ở chế độ `single`, bắt buộc ở `multi`, xem "Conversation Scope") |
| PATCH | `/api/conversations/{id}` | Đổi tên hội thoại (body: `title`, xem "Quản lý hội thoại" bên dưới). Trả 200 với `ConversationDto` |
| DELETE | `/api/conversations/{id}` | Xoá hội thoại cùng toàn bộ tin nhắn và citation của nó. Trả 204 |
| GET | `/api/conversations/{id}/repositories` | Conversation Scope: các repository của hội thoại (xem "Conversation Scope") |
| POST | `/api/conversations/{id}/repositories` | Thêm một repository vào Scope (body: `repositoryId`). Trả 200 với Scope sau khi thêm |
| DELETE | `/api/conversations/{id}/repositories/{repositoryId}` | Bỏ một repository khỏi Scope. Trả 204 |
| GET | `/api/conversations/{id}/messages` | Lịch sử tin nhắn (`?limit=`, mặc định 200, tối đa 500) |
| POST | `/api/conversations/{id}/messages` | Hỏi một câu (body: `question` tối đa 2000 ký tự, `topK` từ 1 đến 20 tuỳ chọn) |

Cần gì thêm thì báo để thống nhất trước khi backend làm.

`repoUrl` chỉ nhận đúng dạng `https://github.com/<owner>/<repo>`. Backend chấp nhận thêm đuôi `.git` hoặc một dấu `/` cuối và lưu về dạng chuẩn không có chúng (`.../flask.git` thành `.../flask`).

**Repository đang hoạt động (active):** mỗi project chỉ hỏi được trên **một** repository, là repository **import sau cùng**; `isActive: true` đánh dấu nó. Import thêm repo thứ hai vào cùng project thì chat chuyển sang repo mới, repo cũ vẫn xem được nhưng chưa có API đổi lại. Xoá repo đang active thì repo mới nhất còn lại trở thành active. Muốn hỏi hai repo khác nhau thì tạo hai project.

**Thay đổi so với trước (ảnh hưởng frontend):** `ref` là tuỳ chọn thật sự. Bỏ `ref` thì backend không gửi `ref` cho RAG, RAG dùng nhánh mặc định của repo (`master`, `develop`, ...; trước đây backend tự điền `main` nên repo dùng `master` bị lỗi). Vì vậy trường `ref` trong response của repository (`POST`/`GET /api/repositories/...`, `GET /api/projects/{id}`) **có thể là `null`**. Frontend nên hiển thị "nhánh mặc định" khi `ref` là `null`, đừng giả định luôn có chuỗi. Import cùng URL lần hai với cùng `ref` (kể cả cùng bỏ trống) trả 409; bỏ trống và `main` tường minh được tính là hai lần import khác nhau.

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
| `REPOSITORY_NOT_READY` | 409 | Repository chưa `ready` nên chưa tạo hội thoại với nó / chưa thêm vào Scope được (khác `INDEX_NOT_READY` là lỗi lúc hỏi) |
| `SCOPE_LIMIT_REACHED` | 409 | Scope đã đủ số repository tối đa (mặc định 5), hãy bỏ bớt một repository rồi thêm lại |
| `SCOPE_CANNOT_BE_EMPTY` | 409 | Không bỏ được repository cuối cùng của Scope |
| `INVALID_REPO`, `REPO_TOO_LARGE` | 422 | URL không hỗ trợ hoặc repository quá lớn |
| `INVALID_REQUEST` | 422 | RAG từ chối request không hợp lệ (validation). Thông báo chung "Yêu cầu không hợp lệ" |
| `LLM_ERROR` | 502 | Hệ thống AI lỗi tạm thời, cho thử lại |
| `RAG_UNAVAILABLE` | 502 | Không kết nối được hệ thống AI |
| `TIMEOUT` | 504 | Hệ thống AI trả lời quá hạn, cho thử lại |
| `RATE_LIMITED` | 429 | Gửi quá nhanh. Đọc header `Retry-After` (giây) rồi cho thử lại, đừng thử lại ngay |
| `IMPORT_IN_PROGRESS` | 409 | Người dùng đang có repository khác đang index (mặc định 1 cái một lúc). Chờ nó `ready` hoặc `failed` rồi import tiếp |
| `REQUEST_TOO_LARGE` | 413 | Body quá 64 KB (mặc định) |
| `FILE_NOT_FOUND` | 404 | File không còn trong bản đã index (code viewer): hiển thị snippet đã lưu trong citation kèm nhãn "phiên bản cũ" |
| `INDEX_NOT_FOUND` | 404 | RAG không còn chỉ mục của repository (code viewer) |
| `INVALID_RANGE` | 400 | Khoảng dòng sai: `start` < 1, `end` < `start`, hoặc quá 2000 dòng (code viewer) |
| `REPOSITORY_FAILED` | 409 | (cũng dùng cho code viewer) repository đã index thất bại |

Timeout từ frontend tới backend: **75 giây cho `POST /api/conversations/{id}/messages`** (backend chờ RAG tối đa 65 giây) và 60 giây cho các API còn lại.

### Giới hạn tốc độ và kích thước

Giá trị mặc định (đổi bằng biến môi trường, xem `.env.example`; đặt 0 để tắt từng giới hạn):

| Giới hạn | Mặc định | Áp dụng cho |
| --- | --- | --- |
| Đăng ký, đăng nhập, refresh | 10 lần/phút | mỗi địa chỉ IP, dùng chung cho cả ba |
| Gửi câu hỏi | 10 lần/phút | mỗi user |
| Import repository | 5 lần/10 phút | mỗi user |
| Repository đang index cùng lúc | 1 | mỗi user (qua mọi project) |
| Kích thước body | 64 KB | mọi request |

Vượt giới hạn tốc độ: `429` kèm `code: "RATE_LIMITED"` và header `Retry-After`. Hiển thị "Bạn thao tác quá nhanh, thử lại sau N giây" và khoá nút gửi trong N giây. Nếu nhiều người dùng chung một IP (lớp học, văn phòng) và gặp giới hạn đăng nhập, tăng `RATE_LIMIT_AUTH_PER_MINUTE`.

**Chạy sau nginx:** đặt `TRUSTED_PROXIES` là địa chỉ hoặc dải mạng của nginx (ví dụ `172.18.0.0/16` trong mạng compose), nginx phải gửi `X-Forwarded-For`. Không đặt thì backend bỏ qua header đó và coi mọi client là địa chỉ của nginx, làm giới hạn theo IP áp lên tất cả người dùng cùng lúc.

Mọi response có `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` và `Cache-Control: no-store`. `Strict-Transport-Security` do nginx đặt (TLS kết thúc ở đó).

### Code viewer

`GET /api/repositories/{id}/files?path=<đường dẫn>&start=<dòng đầu>&end=<dòng cuối>` (cần đăng nhập).

- `path` bắt buộc: đường dẫn tương đối so với gốc repo, đúng như `filePath` trong citation (ví dụ `src/flask/app.py`, **không** có `/` đầu). Tối đa 1024 ký tự, không có đoạn nào đúng bằng `..` (tách theo `/` và `\`; tên như `v1..2.md` hay `..hidden` vẫn hợp lệ), không bắt đầu bằng `/` hoặc `\`, không có ký tự điều khiển. Sai thì 400 kèm `errors.Path`.
- `start` mặc định 1, `end` mặc định `start + 1999`. Tối đa **2000 dòng** mỗi lần (`end - start + 1 <= 2000`). Sai khoảng dòng thì 400 với `code: "INVALID_RANGE"`. Muốn xem file dài hơn thì gọi nhiều lần. Nếu `end` vượt cuối file, RAG cắt về dòng cuối và `end` trong response là giá trị đã cắt. `start` vượt cuối file là 400 `INVALID_RANGE`.
- Chỉ chủ sở hữu đọc được. Repository của người khác và repository không tồn tại cho **cùng một** 404.
- Repository chưa `ready` thì 409: `INDEX_NOT_READY` (đang index), `NO_REPOSITORY` (chưa có chỉ mục), `REPOSITORY_FAILED` (index thất bại). Backend không hỏi RAG trong các trường hợp này.
- `404 FILE_NOT_FOUND`: file không còn trong bản đã index. Frontend hiển thị `snippet` đã lưu trong citation kèm nhãn "phiên bản cũ" (D14). `404 INDEX_NOT_FOUND`: RAG đã mất chỉ mục. `502 RAG_UNAVAILABLE` và `504 TIMEOUT` như các API khác.

Response (camelCase):

```json
{ "path": "src/flask/app.py", "start": 34, "end": 36, "content": "def verify_token(...)\n...", "language": "python", "commitSha": "d086db856be1..." }
```

`language` có thể là `null` (RAG không nhận ra ngôn ngữ), `commitSha` là commit repository đã được index (có thể `null`). `content` là **nguyên văn** (kể cả `<script>` hay markdown): frontend phải escape khi render. Mốc dòng trong citation (`startLine`, `endLine`) dùng được trực tiếp làm `start`, `end`.

### Quản lý hội thoại

**Luật title (dùng chung cho tạo và đổi tên):** backend cắt khoảng trắng hai đầu rồi mới kiểm tra. Title phải dài 1 đến **200** ký tự và không chứa ký tự điều khiển (xuống dòng, tab, ...); backend lưu và trả về bản đã cắt, nên frontend nên hiển thị giá trị trong response thay vì giá trị người dùng gõ. Khi **tạo** (`POST`), `title` vẫn tuỳ chọn: bỏ trống hoặc `null` thì dùng tên mặc định "New conversation" (sau câu hỏi đầu tiên tự đổi thành tên theo câu hỏi); khi **đổi tên** (`PATCH`) thì bắt buộc. Title sai thì 400 với `errors.Title`.

**Đổi tên:** `PATCH /api/conversations/{id}` với body `{ "title": "..." }`.

- `title` bắt buộc, theo luật title ở trên.
- Thành công: **200** với `{ id, projectId, title, createdAt }` (cùng dạng với response của `POST /api/projects/{id}/conversations`; không có `lastActivityAt` và `messageCount`, hãy giữ các giá trị đó từ danh sách đã tải).
- Title sai: **400** ProblemDetails, `errors.Title` là danh sách thông báo (cùng định dạng các lỗi validate khác).

**Xoá:** `DELETE /api/conversations/{id}` trả **204**, không có body.

- Xoá cứng và không hoàn tác: hội thoại, toàn bộ tin nhắn và citation đã lưu của nó biến mất. Nên hỏi xác nhận trước khi gọi. Các hội thoại khác và repository không bị ảnh hưởng, RAG không được gọi.
- Sau khi xoá, mọi gọi tới hội thoại đó (xem tin nhắn, hỏi tiếp, đổi tên, xoá lần nữa) đều trả 404.

Cả hai: hội thoại của người khác và hội thoại không tồn tại cho cùng một **404** (kiểm tra quyền trước khi validate body, nên body sai trên hội thoại của người khác vẫn là 404). Chưa đăng nhập thì 401.

### Conversation Scope (D40, đang làm)

Mỗi hội thoại có một **Scope**: tập repository mà hội thoại được phép tìm trong đó (tối đa `CONVERSATION_SCOPE_MAX`, mặc định 5). Bước này mới có phần **quản lý Scope**; **luồng hỏi chưa đổi** (câu hỏi vẫn được trả lời từ repository active của project, xem "Repository đang hoạt động"). Focus (ưu tiên repository cho từng câu hỏi) và snapshot lịch sử ở bước tiếp theo. Mọi response ở đây **không bao giờ có `indexId`/`index_id`**.

**Tạo hội thoại với repository đầu tiên:** `POST /api/projects/{id}/conversations`

```json
{ "title": "Luồng đơn hàng", "initialRepositoryId": "5b0c0a3e-3f7e-4b6a-9d2e-0d6f1b8f2a11" }
```

Response vẫn là `{ id, projectId, title, createdAt }`, không đổi dạng. Hội thoại và thành viên Scope đầu tiên được tạo cùng một lúc (một transaction).

- `initialRepositoryId` phải là repository `ready` của **chính project này**. Không phải (của project khác, của người khác, không tồn tại) thì **404** cùng thông báo với "project không tồn tại". Có nhưng chưa `ready` thì **409 `REPOSITORY_NOT_READY`**.
- **Tuỳ chọn** khi `RAG_QUERY_API=single` (mặc định): bỏ qua thì backend dùng repository active của project nếu nó đã `ready`, không thì hội thoại bắt đầu với Scope rỗng, đúng như trước. **Bắt buộc** khi `RAG_QUERY_API=multi`: thiếu thì **400** với `errors.InitialRepositoryId`. Frontend nên gửi luôn từ bây giờ.
- Thứ tự kiểm tra: project (404) → repository (404) → body (400) → trạng thái (409).

**Xem Scope:** `GET /api/conversations/{id}/repositories` (xếp theo thời điểm thêm, cũ trước)

```json
[
  { "repositoryId": "5b0c0a3e-3f7e-4b6a-9d2e-0d6f1b8f2a11", "displayName": "pallets/flask", "repoUrl": "https://github.com/pallets/flask",
    "status": "ready", "commitSha": "d086db856be1...", "addedAt": "2026-11-26T08:15:02.113+00:00" }
]
```

`displayName` là `owner/repo` lấy từ `repoUrl`. `status` là trạng thái hiện tại của repository (có thể không còn `ready` nếu nó lỗi sau khi được thêm), `commitSha` có thể `null`. Scope rỗng trả `[]`.

**Thêm:** `POST /api/conversations/{id}/repositories` với `{ "repositoryId": "..." }`. Trả **200** với toàn bộ Scope sau khi thêm (cùng dạng `GET`).

- Repository phải thuộc cùng project với hội thoại, không thì 404. Chưa `ready` thì 409 `REPOSITORY_NOT_READY`.
- Đã có trong Scope thì vẫn **200** và không đổi gì (gọi lại an toàn, kể cả khi Scope đã đầy).
- Scope đã đủ số tối đa thì **409 `SCOPE_LIMIT_REACHED`**, Scope giữ nguyên. Giới hạn do backend giữ, kể cả khi hai yêu cầu đến cùng lúc. Nút "+ Add repository" vẫn nên luôn hiện; khi đủ thì giải thích giới hạn và không cho thêm.
- Thiếu `repositoryId` thì 400 với `errors.RepositoryId`.

**Bỏ:** `DELETE /api/conversations/{id}/repositories/{repositoryId}` trả **204**.

- Repository không nằm trong Scope (hoặc không tồn tại) thì **404**. Là thành viên cuối cùng thì **409 `SCOPE_CANNOT_BE_EMPTY`** và không bỏ.
- Chỉ bỏ khỏi Scope: repository, tin nhắn và citation cũ không bị ảnh hưởng.

**Chung:** hội thoại của người khác và hội thoại không tồn tại cho cùng một **404** (kiểm tra trước mọi validate, nên body sai trên hội thoại của người khác vẫn là 404); chưa đăng nhập thì 401. Xoá một repository (`DELETE /api/repositories/{id}`) thì nó tự biến khỏi mọi Scope, còn tin nhắn và citation cũ giữ nguyên; Scope có thể thành rỗng (khi hỏi bằng luồng mới sẽ là 409 `NO_REPOSITORY_IN_SCOPE`, chưa có ở bước này). Hội thoại tạo trước bản này được tạo sẵn Scope gồm repository active của project (nếu có), nếu không thì Scope rỗng.

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
- Không sửa `contracts/openapi.yaml` hay repo RAG. Cần đổi contract thì báo Giang (cách cập nhật bản pin: [contracts/README.md](contracts/README.md)).

## Tài liệu và tiến độ

Báo cáo tiến độ nằm trong [docs/](docs/README.md). Báo cáo mới nhất: [Tiến độ đến Phase E](docs/reports/2026-11-06-progress-phase-a-e.md).
