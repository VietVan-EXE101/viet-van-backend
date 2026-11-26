# Việt Văn — Payment / PRO / SePay

> Cập nhật 25/11/2026: xem [README_DRAMA.md](README_DRAMA.md) cho migration, quyền FREE/PRO, danh sách API và kết quả kiểm thử hiện tại. Mọi gói trả phí còn hiệu lực cấp PRO, giữ nguyên giá/thời hạn/tier đã có. Chỉ dùng các route thanh toán hiện có bên dưới; không có API Customer tự kích hoạt PRO.

## Phạm vi và kết luận kiểm tra source

### POST /api/payments/orders trả DATABASE_UPGRADE_REQUIRED (26/11/2026)

Request packageId=2 với trace `0HNQBJCDCGLTT:00000001` trả 503 `DATABASE_UPGRADE_REQUIRED`. Source middleware chỉ gán code này khi tìm thấy SQL error 207 (thiếu cột) hoặc 208 (thiếu bảng/object). Đây là lỗi schema trong database đang kết nối; chưa thể xác định tên bảng/cột chỉ từ response. Không quy lỗi cho FE, QR hoặc packageId theo phỏng đoán. Chưa xác nhận trực tiếp Render database/schema, chưa có chi tiết provider từ log trace này.

Đã bổ sung PaymentOrders, PaymentAuditLogs, PaymentBankTransactions, PaymentWebhookEvents vào `--check-db-readonly`; trước đó lệnh chưa bao gồm 4 bảng này. Có thể dùng lệnh riêng để kiểm tra Payment và các bảng user/package/subscription phụ thuộc:

```powershell
dotnet run --launch-profile http -- --check-payment-db-readonly
```

Lệnh thoát trước khi khởi động Web Service/expiry worker. Chỉ đọc tên cột bằng SELECT TOP (0) rồi kiểm tra projection EF, không đọc hàng giao dịch, tạo order, migration, seed, UPDATE/INSERT/DELETE hoặc gọi SePay. Output in tên database và từng bảng/cột thiếu từ model; không in Connection String, password hoặc message provider. Nếu tên database khác mục tiêu Render thì không áp dụng kết luận vào Production trước khi đối chiếu cấu hình. SELECT pass không thay thế kiểm tra constraints, indexes, quyền ghi và webhook thực tế.

API CreateOrder đọc PaymentOrders đầy đủ trước khi kiểm tra gói và ghi PaymentOrders/PaymentAuditLogs sau đó. Người dùng đã chạy lệnh thành công trên Somee `VietVan_New`, SQL Server 15.0.2000.5, compatibility 150: Users, UserAuthStates, UserSubscriptions và SubscriptionPackages đều PASS. Bốn bảng Payment thiếu 14 cột dưới đây, gồm cả `PaymentOrders.BillingPeriodSnapshot` mà script PaymentUpgrade lịch sử không tạo. Đây là kết quả schema thực do người vận hành cung cấp, chưa có đối chiếu trực tiếp Connection String Render hoặc exception tên cột của trace trên.

| Bảng | Cột thiếu đã xác nhận |
| --- | --- |
| PaymentOrders | Amount, BillingPeriodSnapshot, Currency, DurationDaysSnapshot, PackageNameSnapshot, ReviewReason, TierLevelSnapshot |
| PaymentAuditLogs | Detail |
| PaymentBankTransactions | AccountNumberMasked, ClaimedOrderCode, Currency, ReviewReason |
| PaymentWebhookEvents | Outcome, PayloadHash |

Đã tạo [scripts/Upgrade_Somee_PaymentColumns.sql](scripts/Upgrade_Somee_PaymentColumns.sql), chỉ dành cho database `VietVan_New`. Các script lịch sử `PaymentUpgrade.sql`/`VerifyPaymentSchema.sql` còn có `USE VietVanDb`; không chạy nguyên văn, baseline hoặc tất cả migration lên Somee để chữa lỗi này.

