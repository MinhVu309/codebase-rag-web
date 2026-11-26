Đã được hợp nhất vào Contract v2, duyệt 25/11

# Phản hồi của Web Backend về D40 (Multi-Index API Contract Proposal)

Ngày: 23/11/2026
Người review: Phạm Minh Vũ (Web Backend)
Tài liệu được review:
- `docs/d40-multi-index-api-contract-proposal.md` và `docs/web-team-d40-implementation-and-test-plan.md` (repo `codebase-rag-service`, nhánh `work/wp6-retrieval-reranking`, commit `7379b6b`)
- Notion: W17, W18
Code đã đối chiếu: `codebase-rag-web` `main` tại `878602b`, `codebase-rag-service` `7379b6b`.

Phần giao diện (cách hiển thị scope, focus, thông báo repo mới, trạng thái từng nguồn) do thành viên Frontend phản hồi riêng. Tài liệu này chỉ nói phần Web Backend và contract.

## Tóm tắt

**Đồng ý hướng chung:** Architecture B (RAG điều phối nhiều index), Scope là ranh giới cứng, Focus là ưu tiên mềm, lưu snapshot H2, trả kết quả một phần (E2), giữ nguyên `POST /v1/query`.

**Cần điều chỉnh trước khi chốt (4 điểm chính):**
1. `index_id` không được trả ra trình duyệt. Proposal đề xuất thêm `IndexId` vào `CitationDto` và trả `RetrievalSnapshot` thô trong `MessageDto`, như vậy trái D11 và trái test bảo mật hiện có (`index_id` không xuất hiện trong response nào). Web lưu `index_id` trong DB, chỉ trả cho frontend `repositoryId`, `commitSha` và trạng thái từng nguồn.
2. Web API dùng camelCase (`topK`, `filePath`...). Các field mới phải là `initialRepositoryId`, `focusRepositoryIds`, không dùng snake_case như ví dụ trong proposal.
3. Fixture test không chạy được như mô tả: RAG hiện chỉ lập chỉ mục file `.py` (`rag/ingestion/filters.py`, `allowed_extensions = {".py"}`), nên "Repo B frontend" dạng `.tsx` sẽ không có chunk nào. Cần đổi fixture sang hai repo Python, hoặc RAG phải hỗ trợ thêm ngôn ngữ (nằm ngoài phạm vi D40).
4. Giới hạn scope: Web đề xuất mặc định 5 repo (thay vì 10) và chặn ngay lúc thêm vào scope, để lúc hỏi không bao giờ vượt giới hạn.

## 1. Ý kiến theo từng nội dung

### 1.1 API contract phía RAG

| Nội dung | Ý kiến | Đề xuất |
|---|---|---|
| Endpoint `POST /v1/query/multi` hay `POST /v2/query` | Đồng ý `POST /v1/query/multi` | Giữ `v1` vì dùng chung xác thực `X-API-Key`, chung envelope lỗi; `v2` ngụ ý đổi phiên bản cả API. |
| `scope[]` gồm `repository_id`, `index_id`, `display_name` | Đồng ý | `display_name` là `owner/repo` lấy từ URL GitHub đã chuẩn hóa, không phải tên do người dùng tự đặt. Chỉ dùng để hiển thị trong câu trả lời. |
| `focus_repository_ids` | Đồng ý | Web chỉ gửi khi đã kiểm tra là tập con của scope; RAG vẫn kiểm tra lại. |
| `top_k` là giới hạn chung sau khi hợp nhất, 1–20 | Đồng ý | Số candidate lấy từ mỗi index là chuyện nội bộ của RAG, Web không truyền. |
| `options`, `debug` | Đồng ý | Web gửi `debug=false` trong luồng thường. |
| Response: `execution_status`, `answer_status`, `focus`, `repository_outcomes`, `citations` | Đồng ý | Yêu cầu RAG cam kết: mỗi phần tử trong `scope` có đúng một `repository_outcomes`; mỗi citation luôn có `repository_id` và `index_id` (bắt buộc), `commit_sha` (có thể null nếu RAG không biết). |
| HTTP 200 cho partial và no-evidence | Đồng ý | Không coi no-evidence là lỗi. |
| 503 `ALL_INDEXES_UNAVAILABLE` | Đồng ý | Cần thêm vào danh sách mã lỗi ở Notion trang 03. Web chuyển thành 503 `ALL_SOURCES_UNAVAILABLE` cho frontend. |
| 422 khi scope vượt giới hạn, focus không thuộc scope, ID trùng | Đồng ý | Web chặn trước nên trường hợp này chỉ xảy ra khi hai bên cấu hình lệch nhau. |
| Không tự động retry POST | Đồng ý | Web hiện đã không retry `/v1/query`, giữ nguyên cho `/v1/query/multi`. |

