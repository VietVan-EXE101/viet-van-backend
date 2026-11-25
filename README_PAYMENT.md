# Việt Văn — Payment / PRO / SePay

> Cập nhật 25/11/2026: xem [README_DRAMA.md](README_DRAMA.md) cho migration, quyền FREE/PRO, danh sách API và kết quả kiểm thử hiện tại. Mọi gói trả phí còn hiệu lực cấp PRO, giữ nguyên giá/thời hạn/tier đã có. Chỉ dùng các route thanh toán hiện có bên dưới; không có API Customer tự kích hoạt PRO.

## Phạm vi và kết luận kiểm tra source

Source hiện dùng PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents và PaymentAuditLogs cho SePay. SubscriptionPackages/UserSubscriptions và lịch sử RevenueTransactions được giữ. Dashboard chỉ tính PaymentOrders Paid có giao dịch SePay Paid phù hợp tại PaidAt; không cộng thêm RevenueTransactions. Các mục bên dưới mô tả luồng thanh toán đã có; hướng dẫn migration mới và chuyển đổi nghiệp vụ nằm trong README_DRAMA.

Build và test nghiệp vụ đã chạy bằng .NET; chưa xác minh SQL Server, SMTP, Google hoặc giao dịch SePay thật. Test thanh toán dùng SQLite và receipt giả lập bên trong test, không có endpoint giả lập trong API và không chuyển tiền thật. Việc đọc SQL Server ngoài sandbox bị từ chối; không thay đổi database trong phiên refactor này.

## 1. Sơ đồ thanh toán

```
Customer --JWT--> POST /api/payments/orders (PackageId + Idempotency-Key)
                       | đọc Package.Price từ SQL Server
                       | snapshot tên/giá/số ngày/TierLevel
                       v
                  PaymentOrders (Pending, ExpiresAt)
                       |
                       +-> URL ảnh VietQR / Bank / Amount / OrderCode
                       |
                  Khách chuyển khoản
                       v
             Bank --> SePay --> HTTPS POST /api/payments/webhooks/sepay
                                      | Authorization: Apikey <key>
                                      | nhận tiền, đúng account + code + amount
                                      | SQL sp_getapplock + transaction
                                      +-> PaymentBankTransactions
                                      +-> PaymentWebhookEvents (unique provider event)
                                      +-> PaymentOrders (Paid)
                                      +-> UserSubscriptions (+ số ngày VIP)
                                      +-> RevenueTransactions (legacy history)
                                      +-> PaymentAuditLogs

Số tiền sai / sai mã / hết hạn / nhiều giao dịch / sai account --> RequiresReview
```

Không có endpoint cho Customer tự nhận thành công. Frontend chỉ đọc status do Backend cung cấp. Sau khi Paid, TTL JWT cũ không cản VIP do entitlement truy vấn DB ở mỗi request đọc nội dung.

## 2. Database / triển khai an toàn

1. **Sao lưu SQL Server** và thư mục asset trước nâng cấp.
2. Nếu database chưa tạo, chạy `VietVanDb.sql`; nếu Drama chưa tạo thì chạy `scripts/DramaUpgrade.sql` rồi `scripts/DramaSeedDemo.sql` (chỉ trong môi trường demo).
3. Cập nhật schema bằng **một trong hai cách** (không cần chạy cả hai):
   - **Cách A**: SSMS chạy `scripts/PaymentUpgrade.sql` trên DB `VietVanDb`.
   - **Cách B**: `dotnet ef database update --project VietVan.API.csproj` — dùng migration thủ công `20261123_AddPaymentModule` (chỉ áp dụng sau khi đã có schema gốc).
4. Chạy `scripts/VerifyPaymentSchema.sql`. Không chèn giao dịch Paid giả trong production.
5. `SubscriptionPackages` có thể quản trị tại `/admin-payments.html`; giá VIP cũ theo bản SQL demo `99.000`/`999.000` VND, không thay đổi khi nâng cấp.