Trong SSMS kết nối Somee, chọn `VietVan_New`, mở file mới và chạy **toàn bộ script với `@ApplyChanges = 0` giữ nguyên**. Preview chỉ đọc dữ liệu ứng dụng (COUNT và metadata; các table variable chỉ dùng nội bộ session), không đọc/in giao dịch, số tài khoản, payload hoặc thay schema. Nó trả số dòng từng bảng, kế hoạch 14 cột, toàn bộ metadata cột và các `LEGACY INSERT BLOCKER`. Gửi các kết quả này trước khi áp dụng nếu có STOP/blocker.

Ảnh SSMS người vận hành cung cấp ngày 26/11 xác nhận cả bốn bảng Payment đều có **0 hàng**, và có đúng bốn cột cũ NOT NULL mà EF không ghi trong PaymentOrders: `DurationInDays`, `PackageName`, `Price`, `TierLevel`. Lần apply cũ dừng với 51005 trước DDL. Chỉ thêm 14 cột mới sẽ chưa đủ: các cột cũ này tiếp tục chặn INSERT đơn hàng.

Script cập nhật xử lý đúng bốn tên cột cũ này bằng `ALTER COLUMN ... NULL` **chỉ khi PaymentOrders còn trống**, giữ nguyên tên, kiểu built-in, độ dài, precision/scale, collation và các constraint hiện có. Không rename/drop cột, không thêm default giả. Các trường mới `Amount`, `PackageNameSnapshot`, `DurationDaysSnapshot`, `TierLevelSnapshot` vẫn NOT NULL đúng model và nhận giá trị thật từ PaymentService. Script không sửa Entity hoặc nghiệp vụ. Nếu kiểu cột cũ không nằm trong nhóm hỗ trợ, có cột chặn khác, có đơn hàng, hoặc dependency không cho ALTER thì dừng/rollback; không tự tháo constraint/index để ép chạy.

Chế độ `@ApplyChanges = 1` chỉ thêm những cột còn thiếu khi thỏa điều kiện: các cột đích đã tồn tại phải khớp type/size/precision/scale/nullability, bảng có cột NOT NULL mới phải đang trống, và không còn cột legacy chặn INSERT ngoài bốn cột đã xử lý. Nếu có lịch sử hoặc blocker, script dừng trước DDL và cần mapping riêng dựa trên schema thật. Không đoán Amount/giá, thời hạn, tier, currency, webhook hash/outcome hoặc audit Detail; không lấy giá gói hiện tại để thay lịch sử.

Trước apply cần sao lưu và dừng các tiến trình ghi thanh toán. **Mở lại file script đã cập nhật từ repository hoặc chép lại toàn bộ vào tab SSMS mới; tab SQLQuery cũ không tự cập nhật.** Chạy preview 0, xem kế hoạch 14 cột mới và bốn dòng `WILL MAKE OBSOLETE COLUMN NULLABLE`. Sau đó đổi `@ApplyChanges = 1` và chạy toàn bộ. Nếu schema vẫn như ảnh, kết quả dự kiến: `APPLIED`, `ColumnsAdded=14`, `LegacyColumnsMadeNullable=4` và số hàng bốn bảng vẫn 0; lần chạy lại đã hoàn tất có thể trả 0 thay vì 14/4.

Script apply dùng transaction, giữ khóa bảng trong lúc kiểm tra/tạo cột, xác minh số hàng không đổi, rollback nếu lỗi; không thay dữ liệu/giá/trạng thái/ID. Không tự tạo index/constraint/trigger hay tự chạy khi startup. Preview/SELECT pass chưa chứng minh quyền ghi, constraints/indexes/triggers và webhook phù hợp. Sau khi apply thành công, chạy lại `--check-payment-db-readonly` rồi thử request tạo đơn với cùng Idempotency-Key; chỉ kết luận API đã sửa khi request thực tế thành công. Không cần push/redeploy riêng cho thay đổi schema nếu Render đã dùng cùng database/model.

