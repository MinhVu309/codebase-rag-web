# CodeInsight — Frontend (React + TypeScript + Vite)

Giao diện Web Client cho hệ thống **CodeInsight — Phân tích và tra cứu kho mã nguồn bằng RAG**.

Giao diện được thiết kế và triển khai dựa trên bản thiết kế **CodeInsight AI Codebase Explorer** từ Stitch, kết nối trực tiếp với Web Backend (.NET 10 Web API).

---

## 1. Công nghệ sử dụng

* **Framework & Tooling:** React 19, TypeScript, Vite.
* **Styling & Theme:** Tailwind CSS (cấu hình đồng bộ theme Dark/Light theo Stitch Design).
* **Data Fetching & Caching:** `@tanstack/react-query` (quản lý server state, tự động polling trạng thái index mỗi 2 giây).
* **Client State:** `zustand` (quản lý phiên đăng nhập `authStore`, giao diện `themeStore`).
* **HTTP Client:** `axios` (với cấu hình proxy `/api`, credentials cookie HttpOnly, interceptor tự động refresh token khi gặp mã lỗi 401).
* **Markdown & Security:** `react-markdown` + `rehype-sanitize` (lọc bỏ mã độc hại XSS theo chuẩn bảo mật D22).
* **Toast & Notification:** `sonner` (hiển thị thông báo ProblemDetails và bộ đếm ngược Rate Limit 429).
* **Icons:** Material Symbols Outlined & Lucide React.

---

## 2. Các trang và chức năng chính

1. **Authentication (`/login`, `/register`):**
   * Đăng ký tài khoản (mật khẩu 8–128 ký tự), tự động đăng nhập.
   * Đăng nhập với cookie HttpOnly (`SameSite=Strict`), bảo vệ chống Timing Attack.
   * Tự động làm mới phiên (Silent Refresh) trong background qua `POST /api/auth/refresh`.
2. **Dashboard (`/dashboard`):**
   * Tổng quan số lượng dự án, kho lưu trữ đã import, trạng thái RAG pipeline.
   * Danh sách dự án gần đây, phím tắt tạo dự án mới, tìm kiếm nhanh.
3. **Quản lý dự án (`/projects`, `/projects/:projectId`):**
   * Tạo, đổi tên và xóa dự án (tự động dọn dẹp RAG index phía sau).
   * Hiển thị **Active Repository** hiện tại của dự án.
   * Modal import GitHub repository công khai (hỗ trợ nhập nhánh/ref tùy chọn).
   * **Live Indexing Tracker:** Thanh tiến độ thời gian thực qua các bước `pending` → `cloning` → `parsing` → `embedding` → `ready`/`failed`, hiển thị cờ `stale` nếu mạng chập chờn.
4. **Phòng hỏi đáp AI Codebase Chat (`/projects/:projectId/chat/:conversationId`):**
   * Sidebar quản lý danh sách cuộc hội thoại (tạo mới, đổi tên, xóa).
   * Tự động đặt tên hội thoại thông minh dựa trên câu hỏi đầu tiên.
   * Khung chat hỏi đáp: hỗ trợ điều chỉnh `topK` (1–20), giới hạn 2000 ký tự, timeout 75 giây.
   * Render câu trả lời Markdown an toàn (XSS-sanitized) kèm highlight cú pháp code.
   * Trích dẫn nguồn (Citations): Hiển thị các thẻ `[1]`, `[2]` tương tác, bấm vào để mở xem code.
5. **Trình đọc mã nguồn (Code Viewer Drawer):**
   * Drawer xem lát cắt file code (`GET /api/repositories/{id}/files`) mở trượt từ bên phải ngay trong màn hình chat.
   * Hiển thị số dòng, highlight dòng bắt đầu – kết thúc của trích dẫn, commit SHA, ngôn ngữ.
   * **Chế độ Snapshot cũ:** Tự động fallback hiển thị snippet đã lưu trong database nếu file không còn tồn tại trên repository (404).
   * Nút phóng to toàn màn hình (Fullscreen Explorer).
6. **Cài đặt & Trạng thái hệ thống (`/settings`):**
   * Thông tin tài khoản người dùng (`GET /api/auth/me`).
   * Chuyển đổi Dark / Light mode.
   * Ping kiểm tra liveness probe hệ thống (`GET /health`).
   * Đăng xuất xóa session (`POST /api/auth/logout`).

---

## 3. Hướng dẫn chạy môi trường Development

### Yêu cầu tiên quyết
* Node.js v20+ hoặc v24+ (đã kiểm tra trên Node.js v24.17.0).
* Web Backend đang chạy ở cổng `8080` (qua `docker compose up -d` hoặc `dotnet run`).

### Khởi chạy
```bash
# 1. Di chuyển vào thư mục frontend
cd frontend

# 2. Cài đặt thư viện (nếu chưa cài)
npm install

# 3. Khởi chạy dev server
npm run dev
```

Ứng dụng sẽ chạy tại địa chỉ: **`http://localhost:5173`**.

> **Cơ chế Proxy:** `vite.config.ts` đã cấu hình proxy toàn bộ request `/api` và `/health` sang `http://localhost:8080`. Trình duyệt xem cùng origin `localhost` nên tự động đính kèm cookie HttpOnly mà không cần cấu hình CORS.

---

## 4. Build Production

```bash
# Kiểm tra TypeScript và build tĩnh vào thư mục dist/
npm run build

# Chạy thử bản build production cục bộ
npm run preview
```

Thư mục `dist/` có thể được phục vụ trực tiếp qua Nginx cùng với reverse proxy `/api` sang backend container theo kiến trúc production của dự án.
