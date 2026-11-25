# Audit và tiến độ Google / Email / SePay

> Cập nhật: Google challenge/login, đăng ký chờ xác minh, verify-email và resend-verification đã được nối vào API và trang VIP. Nội dung audit bên dưới mô tả trạng thái tại thời điểm audit trước đó. Các luồng mới chưa được kiểm thử với SQL Server hoặc nhà cung cấp thực; xem `INTEGRATION_SETUP_VI.md` và `scripts/Upgrade_Auth_Runtime.sql` trước khi khởi động lại. Google challenge/rate limiting đang nằm trong bộ nhớ tiến trình, chưa phù hợp triển khai nhiều instance nếu không bổ sung kho dùng chung.

## Kiến trúc đã kiểm tra

Backend tự phát hành JWT, dùng ASP.NET Core `PasswordHasher<User>` và `Users.Role`; không thấy cấu hình Supabase trong source. Đăng ký hiện cấp JWT ngay, chưa xác minh email. Chưa có bảng liên kết Google, token xác minh, trạng thái khóa tài khoản hoặc email sender trong model hiện tại. Không thay đổi chính sách tài khoản cũ khi chưa có quy trình xác minh.

SePay hiện dùng header API Key, so sánh khóa bằng constant time, kiểm tra nội dung giao dịch, tiền, tài khoản và trạng thái đơn; sử dụng transaction, application lock và các unique index được thiết kế trong migration để chống xử lý trùng. Đây là nhận xét về source, chưa xác nhận schema SQL thực hoặc kiểm thử giao dịch thực. API Key và webhook response 201 hiện phù hợp một chế độ webhook được mô tả trong [tài liệu SePay](https://docs.sepay.vn/tich-hop-webhooks.html); cần xác nhận chế độ, retry và tài khoản ngân hàng trong cấu hình nhà cung cấp.

## Phần đã bổ sung nhưng chưa nối vào API

- `GoogleIdTokenValidator`: xác minh chữ ký RS256 bằng OIDC discovery/keys của Google, issuer, audience, thời hạn, subject, email_verified và nonce. Tài liệu: [Google ID token verification](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token). Gọi service này đơn lẻ không đủ: phải có nonce do server phát hành, ràng buộc với phiên trình duyệt và tiêu thụ đúng một lần, chống CSRF/replay, lưu subject và xử lý email trùng trước khi cấp JWT.
- `SmtpEmailSender`: gửi qua SMTP STARTTLS và chuyển lỗi gửi tới caller; cấu hình `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__UserName`, `Email__Smtp__Password`, `Email__Smtp__FromAddress` bằng secret store. Chưa nối vào đăng ký hoặc webhook, chưa gửi email thực.
- `EmailVerificationToken`: tạo token ngẫu nhiên 32 byte và hash SHA-256. Chưa lưu/tiêu thụ token hay thực thi cooldown.
- `scripts/Upgrade_Google_Email_Payment.sql`: dự thảo additive có kiểm tra target và dừng nếu các đối tượng đã tồn tại; chưa áp dụng hoặc kiểm thử SQL Server. NULL EmailVerified được dành cho tài khoản cũ chưa có bằng chứng xác minh. Script không chỉnh bảng thanh toán và không xác nhận schema đang chạy tương thích.

## Chưa hoàn thành

Không có endpoint Google/link, email verification/resend, forgot/reset password hoặc Google UI. Chưa có DB-backed nonce, cơ chế tiêu thụ token một lần, rate-limit/cooldown, email templates, xử lý gửi email trong transaction/outbox, chính sách chuyển đổi người dùng cũ, account lock enforcement hay điều kiện xác minh email trước login/payment. Không áp dụng script trước khi đối chiếu schema `VietVan_New` thực tế, thiết kế và kiểm thử các luồng này; script có thể cần điều chỉnh theo schema.

Chưa kiểm thử xác thực Google thật, SMTP provider, SQL concurrency, webhook provider hoặc giao dịch ngân hàng. Chưa thiết kế xong CORS/HTTPS/rate-limiting, backup, health checks và cấu hình triển khai. Không coi module này là production-ready. Không chạy giao dịch thật tự động; cần test provider trong môi trường được phép và đối soát độc lập.