Lần apply tiếp theo người vận hành nhận SQL 102 gần `SQL_Latin1_General_CP1_CI_AS`; kiểm tra read-only sau đó vẫn thiếu 14 cột. Đã tái hiện bằng parser trên câu ALTER sinh ra: `COLLATE [SQL_Latin1_General_CP1_CI_AS]` không hợp lệ. Bản cũ dùng QUOTENAME cho collation; đây là lỗi script, không phải tên collation của Somee. Bản sửa giữ nguyên collation nhưng dùng tên không bọc ngoặc, kiểm tra tên từ metadata có trong `sys.fn_helpcollations()` và chỉ chứa chữ/số/gạch dưới. Tên cột vẫn dùng QUOTENAME. Lỗi dynamic SQL nằm trong TRY/CATCH và transaction nên rollback, cần mở lại file mới trong SSMS trước khi thử lại.

Kiểm thử bản sửa collation: parser SQL Server 2019 đạt cho toàn bộ script, 12 biến thể ALTER COLUMN được sinh theo các nhóm kiểu dữ liệu hỗ trợ, và 14 câu ADD cột; kiểm tra riêng dynamic SQL bổ sung cho kiểm tra cú pháp script ngoài. **30/30 test schema/middleware/payment đạt** (`tests/VietVan.Payment.Tests/TestResults/somee-payment-collation-regression.trx`), gồm 14 định nghĩa cột khớp model, đủ 40 scalar column để phát hiện legacy INSERT blocker, chỉ cho phép đúng bốn cột cũ không thuộc EF trở thành nullable, giữ metadata, guard lịch sử/cột lạ trước DDL và kiểm tra collation không bị quote sai. Đây là test offline và parser, không phải integration test SQL Server. Agent chưa apply script cập nhật trên Somee hoặc kiểm tra tạo đơn thật trên Render.

Có thể lấy chi tiết trực tiếp ở Render Logs bằng trace trên: gửi riêng dòng `Invalid column name ...` hoặc `Invalid object name ...` và SqlNumber; không cần gửi JWT hoặc Connection String. File sửa trong lượt chẩn đoán: `Hosting/DatabaseReadOnlyCheck.cs`, `Program.cs`, `tests/VietVan.Payment.Tests/PaymentSchemaDiagnosticsTests.cs`, tài liệu này. Không thay Entity, DTO, PaymentService, giá gói hoặc logic idempotency. 24 test về schema diagnostics/middleware/payment business đạt; dùng SQLite và dữ liệu giả lập, không tạo đơn Production hoặc chuyển tiền.

Source hiện dùng PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents và PaymentAuditLogs cho SePay. SubscriptionPackages/UserSubscriptions và lịch sử RevenueTransactions được giữ. Dashboard chỉ tính PaymentOrders Paid có giao dịch SePay Paid phù hợp tại PaidAt; không cộng thêm RevenueTransactions. Các mục bên dưới mô tả luồng thanh toán đã có; hướng dẫn migration mới và chuyển đổi nghiệp vụ nằm trong README_DRAMA.

Build và test nghiệp vụ đã chạy bằng .NET; chưa xác minh SQL Server, SMTP, Google hoặc giao dịch SePay thật. Test thanh toán dùng SQLite và receipt giả lập bên trong test, không có endpoint giả lập trong API và không chuyển tiền thật. Việc đọc SQL Server ngoài sandbox bị từ chối; không thay đổi database trong phiên refactor này.

### Chẩn đoán GET /api/subscription-packages trên Render

