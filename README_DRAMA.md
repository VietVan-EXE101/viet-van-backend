# Việt Văn — backend video FREE và kịch tương tác PRO

Cập nhật 25/11/2026. Backend giữ .NET 8 và các dependency production hiện có. Không sửa Frontend, không reset database, không seed giá/giao dịch mới, không commit/push. Thư mục làm việc không có metadata `.git`; không thể xác nhận diff với commit gốc.

## Trạng thái triển khai và kiểm chứng

- Đã triển khai video FREE không cần scene, manifest PRO, bản nháp/xuất bản riêng, upload nhiều file, metadata Việt–Anh, feedback có điều kiện truy cập, quản trị tài khoản và dashboard.
- Giữ đăng ký/email verification/Google, SePay, các gói/giá/thời hạn đã có, kho media riêng và metadata LiteraryWorks. Mọi gói trả phí còn hạn cấp cùng quyền PRO; giữ TierLevel gốc phục vụ dữ liệu đã bán.
- Đã ngừng nội dung đọc và preview/giới hạn insight. Các route đã ngừng trả 410; dữ liệu và bảng cũ được giữ. Không phát hiện module PDF/audiobook/bookmark/reading-progress/collection riêng trong source hiện tại để xóa.
- Chưa áp dụng migration hoặc xác minh schema/số hàng trên SQL Server thực tế: kết nối trong sandbox thất bại, yêu cầu đọc ngoài sandbox bị từ chối. Không coi baseline/script là bằng chứng schema live.
- Build, test, Swagger và kiểm tra HTTP được ghi ở cuối tài liệu. Không tuyên bố sẵn sàng production khi chưa kiểm tra SQL Server, SMTP, Google và webhook SePay thật.

## Quyền truy cập và dữ liệu

Role JWT giữ tên `Admin` / `Customer` để tương thích Frontend hiện tại. `FREE` / `PRO` là quyền subscription, không phải role. API tính lại quyền từ `UserSubscriptions` với `IsActive`, `StartDate <= UTC now < EndDate` và `GrantedTierLevel ?? Package.TierLevel > 0`; không dùng claim tier hay `VipExpiresAt` để quyết định quyền.

`OnTokenValidated` đọc trạng thái tài khoản, role và `TokenVersion` từ DB. Khóa tài khoản, logout hoặc reset mật khẩu vô hiệu hóa JWT cũ. Logout hiện thu hồi **mọi phiên** của tài khoản. Kiến trúc hiện tại chỉ có access token, nên không bổ sung hệ refresh-token song song. JWT không hợp lệ gửi kèm API công khai cũng nhận 401; client cần bỏ token cũ.

Customer chỉ nhận nội dung `Status=PUBLISHED` và `IsPublished=true`. Admin có thể xem draft/disabled, không cần subscription. Mỗi `DramaProduction` có tối đa một `FreeVideoAssetId` và một `DramaManifest` chứa draft/published riêng; hai phiên bản cùng dùng một tác phẩm, không cần tạo thêm bảng DramaVersions hoặc tác phẩm trùng tên.

| Bảng | Thay đổi |
|---|---|
| Users | Thêm FullName nullable; giữ tài khoản, mật khẩu hash và role |
| UserAuthStates | Thêm TokenVersion; tài khoản Customer thiếu trạng thái được tạo trạng thái chưa xác minh |
| DramaProductions | Thêm Status, PublishedAt, FreeVideoAssetId; giữ LiteraryWorkId, AccessLevel và dữ liệu cũ |
| DramaAssets | Thêm NameVi/NameEn/MeaningVi/MeaningEn/DurationSeconds; thêm Music/Effect vào allowlist đã biết |
| DramaManifests | Mới: khóa DramaProductionId; DraftJson, PublishedJson, PublishedLegacyJson, Revision và timestamps |
| DramaViewings | Mới: khóa UserId + DramaProductionId; bằng chứng server đã cấp video/cấu hình tương tác hợp lệ |
| PasswordResetTokens | Mới: hash token, email snapshot, hạn dùng và thời điểm tiêu thụ |
| AdminAuditLogs | Mới: lịch sử khóa/mở khóa tài khoản |
| Reviews / ReviewModerationLogs | Giữ bảng có sẵn, tạo nếu thiếu; comment có thể rỗng; unique theo user + drama và rating 1–5 |
| PaymentOrders / PaymentBankTransactions / PaymentWebhookEvents / PaymentAuditLogs | Giữ; thêm index Status + PaidAt |
| SubscriptionPackages / UserSubscriptions | Giữ giá, thời hạn, tier; thêm index truy vấn thời hạn |
| LiteraryWorks / RevenueTransactions / DramaScenes / DramaHotspots / CulturalInsightViews | Giữ dữ liệu và quan hệ cũ; không dùng RevenueTransactions để tự quyết định doanh thu/thanh toán |

## Migration an toàn

Migration mới: `Data/Migrations/20261125103354_DramaPlatformRefactor.cs`, kèm designer và model snapshot. Migration nhúng cùng SQL ở `scripts/Upgrade_Drama_Platform.sql`, có transaction, guard prerequisite và marker `__VietVanUpgrades`. Không tự migration khi API khởi động. Down bị chặn để tránh xóa lịch sử.