### 1.2 Web API (mục 9.1 của proposal)

| Route | Ý kiến | Đề xuất |
|---|---|---|
| `POST /api/projects/{projectId}/conversations` bắt buộc `initial_repository_id` | Đồng ý, đổi tên field | Body `{ "title"?: string, "initialRepositoryId": guid }`. Repo không thuộc project của user → 404 (giống hiện nay, không lộ thông tin). Repo chưa READY → 409 `REPOSITORY_NOT_READY`. Tạo conversation và dòng scope đầu tiên trong một transaction. |
| `GET /api/conversations/{id}/repositories` | Đồng ý | Trả `repositoryId`, `displayName`, `repoUrl`, `status`, `commitSha`, `addedAt`. Không có `index_id`. |
| `POST /api/conversations/{id}/repositories` | Đồng ý | Body `{ "repositoryId": guid }`. Thêm trùng → 200 trả scope hiện tại (idempotent). Repo khác project hoặc của người khác → 404. Chưa READY → 409 `REPOSITORY_NOT_READY`. Đủ giới hạn → 409 `SCOPE_LIMIT_REACHED`. |
| `DELETE /api/conversations/{id}/repositories/{repositoryId}` | Đồng ý, thêm một luật | Trả 204. Không cho xóa repo cuối cùng khỏi scope (409 `SCOPE_CANNOT_BE_EMPTY`), để giữ bất biến "conversation luôn có ít nhất một repo". Scope chỉ rỗng khi repo bị xóa khỏi project; khi đó hỏi sẽ trả 409 `NO_REPOSITORY_IN_SCOPE`. |
| `POST /api/conversations/{id}/messages` thêm focus | Đồng ý, đổi tên field | Body `{ "question", "topK"?, "focusRepositoryIds"?: guid[] }`. Rỗng hoặc bỏ trống = tìm cả scope. ID trùng → 400. ID không thuộc scope (kể cả repo của người khác) → 400 `FOCUS_NOT_IN_SCOPE`, không phân biệt "không tồn tại" và "không thuộc scope" để không lộ thông tin. |
| `GET /api/conversations/{id}/messages` thêm snapshot | Cần điều chỉnh | Không trả `RetrievalSnapshot` thô (có `index_id`). Trả dạng đã lọc: `executionStatus`, `answerStatus`, `focusRepositoryIds`, `sources: [{ repositoryId, displayName, focused, usedAsFallback, outcome, reason }]`. |
| `CitationDto` thêm `IndexId` | Không đồng ý | Trái D11 và test hiện có. Ở Web, mỗi Repository gắn với đúng một index suốt vòng đời (không có re-index; import lại tạo Repository mới), nên `repositoryId` + `commitSha` đã phân biệt được. `IndexId` chỉ lưu trong DB. Thêm `displayName` vào citation để frontend hiện tên repo cạnh đường dẫn file (R02). |
| Thông báo "repo mới READY, thêm vào hội thoại?" | Đồng ý, không cần endpoint mới | Frontend so danh sách repo READY của project (đã có) với scope của conversation. Cần owner chốt: khi người dùng bấm bỏ qua, gợi ý có hiện lại sau khi tải lại trang không. MVP đề xuất lưu phía frontend, backend không lưu. |
| Bỏ `Project.ActiveIndexId` (D12) | Bổ sung | Sau khi chuyển hẳn sang scope, bỏ khái niệm "repo active" và field `isActive` trong `RepositoryDto`. Frontend cần biết trước. |

### 1.3 Scope và Focus

Đồng ý: Scope là ranh giới cứng, Focus là ưu tiên mềm, RAG chỉ fallback trong scope.

Web bảo đảm ranh giới ở ba lớp:
1. Thêm vào scope: kiểm tra chủ sở hữu, cùng project, trạng thái READY.
2. Lúc hỏi: focus phải là tập con của scope hiện tại.
3. Gửi RAG: `index_id` luôn lấy từ DB, không bao giờ nhận từ client (F11, F12).

