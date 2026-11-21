# Báo cáo: rà soát và tăng cường bảo mật backend

Ngày: 2026-11-21
Phạm vi: nhánh `feature/security-hardening` (từ `main` tại `f0cf6ed`)

## Tóm tắt

Backend đã có sẵn nền bảo mật tốt (token hash, xoay refresh token, kiểm quyền theo owner, không log nội dung người dùng). Nhánh này thêm các phần còn thiếu: giới hạn tốc độ, giới hạn import đồng thời, CORS tùy chọn, security header, giới hạn kích thước body, cảnh báo cookie không `Secure`; đồng thời bổ sung test chứng minh quyền sở hữu của mọi endpoint có id và test chứng minh log không chứa dữ liệu nhạy cảm. Không tìm thấy lỗ hổng cần vá ở code cũ. `dotnet list package --vulnerable --include-transitive`: không có package nào bị báo. Toàn bộ test: **537 pass** (Application 137, Api 400; trước nhánh này là 474). Không đổi contract, không thêm package.

## Checklist theo quyết định

### D10: frontend không gọi RAG, Web Backend giữ `RAG_API_KEY`

| Việc | Kết quả |
| --- | --- |
| Key chỉ nằm trong biến môi trường của container `api` và `.env` (không commit) | Đạt. `.env` bị git ignore; `.env.example` chỉ có giá trị giả. |
| Key chỉ gửi trong header `X-API-Key` tới RAG, không bao giờ ghi log | Đạt. Test cũ ở `HttpRagClientTests` và test hành trình đầy đủ `LogHygieneTests` mới. |
| Frontend không có đường nào tới RAG; thông điệp lỗi của RAG không lộ ra ngoài | Đạt. Lỗi RAG được map về mã và câu cố định; `UpstreamMessage` chỉ lưu trong exception, không log, không trả về (rà bằng grep). RAG local chỉ bind `127.0.0.1`. |
| `index_id` của RAG không xuất hiện trong response nào | Đạt. Test mới `The_Index_Id_Never_Appears_In_Any_Response` quét 10 response (project, repository, file, hội thoại, câu trả lời, lỗi). |

### D11: kiểm quyền người dùng với project/index trước khi proxy

| Việc | Kết quả |
| --- | --- |
| Liệt kê mọi endpoint có id | 13 endpoint (project 3, import, repository 3, hội thoại 2 danh sách/tạo, đổi tên, xóa, đọc tin nhắn, gửi câu hỏi). |
| Mỗi endpoint có test "user khác thì 404" | Đạt. `OwnershipMatrixTests` chạy cả 13: người lạ nhận 404, body giống hệt id không tồn tại (khác nhau chỉ ở `traceId`), người chưa đăng nhập 401, dữ liệu của chủ không đổi, chủ vẫn truy cập được. |
| Endpoint mới quên test | Không thể quên: test `Every_Route_With_An_Id_Is_In_The_Matrix` đọc bảng route thật và fail nếu có route có id mà chưa nằm trong ma trận. |
| Quyền được kiểm trước validate body | Đạt (đã có test từ trước cho hội thoại và file). |

### D21: chỉ nhận repo GitHub public dạng `https://github.com/<owner>/<repo>`

| Việc | Kết quả |
| --- | --- |
| Allowlist bằng regex cho owner/repo; chuẩn hóa `.git` và `/` cuối; ref không bắt đầu bằng `-`, không chứa `..`, `//`, `.lock` | Đạt, có test đơn vị từ trước. |
| Chạy thật | URL gitlab, `http://`, chỉ có owner, rỗng đều 400 (E2E 21/11). Repo không tồn tại hoặc private đều `failed` với `REPO_NOT_FOUND`. |
| Chống lạm dụng import | Mới: tối đa 5 import mỗi 10 phút mỗi user và 1 repository đang index cùng lúc mỗi user (409 `IMPORT_IN_PROGRESS`). |

### D22: không log question/secret, giới hạn độ dài, escape khi render

| Việc | Kết quả |
| --- | --- |
| Không log question, title, path, nội dung file, token, cookie, key, mật khẩu, email | Đạt. Rà toàn bộ `backend/src`: chỉ có 9 lệnh log (tính cả cảnh báo khởi động mới), chỉ ghi id, mã lỗi, độ trễ, số citation. Test mới `LogHygieneTests` chạy cả hành trình (đăng ký, đăng nhập sai, refresh, hỏi, lỗi LLM 502/504, title sai, JSON hỏng, đọc file, 404 file, đăng xuất) và xác nhận 14 giá trị nhạy cảm cùng 4 token không xuất hiện trong bất kỳ dòng log nào ở mức Trace. Đã kiểm tra test có phát hiện được rò rỉ bằng cách tạm chèn một dòng log câu hỏi: test fail đúng, đã gỡ. |
| Log của framework | HttpClient của .NET tự che query string (`?*`), EF che giá trị tham số (`'?'`); đã kiểm tra trên log thật của container (4877 dòng, không có path, email, token). Mới: log lệnh SQL của EF hạ xuống Warning (trước đó 429 dòng loại này trong log container sau phiên E2E). |
| Giới hạn độ dài | question 2000, `topK` 1–20, title 200, path 1024, tên project 100, body 64 KB (mới, 413 `REQUEST_TOO_LARGE`, cả với body chunked). Test cho từng giá trị biên đã có (`ChatValidatorTests`, `FileRequestTests`) và test body mới. |
| Escape HTML/markdown khi render | **Thuộc frontend, chưa kiểm chứng được ở đây.** Backend trả nguyên văn (đã xác nhận `<script>` được lưu và trả nguyên). Xem rủi ro 9. |