Trước khi áp dụng, đối chiếu DB đang cấu hình với các script chẩn đoán hiện có, kiểm tra backup và schema Auth/Payment/Drama. Script tạo `Reviews` và `ReviewModerationLogs` khi thiếu, giữ nguyên bảng và dữ liệu đánh giá đã có. Nếu thiếu prerequisite khác, lỗi liệt kê tên bảng còn thiếu; script không tạo baseline trên database đang có dữ liệu. Những upgrade lịch sử có thể có guard/tên database khác nhau, cần kiểm tra từng script thay vì chạy hàng loạt.

Nếu SSMS báo `Msg 51001 ... Missing prerequisite schema` với script cũ và Object Explorer không có `dbo.Reviews`, mở lại file `scripts/Upgrade_Drama_Platform.sql` đã cập nhật từ ổ đĩa (không Execute lại nội dung cũ đang mở). Chọn đúng database `VietVan_New`, chạy toàn bộ script. Bản sửa tạo hai bảng review còn thiếu trong cùng transaction với `Users.FullName` và `UserAuthStates.TokenVersion`; không đổi mật khẩu hoặc giá gói. Chỉ khi thành công mới in `Drama platform upgrade completed. Retry login and POST /api/admin/dramas.`. Sau đó thử đăng nhập lại; lỗi 503 còn tồn tại cần đối chiếu tên cột/bảng cụ thể trong log Backend hoặc kết quả `Diagnose_AuthRegister.sql`.

### Khi upload qua ngrok báo 400 MinRequestBodyDataRate

Thông báo `Failed to read the request form ... data arriving too slowly ... MinRequestBodyDataRate` cho biết Kestrel ngắt đọc multipart trước khi action tạo kịch được gọi. Cần phân biệt lỗi truyền file này với lỗi schema 503. Mặc định Kestrel kiểm tra tốc độ tối thiểu 240 byte/giây với grace period 5 giây; xem [tài liệu Microsoft](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations).

