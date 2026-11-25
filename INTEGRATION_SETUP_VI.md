# Cấu hình tích hợp Việt Văn

**Trạng thái hiện tại:** Google login và xác minh email đã được nối vào API và trang VIP, nhưng chưa được kiểm thử với database hoặc provider thực. SePay có luồng webhook hiện tại nhưng chưa được xác nhận bằng giao dịch thực. Không triển khai production chỉ dựa vào kết quả script kiểm tra cấu hình.

## Chuẩn bị

- Google: tạo OAuth client loại Web application trong Google Cloud và cấu hình Authorized JavaScript origins phù hợp. Khi luồng Google được hoàn thiện, môi trường local có thể cần origin `http://localhost:5080`; xem [Google Identity Services](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid). Code hiện chỉ sử dụng `GoogleSignIn:ClientId`, chưa dùng Client Secret hoặc Redirect URI.
- Gmail: bật xác minh hai bước và tạo App Password nếu tài khoản hỗ trợ. [Google hướng dẫn tạo App Password](https://support.google.com/accounts/answer/185833); một số tài khoản không hỗ trợ tính năng này. Code SMTP dùng STARTTLS cổng 587.
- SePay: liên kết tài khoản ngân hàng và cấu hình webhook theo [tài liệu SePay](https://docs.sepay.vn/tich-hop-webhooks.html), chọn **API Key** cho endpoint hiện tại. Cần BankId VietQR, tên ngân hàng, số tài khoản, chủ tài khoản và API key. URL webhook công khai phải dùng HTTPS và dẫn tới `/api/payments/webhooks/sepay`. Cấu hình retry và quy trình đối soát giao dịch đến muộn trong SePay.

## Nhập cấu hình trên Windows

Trong PowerShell tại thư mục `VietVan.API`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Configure-Integrations.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Integrations.ps1
```

Nhập từng giá trị khi được hỏi; để trống để giữ nguyên mục hiện có. Gmail App Password và API key được nhập ẩn và lưu vào .NET User Secrets của project. Các khóa không liên quan, gồm JWT hiện có, không bị xóa. User Secrets dùng cho Development và không phải kho bí mật mã hóa cho production. Template `configuration/INTEGRATION_SETTINGS.example.json` chỉ là ví dụ; ứng dụng không tự đọc template đó.

Quên mật khẩu gửi mã reset 43 ký tự qua SMTP ngay cả khi chưa có trang FE. `Email:PasswordResetUrl` là URL trang FE tùy chọn: khi có URL HTTPS thật hoặc HTTP localhost trong Development, email kèm thêm link với token ở fragment. Chỉ URL được cấu hình nhưng sai mới gây 503 `PASSWORD_RESET_NOT_CONFIGURED`. Mã trong email dùng làm `token` của POST `/api/auth/reset-password`. Sau khi restart Backend, kiểm tra `PasswordResetEmailAcceptedByProvider` hoặc `PasswordResetEmailFailed` trong console; không gửi token hoặc App Password trong báo cáo lỗi.

Script kiểm tra chỉ đọc cấu hình khả dụng trong phiên PowerShell, không kết nối SQL, Google, SMTP hoặc SePay, không gửi email, không chuyển tiền. Biến môi trường thiết lập riêng cho VS Code, profile khác hoặc server triển khai có thể khác phiên kiểm tra. Không đưa secret vào Git hoặc appsettings trong workspace.

## SQL và chạy backend

`scripts/Upgrade_Auth_Runtime.sql` là **script bổ sung chưa được kiểm thử với schema SQL thực**. Trước khi chạy, sao lưu và đối chiếu schema trên bản sao. Script dừng nếu các bảng auth đã tồn tại, thay vì đoán chúng có tương thích hay không; không thay đổi các bảng Payment. Không chạy các script auth dự thảo cũ chồng lên script này.

Backend cần các bảng auth mới trước khi khởi động lại phiên bản mới; nếu không, đăng ký và đăng nhập có thể lỗi. Tài khoản được tạo sau nâng cấp phải xác minh email trước khi đăng nhập; tài khoản cũ chưa có trạng thái được giữ tương thích và không tự đánh dấu verified. Chủ tài khoản cũ đang đăng nhập có thể gọi resend với JWT để bắt đầu xác minh.

Development dùng `http://localhost:5080` cho link email mặc định. Với môi trường khác, cấu hình `Email:VerificationBaseUrl` là origin HTTPS công khai được kiểm soát; không lấy URL từ request. Google challenge được lưu trong bộ nhớ tiến trình, cần sticky sessions hoặc kho dùng chung khi chạy nhiều instance. Bộ giới hạn gửi lại theo IP cũng là bộ nhớ tiến trình.

Sau khi các bước phát triển và kiểm thử còn thiếu được hoàn thành, chạy backend Development với `dotnet run --environment Development`. Với SePay, tạo một đơn từ API, xác nhận thông tin ngân hàng/giá, dùng tính năng gửi thử của provider và kiểm tra trạng thái qua backend. Không xem QR hoặc redirect là bằng chứng thanh toán; giao dịch thật chỉ thực hiện khi đã kiểm thử và được người vận hành cho phép.

## Giới hạn và xử lý lỗi

- Cấu hình Google/SMTP đủ chưa chứng minh kết nối, email hay Google login thực sự hoạt động. Email trùng tài khoản cũ không được tự liên kết; chưa triển khai flow liên kết tài khoản.
- SMTP báo lỗi: kiểm tra 2-Step Verification, App Password và quyền của tài khoản. Không tắt TLS để vượt lỗi.
- SePay không cập nhật đơn: kiểm tra webhook API Key, tài khoản thụ hưởng, mã đơn, số tiền, cấu hình retry và log đối soát; không tự đánh dấu Paid.
- Nếu SQL Server không kết nối bằng Windows Authentication, kiểm tra quyền của tài khoản Windows chạy ứng dụng; không đổi sang SQL Authentication để vượt lỗi.