### Xác thực (D31) rà lại

| Việc | Kết quả |
| --- | --- |
| Cookie | `HttpOnly`, `SameSite=Strict`, `Secure` mặc định bật (chỉ tắt khi `AUTH_COOKIE_SECURE` đúng bằng `false`; `0`, `no`, rỗng vẫn là Secure, có test). Refresh cookie chỉ gửi tới `/api/auth`. Logout xóa cả hai cookie với cùng cờ và path. Mới: khởi động với `AUTH_COOKIE_SECURE=false` ghi cảnh báo vào log. |
| Token | Refresh token lưu hash, xoay mỗi lần dùng, dùng lại token cũ thì thu hồi toàn bộ phiên (đã có test). JWT chỉ nhận HS256, kiểm issuer và audience, không cho lệch giờ, bí mật tối thiểu 32 ký tự. |
| CSRF | `SameSite=Strict` cộng việc mọi endpoint chỉ nhận JSON: form chéo trang gửi `text/plain` hoặc `x-www-form-urlencoded` nhận 415 (test mới). |
| Liệt kê tài khoản | Đăng nhập trả cùng một lỗi và cùng thời gian cho email lạ và sai mật khẩu. Đăng ký trả 409 khi email đã có (chấp nhận, đã có rate limit). |

## Việc đã làm trong nhánh này