`Filters/UploadRequestAttribute.cs` chạy ở resource filter trước MVC model binding, áp dụng riêng cho các action multipart tạo kịch/tác phẩm, upload cover/video/assets. Với HTTP/1.x, ngưỡng upload là 64 byte/giây, grace period 60 giây. Giới hạn request/file, xác thực, rate limiter hiện có và kiểm tra MIME/signature vẫn áp dụng. Không tắt minimum data rate trên toàn server. HTTP/2 giữ cấu hình server vì Kestrel không hỗ trợ đặt ngưỡng khác null theo từng request HTTP/2; xem [Kestrel options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-8.0#minimum-request-body-data-rate).

Dừng rồi build/chạy lại tiến trình Backend để nạp filter mới; chỉ refresh Swagger không đủ. Thử cùng file qua `http://localhost:5080` trước, sau đó qua ngrok để phân biệt vấn đề đường truyền còn tồn tại. Swagger tạo kịch đã mô tả 400/413/415/503. Không cần chạy migration cho lỗi tốc độ upload này.

### Khi tạo kịch có ảnh bìa báo 503 DATABASE_UPGRADE_REQUIRED

Payload multipart gồm `Title`, `Description`, `IsPublished=false` và `coverImage` WebP là hợp lệ về cấu trúc; có thể bỏ `LiteraryWorkId` và `AccessLevel` (mặc định `Public`). Vẫn cần nội dung file WebP hợp lệ và dung lượng tối đa 5 MiB. Lỗi schema SQL không xác định được cột cụ thể từ response 503; lấy exception có `Invalid column name` / `Invalid object name` tương ứng `traceId` ở console Backend.

Log người dùng cung cấp cho trace `0HNQAV9L1K06J:00000004` xác nhận lỗi SQL 207 tại `DramaService.DetailAsync`, sau khi luồng tạo đã lưu drama và bìa trong transaction: `DramaScenes` thiếu `SceneKey`, `Title`, `StartMs`, `EndMs`, `AudioOffsetMs`, `BackgroundAssetId`, `AudioAssetId`, `VideoAssetId`. EF biên dịch truy vấn Include nên vẫn lỗi dù drama mới chưa có scene. Điều này rộng hơn các cột preview đã bổ sung trước đó: cấu trúc scene của database khác EF model.

Đã sửa `CreateCoreAsync` trả DTO từ drama và asset bìa vừa lưu, với scenes rỗng theo luồng tạo nháp. Không gọi truy vấn toàn bộ scene/hotspot trước khi commit. `DetailAsync` đọc metadata/assets, kiểm tra scene theo `DramaProductionId`, rồi chỉ đọc đầy đủ scene/hotspot khi drama đó có scene. Các bản nháp mới, video-only và manifest mới không cần các trường legacy chưa dùng. Restart Backend để nạp sửa đổi này.

Phạm vi bản sửa là luồng kịch mới chưa có scene legacy. Kịch có scene legacy vẫn đọc dữ liệu thật và tiếp tục báo lỗi schema khi mapping không tương thích; không bắt lỗi SQL rồi giả vờ trả scenes rỗng. File schema lịch sử `VietVanNew.sql` dùng `DramaScenes.WorkId/Name`, media và playback segments riêng, khác model cũ của API. Cần đối chiếu đầy đủ metadata/dữ liệu trước khi chuyển đổi kịch đã có; không thêm timeline giả hoặc tự coi ID `DramaMediaAssets` là ID `DramaAssets`.

Với lỗi schema khác, chạy `scripts/Diagnose_Drama_Schema.sql` trong đúng instance/database của API để xem các dòng `MISSING_COLUMN_OR_NOT_VISIBLE` / `MISSING_TABLE_OR_NOT_VISIBLE` (cần quyền xem metadata). `Upgrade_Drama_Platform.sql` bổ sung các trường platform và preview đã biết, không chuyển đổi đầy đủ mô hình scene WorkId/Name sang scene của API. Bản sửa tạo nháp trên không thay đổi database. Chưa xác minh thực thi trên SQL Server live.

Với database dùng history bootstrap riêng, dùng script mới đã review trong đúng database; **không chạy toàn bộ migration API lịch sử**. Script không giả lập các migration cũ đã chạy và không sửa history bootstrap. Với database dùng history API đã đối chiếu, có thể dùng migration EF mới qua connection được chỉ định rõ. Design-time factory mặc định dùng `VietVan_DesignOnly`; muốn cập nhật DB bằng EF phải cấu hình `VIETVAN_MIGRATION_CONNECTION` hoặc connection tường minh.

Hai ID migration cũ chỉ có 8 chữ số ngày; EF CLI diễn giải tên ngắn không đúng quy ước hiện nay. Không đổi ID hoặc tự chèn history khi chưa kiểm tra database. Script mới là lối áp dụng độc lập cho môi trường bootstrap. SQL sinh toàn bộ lịch sử trong artifacts chỉ để review, không dùng trực tiếp để upgrade database hiện có.

Tác phẩm đã xuất bản được backfill Status từ IsPublished; PublishedAt của chúng là thời điểm chuyển đổi vì không có ngày xuất bản lịch sử đáng tin cậy. Không tự coi video trong scene PRO là video FREE. Admin cần upload video FREE rõ ràng.

Các cảnh/hotspot cũ giữ nguyên timestamp, audio offset, ActionKey và TargetSegmentIds. Backend chụp snapshot trước lần sửa/upload đầu tiên trên tác phẩm đang xuất bản. Customer tiếp tục đọc snapshot cũ đến lần publish tiếp theo. Manifest mới dùng schema 1; tác phẩm chưa chuyển đổi trả adapter schema 0.

## Cấu hình và chạy

### Khi forgot-password trả 200 nhưng Gmail không nhận thư

Kiểm tra cấu hình cục bộ ngày 25/11/2026 cho thấy SMTP Gmail có host/port/user/password/from trong User Secrets, nhưng `Email:PasswordResetUrl` chưa có trong appsettings, User Secrets hoặc biến môi trường khả dụng trong phiên kiểm tra. Đây chưa phải bằng chứng SMTP đăng nhập/gửi thư thành công, và tiến trình đang chạy có thể có môi trường riêng. Source cũ dừng trước `IEmailSender.SendAsync` nếu URL reset thiếu/sai nhưng vẫn trả thông báo chung 200.

`Email:PasswordResetUrl` hiện là tùy chọn. Khi chưa có trang reset FE, email chứa mã reset 43 ký tự dùng với POST `/api/auth/reset-password` body `{ "token": "<mã trong email>", "password": "<mật khẩu mới>" }`. Đây là token 32 byte ngẫu nhiên hiện có, sống 30 phút, chỉ dùng một lần; DB chỉ lưu hash. Không trả token trong response forgot-password hoặc ghi vào log.

Khi đã có trang reset FE, cấu hình `Email:PasswordResetUrl` bằng URL thật để email kèm liên kết với cùng mã ở fragment `#`. HTTPS dùng khi triển khai; Development cho phép HTTP localhost; URL không chứa userinfo hoặc fragment có sẵn. Trang FE đọc token từ `location.hash`, rồi POST `{ token, password }` đến `/api/auth/reset-password`. Không điền URL API POST reset thay cho trang FE. Chỉ khi URL được cấu hình nhưng sai thì API trả 503 `PASSWORD_RESET_NOT_CONFIGURED` cho mọi email, trước khi tìm tài khoản. Thông báo 200 vẫn chung cho tài khoản hợp lệ/không có/bị khóa hoặc lỗi SMTP, giữ contract không tiết lộ tài khoản. Chờ ít nhất một phút giữa hai yêu cầu gửi cho cùng tài khoản.

Log server theo dõi `PasswordResetEmailStarted`, `PasswordResetEmailAcceptedByProvider`, `PasswordResetEmailFailed` (Type/SmtpStatus) và `PasswordResetEmailSkipped ... Reason=Cooldown/AccountNotEligible`. Không ghi token, nội dung email, mật khẩu SMTP hoặc địa chỉ người nhận. `AcceptedByProvider` chỉ có nghĩa SMTP đã chấp nhận; nếu có log này mà chưa thấy thư, kiểm tra thư spam và trạng thái gửi của tài khoản SMTP. Restart Backend sau khi đổi code/cấu hình. Chưa gửi email thật trong lần kiểm tra này; SMTP còn cần kiểm chứng thực tế.

`Configure-Integrations.ps1` nay hỏi thêm URL trang reset; `Test-Integrations.ps1` kiểm tra khóa này. Các script đã được kiểm tra cú pháp nhưng không chạy hoặc thay execution policy. Khi thao tác cấu hình bằng công cụ khác, key tương ứng là `Email__PasswordResetUrl` (environment) hoặc `Email:PasswordResetUrl` (User Secrets).

### Khi POST /api/auth/login báo lỗi server

Đối chiếu `traceId` response với log `API failure` ở console Backend. Log mới ghi exception và SqlNumber ở server; response giữ thông báo an toàn. Các mã phân biệt:

- `503 DATABASE_UPGRADE_REQUIRED`: SQL báo thiếu cột/bảng (207/208). Chạy script chỉ đọc `scripts/Diagnose_AuthRegister.sql` trong đúng DB để kiểm tra, đặc biệt `Users.FullName` và `UserAuthStates.TokenVersion`; sau đó review/apply `scripts/Upgrade_Drama_Platform.sql` nếu thiếu upgrade này và các prerequisite đã có. Không chạy baseline/reset DB để sửa.
- `503 DATABASE_UNAVAILABLE`: kiểm tra kết nối, instance, tên database hoặc tài khoản SQL; migration không sửa lỗi kết nối.
- `401`: sai thông tin, hash lưu không hợp lệ, email chưa xác minh hoặc tài khoản bị khóa. Không tự ghi password plain text hoặc đổi mật khẩu Admin để bỏ qua lỗi.

Login tiếp tục chấp nhận mật khẩu cũ 6 ký tự nếu hash hợp lệ; yêu cầu tối thiểu 8 ký tự áp dụng cho đăng ký/reset mới. Hash malformed được từ chối bằng 401, không còn làm văng FormatException thành 500. Swagger login đã mô tả thêm 400/401/429/500/503.

Sau khi sửa source, dừng tiến trình API đang chạy rồi build/chạy lại để nạp assembly mới. Chỉ mở lại Swagger hoặc bấm Execute không cập nhật code của tiến trình cũ.

Giữ thông tin ngân hàng hiện có trong secret/env. File `configuration/INTEGRATION_SETTINGS.example.json` chỉ chứa placeholder; không chép nguyên file đó vào production.

| Nhóm | Các khóa cần cấu hình |
|---|---|
| SQL Server | `ConnectionStrings__DefaultConnection` |
| JWT | `Jwt__Key` (tối thiểu 32 ký tự ngẫu nhiên), Issuer, Audience, ExpiryMinutes |
| SMTP STARTTLS | `Email__Smtp__Host`, Port, UserName, Password, FromAddress |
| Xác minh email | `Email__VerificationBaseUrl` (origin HTTPS của backend; dùng trang verify-email hiện có) |
| Reset mật khẩu | `Email__PasswordResetUrl` tùy chọn (trang FE HTTPS nhận token từ fragment); bỏ trống thì email chứa mã reset |
| Google | `GoogleSignIn__ClientId` đúng client ID của Frontend |
| SePay/VietQR | `SePay__BankId`, BankName, AccountNumber, AccountHolder, WebhookApiKey, OrderLifetimeMinutes |
| Media | `Storage__RootPath`, mặc định PrivateMedia, bắt buộc ngoài wwwroot |
| CORS | `Cors__AllowedOrigins__0`, `__1`, …; origin chính xác, không wildcard với credentials |

Backend giữ OIDC Google với issuer/audience/expiration/signature/email_verified/subject/nonce. Frontend gọi challenge, đưa nonce vào Google Sign-In, giữ cookie và gửi ID token. Email đã có tài khoản trả ACCOUNT_LINK_REQUIRED; sau khi đăng nhập tài khoản hiện có, gọi link với challenge mới. Không tự liên kết chỉ dựa trên email. Cookie challenge dùng SameSite=Strict: triển khai Frontend/API cùng site hoặc qua reverse proxy; challenge store hiện ở RAM nên triển khai nhiều instance cần cùng nơi xử lý challenge hoặc thay bằng shared store.

Đăng ký email nhận `emailVerificationRequired=true`, `verificationEmailSent` phản ánh kết quả gửi thực tế, `token` rỗng. Token xác minh sống một giờ, một lần dùng, gửi lại tối đa mỗi phút/tài khoản; các API auth bị giới hạn theo IP. Customer cũ thiếu bằng chứng xác minh cần xác minh email sau upgrade. Reset token sống 30 phút, lưu hash, một lần dùng; response forgot-password không tiết lộ tài khoản tồn tại.

```powershell
dotnet restore VietVan.API.csproj
dotnet build VietVan.API.csproj --no-restore
dotnet restore tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj
dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj --no-restore
dotnet run --launch-profile http
```

Không cần chạy seed demo. Các trang HTML cũ chưa được sửa trong đợt refactor này.

## API mới và khả năng mới

Các response cũ giữ dạng object/array/phân trang, không thêm wrapper toàn cục. Lỗi API có message và có thể thêm traceId; không trả chi tiết exception server.

| Method | Route | Quyền/chức năng |
|---|---|---|
| POST | /api/auth/forgot-password | Gửi mã reset qua email; kèm link nếu có URL FE |
| POST | /api/auth/reset-password | Tiêu thụ token, đổi mật khẩu, thu hồi JWT |
| POST | /api/auth/logout | Đăng nhập; thu hồi mọi phiên |
| GET | /api/auth/me | Profile và quyền hiện hành |
| POST | /api/auth/google/link | Đăng nhập + Google token/nonce đã xác minh |
| GET | /api/dramas/{id}/video | Video FREE; HTTP Range, Admin có thể preview |
| GET | /api/dramas/{id}/interactive | PRO/Admin; manifest và asset metadata trong một response |
| PUT / DELETE | /api/dramas/{id}/feedback | Tạo/cập nhật/xóa đánh giá của chính mình |
| PATCH | /api/admin/dramas/{id}/status | DRAFT / PUBLISHED / DISABLED |
| GET / PUT | /api/admin/dramas/{id}/manifest | Đọc/lưu draft với optimistic Revision |
| GET | /api/admin/dramas/{id}/assets | Asset metadata + URL preview có xác thực |
| POST | /api/admin/dramas/{id}/video | Multipart file video, không cần scene |
| POST | /api/admin/dramas/{id}/cover | Multipart ảnh bìa |
| PUT | /api/admin/dramas/{id}/assets/{assetId}/metadata | Metadata Việt–Anh và duration |
| DELETE | /api/admin/dramas/{id} | Chỉ draft rỗng chưa từng xuất bản/chưa có lịch sử |
| GET | /api/admin/users | Tìm tên/email, lọc FREE/PRO/locked, phân trang |
| GET | /api/admin/users/{id} | Profile trong items, cùng dạng phân trang |
| PATCH | /api/admin/users/{id}/lock | `{ "isLocked": true/false }`, có audit |
| GET | /api/admin/dashboard | Doanh thu/tài khoản/tác phẩm/rating, groupBy day hoặc month |

Upload chung vẫn là `POST /api/admin/dramas/{id}/assets`. Form cũ `kind`, `title`, `file` trả một asset; form nhiều file `assetType`, `files` trả array asset. AssetType nhận Background/Character/Object/Audio/Music/Effect, không phân biệt hoa thường. Không cần controller riêng cho từng loại tài nguyên. Video FREE dùng route video.

Các route thanh toán hiện có tiếp tục dùng: `GET /api/subscription-packages`, `GET /api/subscriptions/me`, `GET /api/subscriptions/me/entitlements`, `POST /api/payments/orders`, `GET /api/payments/orders/{orderCode}`, `POST .../{orderCode}/cancel`, `GET /api/payments/history`, `POST /api/payments/webhooks/sepay`. Không tạo alias trùng chỉ để giống thiết kế tham khảo.

Feedback chỉ được ghi sau khi server cấp video hoặc cấu hình tương tác hợp lệ, đồng thời người dùng còn quyền truy cập tác phẩm. Đây là bằng chứng đã mở nội dung, không chứng nhận xem hết video. Client không gửi isWatched. Rating 1–5, comment tùy chọn, một user/drama, cập nhật không tự gỡ trạng thái Hidden. Chi tiết tác phẩm trả averageRating, totalRatings và myRating; danh sách feedback giữ `GET /api/dramas/{id}/reviews` có phân trang.

## API thay đổi, deprecated và mapping

| API cũ | Hành vi hiện tại / đích chuyển đổi |
|---|---|
| POST /api/auth/register | Nhận email/fullName/password; username cũ vẫn được hỗ trợ; chưa cấp JWT trước xác minh |
| GET /api/dramas | Giữ array, thêm Status/HasFreeVideo; chỉ published |
| GET /api/dramas/{id} | Metadata cho FREE, không có asset/scene PRO; PRO nhận snapshot legacy nếu còn schema 0 |
| GET /api/dramas/{id}/assets/{assetId}/media | Cover/video FREE được phép; asset khác cần PRO/Admin và thuộc bản xuất bản |
| POST /api/admin/dramas/{id}/assets | Mở rộng nhiều file, dùng cùng storage/validation |
| POST /api/admin/dramas | Chỉ tạo draft; multipart cover hoặc JSON cũ; upload rồi publish riêng |
| PUT /api/admin/dramas/{id} | Sửa metadata; không dùng IsPublished để đổi trạng thái |
| PATCH .../{id}/publish | Deprecated adapter sang status PUBLISHED/DISABLED |
| Admin scene/hotspot CRUD | Deprecated, vẫn hoạt động; ưu tiên GET/PUT manifest, sau đó publish |
| Customer scene/hotspot/insights/usage | Deprecated, luôn cần PRO; đọc snapshot; chuyển sang GET interactive |
| POST /api/dramas/{id}/reviews | Deprecated create-only; chuyển PUT .../feedback |
| PUT/DELETE /api/reviews/{reviewId} | Deprecated adapter, chỉ chủ sở hữu; chuyển .../dramas/{id}/feedback |
| GET /api/works/{id}/content | 410; chuyển sang drama video hoặc interactive |
| GET/POST /api/works/{id}/reviews và summary | 410; dùng đánh giá của drama; giữ dữ liệu review cũ cho quản trị |
| Admin preview/preview-media | 410; FREE video đầy đủ, PRO interactive |
| GET /api/admin/analytics/revenue | Giữ wrapper; doanh thu từ PaymentOrders Paid có bank transaction phù hợp |
| Admin works | Giữ metadata/tác giả/liên kết; create trả DTO, Content không còn bắt buộc/không lưu cho tác phẩm mới |

Các route 410 được ẩn khỏi Swagger. Adapter còn dùng được được đánh dấu deprecated; chưa xóa endpoint scene/hotspot mà Frontend hiện đang gọi. Không xóa bảng/cột đọc cũ và không chuyển chúng thành module song song mới.

## Ví dụ request/response

Đăng ký `POST /api/auth/register`:

```json
{"email":"reader@example.com","fullName":"Nguyễn Văn An","password":"ExamplePass123!"}
```

Đăng nhập vẫn dùng `{ "usernameOrEmail": "reader@example.com", "password": "..." }`. Xác minh dùng `{ "token": "..." }`; reset dùng `{ "token": "...", "password": "..." }`. Me trả profile, role, accessLevel và proUntil; không trả hash/token bí mật.

Tạo drama JSON (hoặc multipart với title/description/coverImage):

```json
{"title":"Tên vở kịch","description":"Mô tả","literaryWorkId":null}
```

Sau đó upload `POST /api/admin/dramas/{id}/video` bằng multipart field `file`, và publish `PATCH /api/admin/dramas/{id}/status` với `{ "status": "PUBLISHED" }`. Video-only không cần scene, timeline hay hotspot.

Tạo thanh toán `POST /api/payments/orders`, header `Idempotency-Key: unique-request-123`, body `{ "packageId": 2 }`. Server lấy giá từ database, trả OrderCode/Amount/Status/ExpiresAt/QrUrl/TransferContent/BankName/AccountNumber/AccountHolder theo DTO hiện có. Không nhận giá hoặc cờ Paid/PRO từ Frontend. Chủ giao dịch hoặc Admin được xem chi tiết.

SePay giữ API Key: header `Authorization: Apikey <secret>`, JSON có id/accountNumber/transferType/transferAmount/content/code. Key tối thiểu 24 ký tự. Webhook hợp lệ trả 201 + success:true kể cả khi nghiệp vụ đưa giao dịch vào RequiresReview; lỗi lưu trả 503 để có thể retry. Chỉ số tiền chính xác, tiền vào, đúng tài khoản, mã duy nhất và đơn Pending còn hạn mới được cấp quyền. Mã lạ, sai tiền, đơn hết hạn/hủy phải đối soát. ID provider có unique và application lock; cập nhật Payment/Subscription/Revenue/Event cùng transaction. Không có API tự xác nhận Paid.

Nguồn đối chiếu chính thức: [SePay Webhooks](https://docs.sepay.vn/tich-hop-webhooks.html), [Google ID token verification](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token). Không chuyển tiền thật trong test tự động.

Dashboard dùng PaymentOrders đã Paid tại PaidAt và bank transaction SePay Paid khớp số tiền; không cộng lại RevenueTransactions. Khoảng lọc `[fromUtc,toUtc)`, nhóm ngày/tháng theo UTC+7. Tổng/số lượng/nhóm được aggregate trong SQL, không tải toàn bộ giao dịch về RAM. Các endpoint Admin thanh toán/đối soát hiện có được giữ.

## Schema manifest cho Frontend

`PUT /api/admin/dramas/{id}/manifest` nhận:

```json
{
  "revision": 0,
  "manifest": {
    "schemaVersion": 1,
    "startSceneId": "scene-01",
    "scenes": [{
      "id": "scene-01",
      "backgroundAssetId": 10,
      "durationMs": 15000,
      "characters": [{"assetId":11,"position":{"x":0.3,"y":0.5},"scale":1}],
      "objects": [],
      "audioTracks": [{"assetId":12,"startMs":0,"endMs":15000}],
      "hotspots": [{"id":"info-01","assetId":11,"actionType":"SHOW_MEANING","position":{"x":0.3,"y":0.5}}]
    }]
  }
}
```

ID minh họa phải thay bằng asset thực tế của đúng drama. Background chỉ dùng Background; characters/objects đúng loại; audioTracks dùng Audio/Music/Effect có MIME audio. SHOW_MEANING cần NameVi/MeaningVi của asset. GO_TO_SCENE cần targetSceneId hợp lệ; PLAY_AUDIO cần asset âm thanh. Scene có nextSceneId tùy chọn. Hotspot có startMs/endMs tùy chọn, nằm trong duration cảnh.

Schema 1 dùng tọa độ chuẩn hóa 0–1 (khác schema legacy 0–100%). Scale >0 đến 10; durationMs >0 đến một ngày; JSON tối đa 1 MiB, tối đa 500 scene và 200 thành phần mỗi nhóm/cảnh; trường không được nhận diện bị từ chối. Backend kiểm tra ID duy nhất, cảnh bắt đầu/đích, owner/type asset và timestamps. Không hardcode tên vở kịch hay tự dựng timeline.

GET manifest trả revision hiện tại; save phải gửi đúng revision, nếu lệch trả 409. Publish sao chép draft sang PublishedJson trong transaction và tăng revision. Revision là token chỉnh sửa, không phải số phiên bản public bất biến. Sửa draft không làm đổi PublishedJson. Asset đang thuộc bản xuất bản không được đổi metadata/xóa; upload asset mới, sửa draft, publish lại rồi xóa asset cũ khi không còn tham chiếu. Snapshot legacy bảo vệ toàn bộ thư viện asset của bản cũ trong giai đoạn chuyển đổi.

Customer `GET /api/dramas/{id}/interactive` trả `{ "manifest": {...}, "assets": [...], "publishedAt": "...", "revision": 2 }`; asset có id/kind/title/url/contentType/sizeBytes và metadata Việt–Anh. Schema 0 trả `manifest: { "schemaVersion": 0, "legacy": <DramaDto> }`, giữ scene timing, hotspot, action và target cũ. Frontend cần đọc cả hai trong quá trình chuyển đổi; cấu hình schema 1 không tự chuyển ngược thành CRUD scene cũ.

Admin dùng cùng route interactive với `?preview=true` để xem draft trước khi publish, kể cả khi tác phẩm DRAFT/DISABLED. Customer không được dùng chế độ preview. Chế độ này trả `isPreview=true` với manifest v1 và không ghi nhận phiên xem để đánh giá.

## Media và các việc Frontend cần cập nhật

Media được stream vào file; ảnh 10 MiB, cover 5 MiB, audio 30 MiB, video 200 MiB; tổng request upload tối đa khoảng 202 MiB. Extension/MIME/signature phải phù hợp, không nhận SVG/executable. Tên lưu do server sinh, tên gốc được làm sạch; đường dẫn đọc/xóa phải nằm trong storage. Batch lỗi rollback metadata và dọn các file đã tạo. Thay file bằng upload asset mới rồi đổi tham chiếu, không ghi đè file published.

Không đưa PrivateMedia lên static hosting. URL asset là API có kiểm tra quyền trên mỗi request, không phải signed/public URL vĩnh viễn; response private/no-store. Video và media hỗ trợ HTTP Range và không đọc toàn bộ file vào RAM. Player phải gửi Bearer qua request media có bảo vệ; không truyền token vào query string.

Frontend hiện có gọi GET drama rồi render scene/hotspot, và tải media thành Blob. Cần cập nhật để:

1. Hiển thị hai lựa chọn FREE_VIDEO/PRO_INTERACTIVE trong cùng drama; FREE dùng route video và player hỗ trợ Range, PRO dùng interactive.
2. Hiểu manifest 0/1, normalized coordinates, metadata và Revision; không gửi full manifest v1 vào CRUD scene cũ.
3. Studio tạo draft, upload file, lưu manifest, publish/status riêng; hỗ trợ form files nhiều lần cùng tên trên route assets.
4. Chờ email verification sau đăng ký; thêm reset page đọc fragment; xử lý ACCOUNT_LINK_REQUIRED và logout thu hồi phiên.
5. Chuyển ghi feedback sang PUT/DELETE feedback, chấp nhận comment trống và chỉ mở form sau khi nội dung được server cấp.
6. Xử lý 401 khi phiên bị thu hồi, 403 khi PRO hết hạn, 404 khi drama bị tắt, 409 khi revision xung đột và 410 cho module đã ngừng.

Chưa chỉnh sửa hoặc kiểm thử UI trình duyệt trong đợt này theo giới hạn chỉ sửa Backend.

## Kết quả kiểm tra thực tế

- `dotnet restore` API và test: khôi phục được package; test thêm EF SQLite 8.0.11 từ cache. NuGet audit trả NU1900 vì không truy cập được nuget.org; yêu cầu mạng ngoài sandbox bị từ chối. Không kết luận đã kiểm tra vulnerability online.
- `dotnet build VietVan.API.csproj --no-restore`: thành công, 0 warnings / 0 errors.
- `dotnet test` sau bản sửa login: **119 passed, 0 failed, 0 skipped**, kết quả trong `tests/VietVan.Payment.Tests/TestResults/login-fix.trx`. Test có cảnh báo NU1900 về NuGet audit như trên.
- Bản sửa upgrade cho bootstrap thiếu `Reviews`: build 0 warnings / 0 errors, kiểm tra cú pháp bằng TSql160Parser không có lỗi, chạy lại **119/119 test** (`schema-fix.trx`), sinh lại `artifacts/DramaPlatformMigration.sql` từ assembly mới. Đây là kiểm tra offline, chưa chứng minh thực thi hoặc chạy lặp thành công trên SQL Server.
- Bản sửa tương thích khi tạo kịch: thêm các cột legacy còn mapping vào upgrade tổng, mở rộng chẩn đoán theo cột EF, kiểm tra cú pháp hai script bằng TSql160Parser. Build 0 warnings / 0 errors, **121/121 test** (`drama-schema-fix.trx`), gồm tạo draft với bìa WebP tên tiếng Việt, bỏ LiteraryWorkId/AccessLevel và kiểm tra rollback/file cleanup khi DB từ chối asset. Test dùng SQLite; chưa xác định cột thiếu của trace live hoặc xác minh thực thi nâng cấp trên SQL Server.
- Bản sửa upload chậm qua proxy: **122/122 test** (`upload-transport.trx`). Test HTTP dùng Kestrel thật với ngưỡng mặc định test 10.000 byte/giây / grace 2 giây, gửi multipart ngắt 4 giây: route đối chứng bị ngắt và log minimum data rate, route có filter trả 200; giới hạn request 1 KiB của probe vẫn giữ và request vượt bị từ chối. Test Swagger xác nhận tạo kịch có 200/400/503. Chưa kiểm chứng đường truyền ngrok thực tế hoặc nạp thay đổi vào tiến trình Backend người dùng đang chạy; cần restart.
- Bản sửa tạo/đọc kịch chưa có scene: **124/124 test** (`create-draft-schema.trx`). Test SQLite đổi tên đúng các cột trong log SQL 207, giữ một scene thuộc drama khác, xác nhận truy vấn scene đầy đủ vẫn lỗi ngay cả khi không có hàng phù hợp. Cả JSON/multipart tạo nháp mới, đọc chi tiết/draft, upload video, lưu/publish manifest và publish video-only đều hoạt động; các hàng scene cũ được giữ và lỗi schema cho kịch có scene thật vẫn được báo. Chưa chạy lại request trên SQL Server live.
- Quên mật khẩu hiện tại: **133/133 test** (`reset-code-email.trx`), gồm URL trống/không có gửi mã email dùng để reset thành công rồi bị từ chối khi dùng lại; tài khoản không có/bị khóa không được gửi; URL được cấu hình nhưng sai vẫn có cùng lỗi cho email đã có/không có; SMTP failure có mã trạng thái, accepted/cooldown không ghi token/địa chỉ email. Không đổi source FE. SMTP dùng sender giả lập, chưa kiểm chứng Gmail thực. Kết quả `forgot-email.trx` là contract cũ yêu cầu URL, đã được thay bằng luồng mã email tùy chọn link này.
- Bộ test bao gồm auth email/Google, reset/logout/khóa tài khoản, manifest/legacy snapshot, upload cleanup/path traversal, feedback, SePay giá/ownership/idempotency/sai tiền/mã lạ/late/cancel/renewal/rollback và HTTP Range/PRO expiry. Xem TRX trong `tests/VietVan.Payment.Tests/TestResults` sau khi chạy.
- SQLite dùng FK/unique/transaction thực; interceptor chỉ thay sp_getapplock cho test. Nó không chứng minh locking/retry/concurrency của SQL Server. Test Google dùng token RSA do test ký và cấu hình key giả lập; email dùng sender giả lập, không gửi thư/chuyển tiền thật.
- Swagger đã trả 200, gồm 74 operation công khai tài liệu, trên tiến trình local với JWT/webhook key test tạm thời. Admin/users/dashboard/payments, subscription/me, auth/me và interactive không token trả 401. Nội dung đọc trả 410. Webhook sai key trả 401, payload sai trả 400. Rate limit auth: 10 request model không hợp lệ trả 400, hai request tiếp theo trả 429. Danh sách endpoint cuối được lưu trong `artifacts/refactor-swagger.json` và `artifacts/refactor-api-routes.txt`; kết quả smoke trong `artifacts/refactor-http-smoke.txt`.
- EF migration được sinh kèm snapshot; script SQL được xuất để review, chưa chạy trên SQL Server. Model drift được kiểm tra bằng `migrations has-pending-model-changes`.
- Script PowerShell kiểm tra cấu hình cục bộ không chạy được vì execution policy của máy. Không thay execution policy và không kết luận credential/provider đã sẵn sàng.
- Khi khởi động trong sandbox, DataProtection báo không đọc/ghi được key Windows/DPAPI của tài khoản và HTTP test không có HTTPS redirect port. API/Swagger vẫn chạy; đây không phải xác nhận cấu hình HTTPS/DataProtection production đã hợp lệ. Tiến trình smoke test đã được dừng.
- Chưa xác minh schema/dữ liệu live, áp dụng/rerun migration trên SQL Server, concurrent webhook trên nhiều instance, SMTP thật, Google thật, bank webhook qua HTTPS hoặc giao dịch ngân hàng thật. Không tuyên bố đã hoàn tất triển khai production.