Bổ sung một trường hợp proposal chưa nói: repo nằm trong scope nhưng lúc hỏi không còn READY (ví dụ đang bị xóa). Web không gửi repo đó cho RAG, nhưng vẫn ghi vào snapshot với `outcome = unavailable`, `reason = repository_not_ready` hoặc `repository_deleted`, để không bị bỏ sót âm thầm. Nếu không còn repo nào gửi được thì trả 409 `NO_REPOSITORY_IN_SCOPE` và không gọi RAG.

### 1.4 Truy vấn nhiều index

Đồng ý. Web gửi toàn bộ scope đã kiểm tra kèm focus trong một request. Web không tự chia request theo từng index và không ghép nhiều câu trả lời.

### 1.5 Lịch sử và citation (H2)

Đồng ý lưu snapshot JSONB `RetrievalSnapshot` có `schema_version` trên tin nhắn trả lời. Bổ sung:
- Lưu snapshot cả khi lượt hỏi lỗi (scope và focus lúc gửi, mã lỗi), để F13 vẫn hiển thị được.
- Tin nhắn của người dùng lưu focus đã chọn.
- Citation trong DB lưu thêm `IndexId` và `DisplayName`. Response cho frontend chỉ có `DisplayName`.
- Xóa repo khỏi scope hoặc khỏi project không sửa snapshot cũ. Bảng `ConversationRepository` xóa theo cascade khi repo bị xóa, nhưng snapshot JSONB trên message không bị ảnh hưởng.

### 1.6 Xử lý lỗi

Đồng ý enum `searched_with_evidence`, `searched_no_evidence`, `unavailable`, `failed`, `not_searched`. Đề xuất chốt luôn danh sách `reason` để Web map ra giao diện:

| outcome | reason |
|---|---|
| `not_searched` | `focus_evidence_sufficient` |
| `unavailable` | `index_not_found`, `index_not_ready`, `storage_error`, `timeout` (RAG); `repository_not_ready`, `repository_deleted` (Web tự ghi) |
| `failed` | `retrieval_error` |

`fallback_reason` cũng cần danh sách cố định, ví dụ `focused_evidence_insufficient`, `focused_sources_unavailable`.

### 1.7 Timeout và giới hạn

- **Timeout:** benchmark Dev của RAG cho thấy truy xuất Hybrid mất khoảng 10 ms mỗi index, nên thời gian chủ yếu vẫn là LLM. Web đề xuất RAG giữ một deadline tuyệt đối ≤ 60 giây cho cả truy xuất lẫn sinh câu trả lời. Khi đó chuỗi 60/65/75 giây (D39) giữ nguyên. Nếu việc nạp index nguội (Qdrant, BM25) làm vượt mốc này, RAG báo số đo để hai bên chỉnh lại.
- **Giới hạn scope:** Web đề xuất mặc định 5 repo mỗi conversation. Demo chỉ cần 2–3 repo; RAG chạy một tiến trình với Qdrant cục bộ và BM25 trong RAM. Giá trị là cấu hình ở cả hai phía; Web chặn khi thêm vào scope.
- **Rate limit:** giữ giới hạn hiện có (10 câu hỏi mỗi phút mỗi người dùng) cho luồng nhiều index.

### 1.8 Tương thích ngược

Đồng ý giữ nguyên `POST /v1/query`. Kế hoạch chuyển đổi phía Web:
- Thêm cấu hình `RAG_QUERY_API=single|multi`, mặc định `single` cho tới khi luồng nhiều index qua E2E; có thể quay lại nếu lỗi.
- Migration: conversation cũ được gán scope bằng repo đang active của project (nếu còn). Nếu không có, scope rỗng và hỏi trả 409 `NO_REPOSITORY_IN_SCOPE`.
- Khi `openapi.yaml` mới được duyệt: Web pin lại file, cập nhật contract test và mock Prism.

## 2. Phát hiện thêm khi đối chiếu code