1. **Rate limit** bằng `Microsoft.AspNetCore.RateLimiting` (có sẵn trong framework). Vượt giới hạn: 429 ProblemDetails `code=RATE_LIMITED` kèm `Retry-After` (số giây nguyên). Chạy thật trên Kestrel: 10 lần đăng nhập sai đầu nhận 401, lần 11 nhận 429 với `Retry-After: 60`.
2. **Giới hạn import đồng thời**: trước khi đếm, backend cập nhật trạng thái các repository chưa xong từ RAG, nên một job đã xong mà chưa ai poll không giữ chỗ của user. Import thất bại cũng không giữ chỗ.
3. **Trusted proxies**: `X-Forwarded-For` chỉ được tin khi request đến từ địa chỉ trong `TRUSTED_PROXIES`. Không có nó thì mọi người sau nginx dùng chung một IP và bị giới hạn chung; tin header từ bất kỳ ai thì kẻ tấn công đổi IP để né giới hạn. Cả hai trường hợp có test.
4. **CORS** chỉ bật khi có `CORS_ORIGINS`; origin phải đúng dạng `scheme://host[:port]` (không `*`, không path), có credentials, chỉ GET/POST/PUT/PATCH/DELETE, chỉ header `Content-Type` và `X-Request-ID`. Cấu hình sai làm API không khởi động.
5. **Security header** qua một middleware nhỏ: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Content-Security-Policy` (`default-src 'none'; frame-ancestors 'none'`), `Cache-Control: no-store`. Không đặt HSTS ở đây vì TLS kết thúc ở nginx.
6. **Giới hạn body**: kiểm `Content-Length` trước khi đọc, cắt body chunked khi đọc vượt mức; cả hai trả 413 `REQUEST_TOO_LARGE`.
7. **Cấu hình**: mọi giá trị đọc từ biến môi trường, kiểm lúc khởi động (giá trị sai dừng API), đặt 0 để tắt từng giới hạn.

## Giá trị mặc định và lý do

| Biến | Mặc định | Lý do |
| --- | --- | --- |
| `RATE_LIMIT_AUTH_PER_MINUTE` | 10 mỗi IP | Người dùng thật đăng nhập vài lần mỗi phút là nhiều. 10 mỗi phút cho tối đa khoảng 14 nghìn lần thử mỗi ngày từ một IP, đủ làm dò mật khẩu chậm hẳn. Dùng chung cho đăng ký, đăng nhập, refresh. |
| `RATE_LIMIT_ASK_PER_MINUTE` | 10 mỗi user | Mỗi câu hỏi tốn một lượt LLM 3–32 giây (đo 21/11); người thật gửi dưới 5 câu mỗi phút. 10 cho phép gửi liên tiếp mà vẫn chặn script. |
| `RATE_LIMIT_IMPORT_PER_WINDOW` / `_WINDOW_MINUTES` | 5 mỗi 10 phút mỗi user | Mỗi import là một job clone cộng embedding trên RAG; demo hiếm khi cần hơn vài repo. |
| `MAX_CONCURRENT_IMPORTS_PER_USER` | 1 | RAG chỉ có một worker, nên import thứ hai của cùng user chỉ xếp hàng chặn người khác. |
| `MAX_REQUEST_BODY_BYTES` | 65536 | Body lớn nhất hợp lệ là câu hỏi 2000 ký tự (vài KB). |
| `CORS_ORIGINS`, `TRUSTED_PROXIES` | rỗng | Mặc định đóng: cùng origin qua nginx, không tin header chuyển tiếp. |

Hệ quả cần biết: kịch bản E2E đã chạy trước đó (import hai repo cùng lúc, hỏi mỗi 3 giây) sẽ gặp `IMPORT_IN_PROGRESS` và 429 với mặc định này. Chạy lại bộ E2E thì đặt `RATE_LIMIT_ASK_PER_MINUTE=0` và `MAX_CONCURRENT_IMPORTS_PER_USER=2` hoặc 0.

## Rủi ro còn lại

1. **Bộ đếm rate limit nằm trong bộ nhớ của một process.** Chạy một instance `api` thì đúng; chạy nhiều instance thì mỗi instance đếm riêng (giới hạn thực tế nhân lên) và khởi động lại thì đếm lại từ 0.
2. **Giới hạn đăng nhập theo IP, không theo tài khoản.** Nhiều người chung một IP (lớp học, văn phòng) dùng chung 10 lần mỗi phút; ngược lại kẻ tấn công đổi IP vẫn thử được một tài khoản. Chưa khóa theo tài khoản vì dễ bị lợi dụng để khóa người khác.
3. **Không có trần tổng cho toàn hệ thống.** Nhiều user mỗi người một import vẫn xếp hàng ở RAG một worker; không giới hạn số project hay repository mỗi user; số câu hỏi đồng thời toàn hệ thống không giới hạn (mỗi user tối đa 10 mỗi phút, mỗi câu tới 65 giây).
4. **Đăng ký mở, không xác minh email.** Rate limit chỉ làm chậm việc tạo tài khoản hàng loạt.
5. **Access token không thu hồi được trước hạn.** Đăng xuất thu hồi refresh token nhưng access token (15 phút) còn dùng được tới khi hết hạn.
6. **TLS, HSTS và `AllowedHosts` chưa có trong repo.** Nginx chưa được dựng; `AllowedHosts` đang là `*`. Khi deploy phải có TLS, HSTS ở nginx, `AUTH_COOKIE_SECURE` bỏ trống (mặc định true) và `AllowedHosts` đặt đúng domain.
7. **`TRUSTED_PROXIES` đặt sai** (quá rộng) cho phép giả IP; để trống khi có nginx thì mọi người chung giới hạn. README có hướng dẫn, nhưng không có kiểm tra tự động.
8. **Secret nằm dạng văn bản trong `.env`** trên máy dev và trong biến môi trường của container (`docker inspect` thấy được). Chấp nhận được cho local; deploy thật cần secret manager.
9. **Frontend phải escape HTML và markdown** của câu trả lời, snippet và nội dung file, và lọc liên kết trong markdown: nội dung do LLM sinh ra từ code của repo bên thứ ba, có thể chứa chỉ dẫn hoặc đường link độc hại. Backend cố ý lưu nguyên văn.
10. **Chưa quét image Docker** (`node`, `postgres`, ảnh .NET). Chỉ quét package NuGet.
11. **Không kiểm tra tải thật**: các giới hạn được kiểm bằng test cấu hình nhỏ, chưa thử với tải đồng thời.

## Kết quả quét package

`dotnet list package --vulnerable --include-transitive` ngày 2026-11-21 (nguồn nuget.org): `Api`, `Application`, `Domain`, `Infrastructure`, `Api.Tests`, `Application.Tests` đều "has no vulnerable packages". Không có package mới được thêm.

## Cách chạy và kiểm thử

```
cd backend && dotnet test          # 537 test: Application 137, Api 400
docker compose up -d --build api postgres
curl -si localhost:8080/health     # xem các security header
```

Các test mới nằm trong `backend/tests/Api.Tests/Security/`: `RateLimitTests`, `HttpHardeningTests`, `SecurityOptionsTests`, `OwnershipMatrixTests`, `LogHygieneTests`.

## Câu hỏi cần xác nhận

- [ ] Nhóm Web: mặc định 10 lần mỗi phút mỗi IP cho đăng nhập có đủ cho buổi demo (nhiều người cùng mạng)? Nếu không thì tăng `RATE_LIMIT_AUTH_PER_MINUTE`.
- [ ] Nhóm Web: mỗi user 1 repository đang index một lúc có ổn cho demo (D28 chuẩn bị sẵn repo index trước)?
- [ ] Thành viên frontend: xử lý `429` (đọc `Retry-After`), `409 IMPORT_IN_PROGRESS`, `413`, timeout 75 giây cho gửi tin nhắn, và escape markdown/HTML của câu trả lời.
- [ ] Người dựng nginx: đặt `TRUSTED_PROXIES`, TLS, HSTS, `AllowedHosts`.

## Bước tiếp theo

- Merge nhánh, báo frontend các mã lỗi mới.
- Phase H: dùng lại các test này trong kiểm thử tích hợp; cân nhắc trần tổng cho hệ thống nếu demo có nhiều người dùng.