Endpoint public chỉ SELECT các gói active, sắp xếp theo tier/giá; không yêu cầu JWT, khóa SePay hoặc R2. Truy vấn và DTO thành công được giữ nguyên. EF Core SQL Server có thể bọc một số lỗi provider trong `InvalidOperationException` ([source EF 8.0.10](https://github.com/dotnet/efcore/blob/v8.0.10/src/EFCore.SqlServer/Storage/Internal/SqlServerExecutionStrategy.cs)); middleware trước đây đổi mọi exception loại này thành 409 nên có thể che lỗi hạ tầng thành xung đột nghiệp vụ. Chỉ từ response 409 chưa xác định được lỗi gốc trên Render.

Luồng đọc gói hiện giữ cause trong `SubscriptionPackagesReadException` để phân loại SQL: thiếu bảng/cột trả 503 `DATABASE_UPGRADE_REQUIRED`; lỗi kết nối/DNS/timeout/reset thường gặp trả 503 `DATABASE_UNAVAILABLE`; lỗi đọc dạng InvalidOperation chưa phân loại trả 500 `SUBSCRIPTION_PACKAGES_UNAVAILABLE` và ghi exception/trace ở server. Response không chứa SQL, connection string hoặc provider message. Các xung đột nghiệp vụ 409 khi tạo/hủy đơn được giữ nguyên. Không bật retry toàn bộ transaction hoặc thay dữ liệu/gói để che lỗi đọc.

**Lỗi trước khi vào action đã tái hiện bằng HTTP:** constructor `PaymentService` và `VietQrGateway` trước đây đọc ngay `IOptions<SePayOptions>.Value`. Nếu `SePay:OrderLifetimeMinutes` không bind được thành int, DI ném InvalidOperation trước khi vào `PackagesAsync`, nên vẫn nhận 409 dù đã có catch lỗi truy vấn. Test HTTP với cấu hình sai tái hiện 409 trước bản sửa; sau bản sửa trả 200 và thực thi SELECT. Hai service hiện chỉ đọc options khi chức năng thanh toán cần dùng, không nạp sớm trong constructor. Cùng cấu hình sai vẫn bị từ chối khi tạo đơn, không thay options bằng giá trị giả hoặc bỏ kiểm tra thanh toán. Đây là nguyên nhân đã tái hiện offline; chưa có log Render để xác nhận cấu hình thực tế của request người dùng.

Trên Render → Environment, `SePay__OrderLifetimeMinutes` phải là số nguyên từ 5 đến 120, ví dụ nhập **15** trực tiếp trong ô Value; không kèm dấu nháy, đơn vị hoặc dòng `SePay__OrderLifetimeMinutes=`. Nếu không đặt biến này, mặc định là 15. Không cần thay khóa API SePay hoặc JWT để sửa giá trị phút.

Sau khi deploy bản sửa, gọi lại endpoint rồi đối chiếu Render Logs bằng traceId mới và `SqlNumber`. Nếu vẫn lỗi, cần exception thực tế và kết quả API đọc khác như `/api/works?page=1&pageSize=10` để xác định kết nối Render–Somee, schema hoặc dữ liệu. Kiểm tra biến `ConnectionStrings__DefaultConnection` ở Render riêng: cấu hình local không được publish lên Render. Không import lại hoặc chạy migration chỉ dựa trên 409.

Kiểm thử offline có chặn kết nối database thật: pipeline SQL Server của endpoint public trả DTO với Code/BillingPeriod null từ dữ liệu giả lập gói import, không cần cấu hình ngân hàng; HTTP localhost thực xác minh GET vẫn 200 khi options SePay không bind được nhưng tạo đơn vẫn bị từ chối; SQL lỗi bọc nhiều lớp vẫn có response/trace đúng; không trả chi tiết provider cho client; business conflict vẫn 409. **226/226 test hồi quy đạt**, báo cáo `tests/VietVan.Payment.Tests/TestResults/subscription-package-di-regression.trx`. Chưa xác minh bản sửa trên Render hoặc đọc Somee thật trong lần chẩn đoán này.

File sửa cho lỗi nạp options sớm: `Services/Payments/PaymentService.cs`, `Services/Payments/VietQrGateway.cs`, `tests/VietVan.Payment.Tests/SubscriptionPackageReadTests.cs`, `README_PAYMENT.md`. Truy vấn, DTO và database không thay đổi.

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
