# Kiểm toán API và database Việt Văn

Ngày 25/11/2026. Đây là báo cáo tiến độ, không phải chứng nhận toàn bộ API hoạt động.

## Môi trường và giới hạn

- `VietVanDbContext` dùng SQL Server qua `DefaultConnection`; cấu hình Development trỏ tới `localhost\SQLEXPRESS`, `VietVan_New`, Windows Authentication. Chưa xác minh database thực tế mà tiến trình đang chạy sử dụng.
- `sqlcmd` trong sandbox không kết nối được; lần yêu cầu chạy ngoài sandbox trước đó bị từ chối. Không coi đây là lỗi schema. Chưa xác minh số hàng, bảng, cột, FK hay index thực tế. Chạy `scripts/Diagnose_VietVan_New.sql` trong SSMS trên VietVan_New để lấy metadata và số hàng ước tính; cần quyền xem metadata đầy đủ. Không chạy FreshBaseline trên database có dữ liệu.
- Source có `scripts/VietVan_New_Baseline.sql`, nhiều upgrade và migration, nhưng không thấy hai file SQL được nêu tên trong workspace. Baseline là schema dự kiến, không phải bằng chứng schema hiện tại. Model `SubscriptionPackage` yêu cầu `Benefits` (số nhiều), `BillingPeriod`, `Code`, `CreatedAt`, `Description`, `UpdatedAt`; chưa thể xác minh các cột thực tế hoặc kết luận lỗi cũ đã được giải quyết.
- Git báo thư mục không phải repository; không thể xác định thay đổi chưa commit. Không tìm thấy AGENTS.md trong các vị trí đã kiểm tra.
- `wwwroot` có Drama Studio, Drama Viewer, VIP và Admin Payments. Chưa kiểm tra được ứng dụng frontend khác hay tất cả luồng UI.

## Phát hiện và sửa chữa

- Drama Studio trước đây chỉ gửi tên kịch, không tải tác phẩm hay gửi `literaryWorkId`. Đã thêm dropdown gọi `GET /api/admin/works`, tìm kiếm, phân trang, trạng thái tải/rỗng/lỗi và gửi ID khi tạo. Chưa kiểm thử trình duyệt hoặc xác minh hiển thị tác phẩm đã liên kết khi mở lại kịch; chưa có chức năng sửa liên kết trên giao diện.
- `DramaProduction.LiteraryWorkId` nullable, FK cấu hình `NoAction`; service kiểm tra ID tồn tại và cho phép tác phẩm inactive. Public drama lọc theo `IsPublished` và trạng thái active của tác phẩm. Không tạo quan hệ hay bảng mới, không thay đổi dữ liệu.
- Chưa xác minh live việc chống tạo trùng tác phẩm, bảo mật media, thanh toán, SMTP, Google, VIP, quyền Customer hay nghiệp vụ từng endpoint. Không chạy giao dịch thật.

## Kiểm thử đã chạy

- Restore thành công; build đầu ra riêng thành công, 0 warnings, 0 errors. Build mặc định trước đó bị khóa bởi tiến trình API đang chạy; không dừng tiến trình.
- `dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj --no-restore -o artifacts/audit-tests`: 62 pass, 0 fail. Đây là kết quả bộ test hiện có, không chứng minh mọi endpoint đã được integration test.
- Swagger tại `localhost:5080/swagger/v1/swagger.json` trả 200. Bốn request không token tới `/api/admin/dramas`, `/api/admin/works`, `/api/admin/payments`, `/api/subscriptions/me` đều trả 401. Chưa kiểm thử Customer/Editor/VIP hay database độc lập.
- Chưa chạy được kiểm tra cú pháp JavaScript bằng `node --check` vì `node` không có trong PATH; chưa có kết quả kiểm thử trình duyệt.
- Request Admin `GET /api/admin/dramas` do người dùng cung cấp trả 200 với `[]`; đây là danh sách kịch, không phải danh sách tác phẩm. Chưa xác minh dữ liệu tác phẩm live.
- Không cần upgrade SQL cho thay đổi giao diện này. Chưa thể khẳng định database không cần upgrade khác.

## Danh sách endpoint

Lấy từ Swagger của tiến trình hiện đang chạy; role và bảng bên dưới là phạm vi dự kiến theo controller/model, không phải xác nhận schema hay truy cập dữ liệu live. `NOT TESTED` nghĩa là chưa kiểm tra đầy đủ endpoint; các kiểm tra 401 riêng được nêu phía trên. POST JSON admin works cũng tồn tại trong source nhưng bị ẩn khỏi Swagger, cùng route với POST multipart.

| Method | Endpoint | Role | Database Tables (dự kiến) | Test Status | Issue | Fix |
|---|---|---|---|---|---|---|
| PUT | `/api/admin/dramas/{dramaId}/scenes/{sceneId}/preview-media` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/dramas/{dramaId}/preview` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/dramas/{dramaId}/assets/{assetId}/preview` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/dramas/{dramaId}/assets/{assetId}/media` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/dramas` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/dramas` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/dramas/{dramaId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/dramas/{dramaId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PATCH | `/api/admin/dramas/{dramaId}/publish` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/dramas/{dramaId}/assets` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| DELETE | `/api/admin/dramas/{dramaId}/assets/{assetId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/dramas/{dramaId}/scenes` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/dramas/{dramaId}/scenes/{sceneId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| DELETE | `/api/admin/dramas/{dramaId}/scenes/{sceneId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/dramas/{dramaId}/scenes/{sceneId}/hotspots` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/dramas/{dramaId}/hotspots/{hotspotId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| DELETE | `/api/admin/dramas/{dramaId}/hotspots/{hotspotId}` | Admin | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/payments` | Admin | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/payments/reconciliation` | Admin | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/payments/{id}` | Admin | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/subscription-packages` | Admin | SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/subscription-packages` | Admin | SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| PUT | `/api/admin/subscription-packages/{id}` | Admin | SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/works` | Admin | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/admin/works` | Admin | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| PATCH | `/api/admin/works/{id}/toggle-status` | Admin | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| DELETE | `/api/admin/works/{id}` | Admin | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/admin/analytics/revenue` | Admin | RevenueTransactions | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/register` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/login` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/verify-email` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/resend-verification` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas/{dramaId}` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas/{dramaId}/scenes/{sceneId}` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas/{dramaId}/hotspots/{hotspotId}` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas/{dramaId}/insights/usage` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/dramas/{dramaId}/assets/{assetId}/media` | Guest / tùy quyền nội dung | DramaProductions, DramaScenes, DramaAssets, DramaHotspots, LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/google/challenge` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/auth/google` | Guest / tùy quyền nội dung | Users, UserAuthStates, EmailVerificationTokens, UserExternalLogins | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/payments/orders` | Đăng nhập | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/payments/orders/{orderCode}` | Đăng nhập | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/payments/orders/{orderCode}/cancel` | Đăng nhập | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/payments/history` | Đăng nhập | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| POST | `/api/payments/webhooks/sepay` | Guest / tùy quyền nội dung | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs, UserSubscriptions | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/subscription-packages` | Guest / tùy quyền nội dung | SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/subscriptions/me` | Đăng nhập | UserSubscriptions, SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/subscriptions/me/entitlements` | Đăng nhập | UserSubscriptions, SubscriptionPackages | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/works` | Guest / tùy quyền nội dung | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
| GET | `/api/works/{id}/content` | Guest / tùy quyền nội dung | LiteraryWorks | NOT TESTED | Chưa xác minh đầy đủ | — |