**Quan trọng:** Do dự án gốc là DB có sẵn và chưa có lịch sử migration hoàn chỉnh, migration được viết bằng SQL idempotent để bảo vệ dữ liệu. Nếu sau này sử dụng `dotnet ef migrations add`, cần chuẩn hóa EF model snapshot và migration baseline; không áp dụng migration scaffolding tự động vào production khi chưa review SQL. Rollback DB giao dịch cần quy trình riêng, không dùng lệnh xóa bảng.

### 2.1. Chuyển media trả phí ra ngoài wwwroot

`PrivateMedia/drama-assets/` là nơi lưu ảnh, audio và video. Gói ZIP này **đã chuyển demo** từ `wwwroot/drama-assets/demo` vào `PrivateMedia/drama-assets/demo`.

Nếu từng upload asset trên máy Windows trước đó, cần copy chúng từ:

`VietVan.API/wwwroot/drama-assets/*` → `VietVan.API/PrivateMedia/drama-assets/*`

Giữ nguyên cấu trúc `drama-assets/...`, sau đó xóa bản copy công khai trong wwwroot. Middleware từ chối toàn bộ `/drama-assets/*`. API trả `url` mới (`/api/dramas/{dramaId}/assets/{assetId}/media`) và kiểm tra quyền. **Điều này cố ý thay đổi URL tài nguyên cũ để ngăn bypass VIP**, cần cập nhật FE nếu có hardcode URL. Bản demo trong ZIP đã dùng URL mới.

## 3. Biến môi trường (PowerShell Windows)

Chỉ ví dụ placeholder, **không dùng dữ liệu thật trong source code hoặc Git**.

```powershell
cd VietVan.API
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Server=localhost\SQLEXPRESS;Database=VietVanDb;Trusted_Connection=True;TrustServerCertificate=True;"
# Tạo secret mạnh, mới cho từng môi trường (đừng commit vào Git).
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 48
$rng.GetBytes($keyBytes)
$env:Jwt__Key = [Convert]::ToBase64String($keyBytes)
$env:SePay__BankId = "BANK_BIN_OR_SUPPORTED_CODE"
$env:SePay__BankName = "TEN_NGAN_HANG"
$env:SePay__AccountNumber = "SO_TAI_KHOAN_THU_HUONG"
$env:SePay__AccountHolder = "TEN_CHU_TAI_KHOAN"
$env:SePay__WebhookApiKey = "YOUR_RANDOM_SEPAY_WEBHOOK_API_KEY_AT_LEAST_24_CHARS"
$env:SePay__OrderLifetimeMinutes = "15"
dotnet restore
dotnet build
dotnet run --launch-profile http
```

Tài khoản Admin demo tự khởi tạo với mật khẩu mặc định đã được **tắt** (`DevelopmentAdmin.Enabled=false`).
Sau khi database sẵn sàng, tạo/cập nhật admin thủ công bằng `dotnet run -- --setup-admin` (nhập mật khẩu mạnh khi ứng dụng yêu cầu). Không lưu mật khẩu Admin trong cấu hình, ZIP hoặc Git.

**Production:** dùng HTTPS, secret manager/Key Vault hoặc environment vars từ deployment system, quản lý quyền trên SQL Server, không dùng dev JWT key mặc định, không bật Swagger Development, không sử dụng tunnel công khai không có TLS. Dùng URL HTTPS chính xác cấu hình vào SePay. Đồng hồ máy chủ phải đúng UTC. Backup SQL Server + `PrivateMedia/` độc lập.

## 4. Cấu hình SePay

1. Kết nối **tài khoản ngân hàng nhận tiền thực tế** với SePay và kiểm tra giao dịch incoming của tài khoản đó.
2. Trong SePay: **Webhooks → Thêm**, chọn sự kiện `Có tiền vào`, tài khoản ngân hàng đã kết nối, JSON, kiểu bảo mật **API Key**. Thiết lập API Key khớp `SePay__WebhookApiKey`.
3. Đặt endpoint HTTPS: `https://your-domain/api/payments/webhooks/sepay`.
4. Cấu hình nhận diện code chuyển khoản theo prefix `VV` hoặc không bật bộ lọc prefix; luôn giữ toàn bộ nội dung `content`/trường `code` do SePay gửi. Hệ thống chấp nhận mã đơn định dạng `VV` + 16 ký tự hex (ví dụ `VV0123456789ABCDEF`). Nếu `code` của SePay khác với mã này, giao dịch được đưa vào đối soát để tránh nhận nhầm đơn.
5. Bật retry cho response lỗi non-2xx và cảnh báo thất bại. Endpoint trả **201** + `{"success":true}` sau khi **đã commit**.
6. Dùng `Gửi thử` của SePay để kiểm tra auth/định dạng, rồi kiểm tra **một giao dịch chuyển khoản thật** với số tiền nhỏ do bạn chủ động kiểm soát; so sánh số dư và audit trước khi mở thanh toán thương mại.