1. **Fixture test (mục 5.1 của test plan):** RAG chỉ lập chỉ mục file `.py`, nên Repo B dạng React/TSX không có chunk nào. Đề xuất: Repo A là service Python (FastAPI) định nghĩa `/api/orders/{order_id}` và field `total_after_discount`; Repo B là client Python (CLI hoặc SDK) gọi endpoint đó và đọc field. Hai repo vẫn có một symbol trùng tên để test R02.
2. **Notion trang 01**, mục "Cập nhật Web Backend cho Frontend", vẫn còn câu về D38 (mỗi conversation một repo cố định). D40 đã thay quyết định này, nên cần sửa để frontend không làm theo bản cũ.
3. **Q05 (chuỗi timeout D39):** Web hiện đã đặt 65 giây (Web → RAG) và khuyến nghị frontend 75 giây, khớp với D39.

## 3. Trả lời 10 câu hỏi mở (mục 11 của proposal)

1. **Tên endpoint:** `POST /v1/query/multi`.
2. **Giới hạn scope:** mặc định 5, cấu hình được; chốt lại sau khi RAG đo tải với 5 index.
3. **`top_k` và budget mỗi index:** `top_k` là giới hạn chung sau khi hợp nhất (1–20). Budget mỗi index và việc chia đều để một repo không chiếm hết ngữ cảnh là quyết định nội bộ của RAG, nhưng cần ghi trong tài liệu.
4. **Chính sách đủ evidence:** thuộc RAG. Web chỉ cần `fallback_triggered`, `fallback_reason` theo danh sách cố định và outcome của từng repo để lưu và hiển thị.
5. **Song song và deadline:** RAG truy xuất song song có giới hạn (ví dụ tối đa bằng giới hạn scope); một deadline tuyệt đối ≤ 60 giây cho toàn request.
6. **Enum và reason:** như bảng ở mục 1.6.
7. **Có trả `repository_id`, `index_id`, `commit_sha` cho mọi citation và outcome không:** Có. `repository_id`, `index_id` bắt buộc; `commit_sha` có thể null.
8. **Migration và endpoint phía Web:** bảng `ConversationRepository`; cột `RetrievalSnapshot` (JSONB) trên message; backfill scope cho conversation cũ; sau đó bỏ `Project.ActiveIndexId`. Endpoint như mục 1.2.
9. **Re-index:** Web không có re-index; import lại tạo Repository mới với index mới, Repository cũ giữ nguyên. Snapshot cũ luôn trỏ đúng index/commit đã dùng. Nếu sau này thêm re-index, snapshot vẫn đúng vì đã lưu `index_id` và `commit_sha` lúc trả lời.
10. **Hiển thị partial và phân biệt no-evidence với lỗi:** Web trả `executionStatus`, `answerStatus` và `sources[]` có `outcome`/`reason`. Frontend hiện câu trả lời bình thường kèm cảnh báo nguồn lỗi khi `partial`; khi `no_evidence` hiện thông báo kiểm tra scope/focus; khi 503 hiện lỗi "không truy vấn được repo nào". Chi tiết giao diện do Frontend chốt.

## 4. Phạm vi việc phía Web Backend sau khi contract được duyệt

- Migration: `ConversationRepository`, `Message.RetrievalSnapshot`, backfill scope; bỏ `ActiveIndexId` ở bước sau.
- API quản lý scope, mở rộng tạo conversation và gửi câu hỏi; DTO mới đã lọc `index_id`.
- `IRagClient.QueryMultiAsync`, map lỗi mới (503, partial, no-evidence), cấu hình `RAG_QUERY_API`.
- Test: mở rộng ma trận quyền sở hữu cho các endpoint mới; F11, F12, Q01; snapshot sau khi xóa repo (R06, R07); test so với ví dụ trong `openapi.yaml` mới.
- Ước lượng sơ bộ: khoảng 3–4 ngày làm việc sau khi `openapi.yaml` mới được duyệt, chưa tính E2E cùng frontend.

## 5. Cần chốt trước khi viết `openapi.yaml`

- [ ] Tên endpoint `POST /v1/query/multi`.
- [ ] `index_id` không trả ra trình duyệt; bỏ đề xuất thêm `IndexId` vào `CitationDto`.
- [ ] Giới hạn scope mặc định 5, chặn khi thêm vào scope.
- [ ] Danh sách `reason` và `fallback_reason`.
- [ ] Deadline tuyệt đối của RAG ≤ 60 giây cho toàn request.
- [ ] Fixture test dùng hai repo Python.
- [ ] Không cho xóa repo cuối cùng khỏi scope.
- [ ] Gợi ý "repo mới" bị bỏ qua có hiện lại không (lưu phía frontend hay backend).
- [ ] Sửa câu D38 ở Notion trang 01.