Tài liệu chính thức: https://docs.sepay.vn/tich-hop-webhooks.html và https://docs.sepay.vn/oauth2/api-webhooks.html

**Giới hạn:** Module chỉ hỗ trợ **Api_Key** cho webhook SePay kiểu JSON, không triển khai HMAC/OAuth2. Nếu chọn loại bảo mật khác ở SePay, cần chỉnh middleware xác thực tương ứng. SePay webhook API Key là cơ chế xác thực nguồn theo tài liệu, không thay thế đối soát với API giao dịch/statement ngân hàng ở tình huống rủi ro. VietQR ở đây là *ảnh lệnh chuyển khoản*, không phải dịch vụ xác nhận giao dịch.

## 5. API mới

| Method | API | Role |
|---|---|---|
| GET | `/api/subscription-packages` | Public |
| GET | `/api/subscriptions/me` | JWT |
| POST | `/api/payments/orders` | JWT + `Idempotency-Key` header |
| GET | `/api/payments/orders/{orderCode}` | Owner JWT |
| POST | `/api/payments/orders/{orderCode}/cancel` | Owner JWT |
| GET | `/api/payments/history` | Owner JWT |
| POST | `/api/payments/webhooks/sepay` | SePay API Key only |
| GET | `/api/admin/payments` | Admin JWT |
| GET | `/api/admin/payments/{id}` | Admin JWT |
| GET | `/api/admin/payments/reconciliation` | Admin JWT |
| GET/POST/PUT | `/api/admin/subscription-packages` | Admin JWT |
| GET | `/api/works/{id}/content` | Public hoặc VIP theo nội dung |
| GET | `/api/dramas/{dramaId}/assets/{assetId}/media` | Public hoặc VIP theo drama |
| GET | `/api/admin/dramas/{dramaId}/assets/{assetId}/media` | Admin JWT |

API `/api/dramas/{id}`, `/api/dramas/{id}/scenes/{sceneId}`, `/api/dramas/{id}/hotspots/{hotspotId}` **giữ đường dẫn** nhưng kiểm tra publish và `AccessLevel`. `Public`: ai cũng xem; `FreeAuthenticated`: đăng nhập; `VIP`: có VIP còn hạn. Nếu drama liên kết LiteraryWork cần Tier lớn hơn, áp dụng quyền chặt hơn. 401 khi chưa đăng nhập, 403 nếu không đủ quyền, response `code=SUBSCRIPTION_REQUIRED`.

**Tạo đơn:**

```http
POST /api/payments/orders
Authorization: Bearer <JWT>
Idempotency-Key: <RANDOM_UUID>
Content-Type: application/json

{"packageId":2}
```

Không truyền `amount`, `status`, `userId`. Trạng thái có thể là `Pending`, `Paid`, `Expired`, `Cancelled`, `RequiresReview`. Giao dịch sai/hết hạn không kích hoạt; dữ liệu nhận được sẽ hiển thị trong reconciliation.

## 6. Giao diện

- Khách hàng: `http://localhost:5080/vip.html` — đăng nhập, xem gói, chọn gói, tạo đơn, QR, countdown, polling 5 giây, lịch sử và thời hạn VIP.
- Admin: `http://localhost:5080/admin-payments.html` — danh sách đơn, lọc theo user/mã/status, xem audit, giao dịch cần đối soát, quản lý gói.
- Drama: `/drama-studio.html` — chọn quyền Public/FreeAuthenticated/VIP; `/drama-viewer.html` — kiểm tra JWT và tải media có quyền.

Trang static dùng `sessionStorage` cho token trong cùng tab, và `vv_order` lưu đơn đang mở sau refresh. Màn production nên tích hợp UI này vào FE hiện tại, bổ sung CSP/anti-XSS và HTTPS toàn bộ.

## 7. Chính sách VIP và rủi ro

- Một đơn được cấp VIP **duy nhất một lần** nhờ unique provider event ID, unique `ActivationPaymentOrderId`, SQL application lock + transaction.
- Khi mua nhiều gói: kéo dài từ `max(UTC now, max VIP EndDate còn hiệu lực/đã lên lịch)`; không mất số ngày còn lại. Mỗi lần mua tạo một UserSubscription (dễ audit), không sửa subscription cũ.
- Lưu tier tại thời điểm cấp trong `GrantedTierLevel`: đổi tier gói sau này không làm mất quyền ở giao dịch mới.
- Kiểm tra expiration từ DB mỗi request: không phụ thuộc worker. Worker chỉ chuyển Pending hết hạn sang Expired để hiển thị.
- Trường hợp nộp thiếu/thừa, nhiều giao dịch, sai mã, sai tài khoản, tiền tới muộn: **RequiresReview**, không tự cấp VIP, cần đối soát bank/SePay trước khi thao tác thủ công.
- **Chưa triển khai API hoàn tiền thực tế/duyệt bù thủ công** vì cần quy trình xác minh và phân quyền đối soát 2 người, đồng bộ với ngân hàng. Status `Refunded` được dự phòng.
- **Chưa triển khai kết nối API tra soát bù tự động** khi SePay không gửi webhook; phải dùng nhật ký SePay hoặc tài khoản ngân hàng. Nên bổ sung trước khi triển khai quy mô lớn.
- **Media protected hiện trả file trực tiếp qua ASP.NET Core**, hỗ trợ Range; production video lớn nên chuyển sang object storage private + signed URL ngắn hạn/CDN bảo vệ. Trang Viewer demo dùng fetch blob khi JWT, không phù hợp video dung lượng rất lớn.
- **Không bảo đảm tài khoản thực/SePay production** khi chưa kiểm thử dòng tiền đầu cuối thật và giám sát log.

## 8. Kiểm thử bắt buộc trước go-live

1. Chạy `dotnet build` và `dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj`.
2. Sao lưu DB, chạy migration **trên bản clone**; so sánh count `Users`, `SubscriptionPackages`, `UserSubscriptions`, `RevenueTransactions` trước và sau (không giảm).
3. Tạo đơn có `amount` giả trong JSON: backend phải lấy DB price; gói ngừng bán không tạo được đơn.
4. Hai request cùng `Idempotency-Key`: cùng một đơn; khác package + cùng key: 409.
5. Customer A GET order Customer B: 404; Customer/Editor GET admin API: 403.
6. Webhook thiếu/khác `Authorization: Apikey ...`: 401; thiếu cấu hình key: 503.
7. Webhook valid đúng tiền: 201 + success:true; kiểm tra 1 PaymentBankTransaction, 1 UserSubscription, 1 history RevenueTransaction; gửi lại payload cũ: không nhân đôi ngày.
8. Bắn 2 webhook đồng thời (cùng id hoặc khác id cùng order): một lần Paid, giao dịch còn lại RequiresReview.
9. Sai code / sai amount / sai account / đến sau expiry: RequiresReview, không cấp VIP.
10. VIP đã hết hạn: API content, scene, hotspot, protected media đều 403; static `/drama-assets/*` luôn 404.
11. Không lấy `JWT.SubscriptionTierLevel` làm căn cứ truy cập: token cũ vẫn mở VIP ngay sau kích hoạt, và bị khóa sau hết hạn.
12. Trước khi mở production: test SePay sandbox/Send Test và **giao dịch thực tế**, kiểm tra retry/đối soát, giám sát database lock, backup/restore.

Xem `PHASE_REPORT.md` để biết trạng thái theo 7 giai đoạn, điều chưa test và file thay đổi.
