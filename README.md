# viet-van-backend
Backend API for Viet Van Platform

## SQL Server Somee đã import dữ liệu

Runtime chỉ dùng khóa **`ConnectionStrings:DefaultConnection`** qua `DeploymentConfiguration.SqlConnection()` trong `Program.cs`, được inject vào `VietVanDbContext`. DbContext không có `OnConfiguring` tự trỏ sang database khác; service nghiệp vụ dùng chung DbContext này. Không sửa Entity/DTO/Controller, migration hoặc dữ liệu để đổi host.

### Dán Connection String ở đâu?

Trên máy hiện tại, mở **`appsettings.Local.json`, dòng 3**, thay **toàn bộ giá trị** `ConnectionStrings.DefaultConnection` bằng Connection string chính thức trong Somee: **MS SQL → Databases → VietVan_New → Connection details → Connection string → Copy to clipboard**. Không chỉ thay riêng password nếu các tùy chọn khác trong chuỗi Somee khác mẫu. Xem [hướng dẫn chính thức Somee](https://www.somee.com/doka/Help/Article/103/MS_SQL_Database_connection_string).

Mẫu đã điền theo thông tin được cung cấp, chưa có mật khẩu thật:

```text
Server=VietVan_New.mssql.somee.com;Database=VietVan_New;User Id=zinnkh0401_SQLLogin_1;Password=REPLACE_WITH_SOMEE_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30
```

Đây là **mẫu**, chưa phải chuỗi đã sao chép từ tài khoản Somee. Giá trị chính thức trên dashboard là nguồn xác nhận hostname, login và tùy chọn TLS. Hostname dùng dấu chấm: `VietVan_New.mssql.somee.com`, không có `\` trước `.mssql`. Không dùng `Integrated Security=True` hoặc `Trusted_Connection=True`. Các keyword chính thức như `workstation id`, `packet size`, `user id`, `pwd`, `data source`, `initial catalog` được SqlClient hỗ trợ và không bị thay thế khi runtime đọc chuỗi.

Giữ `Encrypt`/`TrustServerCertificate` theo cấu hình TLS chính thức của Somee; không tự đổi sang tắt mã hóa để bỏ qua lỗi kết nối. JSON yêu cầu escape dấu nháy kép/backslash nếu chúng xuất hiện trong giá trị. Không gửi mật khẩu/chuỗi thật vào chat hoặc commit vào Git.

`appsettings.Local.json` đã bị Git/Docker/publish bỏ qua. Cấu hình database Windows cũ được giữ ở `ConnectionStrings.LocalBackupConnection` trong cùng file; **không tự động sử dụng key backup này** khi Somee lỗi. Database local không bị sửa/xóa. Muốn quay lại local, chủ động sao chép giá trị backup vào `DefaultConnection`, rồi restart. Không thay đổi kết nối khi API đang thực hiện transaction.

Có thể dùng .NET User Secrets thay cho file local, qua Manage User Secrets của IDE. Thứ tự hiện tại: command line → Environment Variables → User Secrets (Development) → appsettings JSON → fallback `appsettings.Local.json` (chỉ Development). Vì vậy biến môi trường/secret `DefaultConnection` đã tồn tại sẽ ưu tiên hơn file local. Không nạp file local trong Production/Render, không tự fallback sang local nếu cloud báo lỗi.

### Render

Tên biến môi trường chính xác:

```text
ConnectionStrings__DefaultConnection
```

Value là toàn bộ chuỗi chính thức Somee trên **một dòng**, không thêm tên biến hoặc cặp dấu nháy bao ngoài như JSON. `.env.example` đã cập nhật mẫu Somee, không chứa mật khẩu thật. Dockerfile, Root Directory, PORT/health/HTTPS và JWT vẫn theo mục triển khai Render bên dưới.

### Retry và bảo vệ database đã import

Thêm `SqlConnectionOpeningRetryInterceptor` cho kết nối SQL do EF tạo: tối đa 3 lần mở kết nối (1 lần đầu + 2 retry), exponential backoff có giới hạn delay 5 giây cho lỗi transient mà SqlClient nhận diện. Provider này chỉ gắn vào **SqlConnection**, không gắn SqlCommand, không replay transaction/thanh toán. Không bật `EnableRetryOnFailure` EF toàn cục vì các service đang dùng nhiều transaction tường minh; xem [SqlClient connection retry](https://learn.microsoft.com/en-us/sql/connect/ado-net/configurable-retry-logic-sqlclient-introduction) và [EF execution strategies/transactions](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency). Timeout mở kết nối áp dụng cho từng lần thử; lệnh kiểm tra chỉ đọc có timeout tổng 45 giây.

Không bật sensitive data logging, không in chuỗi có mật khẩu. Lỗi hết retry vẫn giữ response 503 `DATABASE_UNAVAILABLE` hiện có. Auto-provision DevelopmentAdmin chỉ được phép với SQL local; không chạy trên Somee dù cấu hình DevelopmentAdmin bị bật nhầm. Không thêm EnsureCreated/EnsureDeleted/Migrate, bootstrap hoặc seed khi khởi động. Factory design-time còn connection local **design-only**, không được DI runtime sử dụng; không gọi EF database update/bootstrap trên cloud đã import.

### Chạy và kiểm tra sau khi đã dán chuỗi thật

```powershell
dotnet restore VietVan.API.csproj
dotnet build VietVan.API.csproj

# Chỉ đọc; kết thúc sau khi kiểm tra, không khởi động Web Service/worker.
dotnet run --launch-profile http -- --check-db-readonly

# Khi kết quả kiểm tra đúng database/schema:
dotnet run --launch-profile http
```

Lệnh `--check-db-readonly` chỉ chạy SELECT: tên database/SQL version/compatibility level và các projection `TOP (0)` của LiteraryWorks, Users, UserAuthStates, UserSubscriptions, SubscriptionPackages, DramaProductions, DramaAssets. Lệnh đối chiếu tên cột thực tế với model EF, in riêng `PASS`/`FAIL` cho từng bảng và tên cột còn thiếu; tiếp tục kiểm tra bảng sau khi một bảng lệch schema. Không đọc ra user record/password hash, không trả nội dung tác phẩm, không UPDATE/INSERT/DELETE, không seed/migration và không khởi động PaymentExpiryWorker. Mật khẩu placeholder/Windows Authentication Somee bị chặn trước khi kết nối. Lỗi cấu hình/schema chỉ in bước kiểm tra, tên bảng/cột, loại/mã lỗi phù hợp, không in chuỗi hoặc provider message chứa dữ liệu nhạy cảm. Kiểm tra này xác nhận bảng/cột và projection rỗng; không thay thế kiểm tra kiểu dữ liệu, dữ liệu thực tế, FK/index hoặc API nghiệp vụ.

API giữ địa chỉ `http://localhost:5080`, Swagger `/swagger`, health `/health`. Sau khi kiểm tra SQL thành công, thử `GET /api/works?page=1&pageSize=10`, `GET /api/dramas` và `POST /api/auth/login` bằng tài khoản **đã có trong database import**. Không tạo/reset Admin để vượt lỗi login. Health 200 không chứng minh cloud database đúng schema. Khi chạy Web Service bình thường, PaymentExpiryWorker vẫn thực hiện nghiệp vụ hết hạn đơn hiện có; dùng lệnh kiểm tra riêng ở trên nếu chỉ muốn đọc.

Kết quả lần cấu hình Somee: restore thành công từ cache, build Release 0 warning/0 error; **195/195 test đạt** ở lần cấu hình ban đầu (`somee-configuration.trx`), gồm env ưu tiên JSON, keyword Somee, chặn placeholder/Windows Auth, retry mở kết nối và hồi quy nghiệp vụ. Test project còn NU1900 do feed audit NuGet không truy cập được. Terminal người dùng đã xác nhận kết nối cloud thành công: database `VietVan_New`, SQL Server `15.0.2000.5`, compatibility level `150`; projection LiteraryWorks đạt. Kiểm tra cũ dừng ở nhóm login/subscription với InvalidOperationException; chưa đủ thông tin để chọn script nâng cấp. Đã bổ sung báo riêng từng bảng/cột thiếu; cần chạy lại `--check-db-readonly` và đối chiếu kết quả trước khi thay đổi schema. Chưa xác nhận API đọc/login trên cloud. Không có Docker CLI nên chưa build image Docker; Dockerfile không thay đổi. Publish và 5 kiểm tra HTTP cấu hình Render đạt bằng cấu hình kiểm thử, không dùng database Somee thật.

File thêm/sửa cho Somee: `Program.cs`, `Hosting/DeploymentConfiguration.cs`, `Hosting/DatabaseReadOnlyCheck.cs`, `Data/SqlConnectionOpeningRetryInterceptor.cs`, `Middlewares/ApiExceptionMiddleware.cs`, `.env.example`, `README.md`, `tests/VietVan.Payment.Tests/DeploymentConfigurationTests.cs`, `tests/VietVan.Payment.Tests/SqlConnectionOpeningRetryTests.cs`, `tests/VietVan.Payment.Tests/ApiExceptionMiddlewareTests.cs`; và file riêng `appsettings.Local.json` bị ignore. `appsettings.json`, `appsettings.Development.json`, DbContext, Dockerfile và `.gitignore` đã kiểm tra, không cần sửa thêm. Không sửa FE hoặc tự push GitHub.

Sau khi bổ sung chẩn đoán từng bảng/cột: build qua lệnh test thành công, **198/198 test đạt** (`somee-schema-diagnostics.trx`), gồm trường hợp thiếu FullName, tên Benefit khác Benefits, và cột phụ/khác chữ hoa thường. Các ví dụ này là tình huống kiểm thử, chưa phải kết luận cột nào thiếu trên Somee.

### Kết quả Somee và bản vá SubscriptionPackages

Kết quả mới nhất do người dùng cung cấp ngày 26/11/2026: kết nối Somee database `VietVan_New` thành công, SQL Server `15.0.2000.5`, compatibility level `150`; **cả 7 bảng LiteraryWorks, Users, UserAuthStates, UserSubscriptions, SubscriptionPackages, DramaProductions, DramaAssets đều PASS**. Lệnh kết thúc thành công và không chạy migration, seed hoặc thao tác thay đổi dữ liệu. Chưa xác nhận HTTP API đọc/login trên cloud; projection rỗng không kiểm tra toàn bộ dữ liệu, FK/index hoặc nghiệp vụ. Có thể chạy Backend bằng `dotnet run --launch-profile http`, mở `http://localhost:5080/swagger`, thử danh sách tác phẩm/kịch và đăng nhập bằng tài khoản đã import.

Trước đó, `dbo.SubscriptionPackages` thiếu đúng **Benefits, BillingPeriod, Code, CreatedAt, Description, UpdatedAt**. Các bước bên dưới ghi lại cách xử lý; khi cả 7 bảng đã PASS thì không cần áp dụng lại bản vá.

Đã chuẩn bị **`scripts/Upgrade_Somee_SubscriptionPackages.sql`**, chỉ bổ sung 6 cột trên nếu thiếu. Chạy thủ công trong SSMS bằng kết nối Somee, chọn `VietVan_New`; không chạy `PaymentUpgrade.sql`, baseline hoặc EF database update thay cho bản vá này.

1. Sao lưu/export database Somee trước khi áp dụng và dừng Backend trong lúc nâng cấp.
2. Mở script, chạy toàn bộ với mặc định `@ApplyChanges = 0` để xem trước. Cột thiếu phải báo `WILL ADD`; cột đã đúng báo `ALREADY MATCHES`. Nếu có `INCOMPATIBLE - STOP`, dừng để đối chiếu, không sửa/xóa cột cũ.
3. Sau khi đối chiếu kết quả, đổi dòng `DECLARE @ApplyChanges bit = 0;` thành `DECLARE @ApplyChanges bit = 1;`, chạy lại toàn bộ. Script dùng transaction, rollback nếu lỗi; các cột đã đúng được bỏ qua khi chạy lại.
4. Chạy `dotnet run --launch-profile http -- --check-db-readonly` một lần nữa. Khi schema đạt, chạy Backend và kiểm tra các API như hướng dẫn trên.

Không seed gói, không thay đổi ID/tên/giá/tier/thời hạn hoặc subscription hiện có. `Benefits` và `Description` mới dùng chuỗi rỗng; `Code`, `BillingPeriod`, `UpdatedAt` mới dùng NULL; `CreatedAt` mới dùng thời điểm nâng cấp UTC, **không phải ngày tạo gói lịch sử**. Cột legacy như `Benefit` nếu có vẫn được giữ nguyên; không tự đoán/sao chép nội dung. BillingPeriod NULL dùng nhánh DurationInDays hiện có của SubscriptionRules; không tự gán Monthly/Yearly từ số ngày. Bản vá không tạo/sửa index hay bảng khác.

Đã kiểm tra cú pháp bằng `TSql150Parser` (SQL Server 2019) cho batch chính và 6 câu ALTER động: **7 batch, 0 lỗi cú pháp**. Agent không trực tiếp chạy ALTER trên Somee hoặc database thử; kết quả schema sau xử lý được xác nhận qua output kiểm tra chỉ đọc do người dùng cung cấp như trên. Backend không gọi script này khi khởi động.

## Deploy Render Free bằng Docker

Cập nhật 26/11/2026, repository `VietVan-EXE101/viet-van-backend`, branch `main`. Dockerfile nằm cùng cấp `VietVan.API.csproj` và `Program.cs`. Không cần solution để build: API target `net8.0`, sử dụng EF Core SQL Server. Hai project phụ nằm trong `tests/`. Không tìm thấy `AGENTS.md` hoặc solution trong lần rà soát này. Trước khi sửa, working tree sạch và `main` theo dõi `origin/main`.

### 1. Những gì bản chuẩn bị triển khai đã làm

- Docker multi-stage: `mcr.microsoft.com/dotnet/sdk:8.0` build/publish, `mcr.microsoft.com/dotnet/aspnet:8.0` chạy API bằng user `app`, không chạy root.
- Nếu có `PORT`, API bind `http://0.0.0.0:$PORT`; Render không cung cấp giá trị thì dùng 10000. Local không có `PORT`/`RENDER` giữ launch profile `http://localhost:5080`.
- Khi Render đặt `RENDER=true`, nhận `X-Forwarded-Proto` và `X-Forwarded-For` từ ingress trước khi xử lý HTTPS, cookie và authentication. Chỉ tin một hop gần nhất, không nhận forwarded Host. Chính sách này dành cho container chỉ được truy cập qua ingress Render; không bật `RENDER=true` trên server có cổng origin mở trực tiếp ra Internet. Render ghi log console.
- `GET /health` không cần JWT, trả `200 {"status":"ok"}` trên cổng HTTP nội bộ. Đây là liveness, **không chứng minh SQL Server/email/storage hoạt động**. Không chạy migration, seed hoặc truy vấn database khi health check.
- Swagger bật trong Development hoặc khi `Swagger__Enabled=true`. Docker mặc định bật để demo đồ án. Tắt bằng `Swagger__Enabled=false` khi cần.
- Connection string lấy từ cấu hình chuẩn ASP.NET Core, gồm `ConnectionStrings__DefaultConnection`. Production từ chối localhost, named instance và Windows Authentication; giữ nguyên provider SQL Server.
- Thêm transport email Resend qua HTTPS, vẫn giữ SMTP cho local. API, nội dung/token xác minh và reset password không thay đổi.
- Google hỗ trợ cấu hình SameSite cho cookie challenge; giữ chữ ký token, audience, issuer, expiration, email đã xác minh và nonce một lần.

Tham khảo [Docker trên Render](https://render.com/docs/docker), [bind cổng Web Service](https://render.com/docs/web-services), [Forwarded Headers ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).

### 2. Environment Variables trên Render

Mở `.env.example` để xem tên biến và giá trị mẫu. **Không dùng nguyên placeholder để deploy.** Nhập giá trị thật trong Render → Environment; không commit `.env` hay secrets vào Git. ASP.NET Core không tự đọc file `.env`; Render nhập các biến từ dashboard, Docker local dùng `--env-file` nếu cần. Xem [cấu hình Environment Variables của Render](https://render.com/docs/configure-environment-variables).

| Biến | Giá trị/cách nhập | Khi nào cần |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Docker đã đặt; giữ nguyên trên Render |
| `PORT` | `10000`, hoặc cổng Render cung cấp | Render/Docker đã cung cấp |
| `ConnectionStrings__DefaultConnection` | SQL Server từ xa, SQL username/password, TLS | Bắt buộc để khởi động Production |
| `Jwt__Key` | Secret ngẫu nhiên riêng, ít nhất 32 ký tự | Bắt buộc để khởi động; không dùng `REPLACE`/`CHANGE_ME` |
| `Jwt__Issuer` | `VietVan.API` | Mặc định hiện tại; phải khớp khi phát và kiểm tra JWT |
| `Jwt__Audience` | `VietVan.Client` | Mặc định hiện tại |
| `Jwt__ExpiryMinutes` | `120` | Tùy chọn |
| `Swagger__Enabled` | `true` | Docker đã bật cho demo |
| `AllowedHosts` | Ví dụ `your-service.onrender.com;localhost` | Khuyến nghị thay bằng host thực; không thêm `https://` |
| `Cors__AllowedOrigins__0` | Origin FE thực, ví dụ `https://frontend.example.com` | FE gọi API từ origin khác |
| `Cors__AllowedOrigins__1` | Origin bổ sung nếu cần, ví dụ `http://localhost:3001` | Tùy chọn; đánh số liên tiếp, không thêm `/register` hoặc dấu `/` cuối |
| `GoogleSignIn__ClientId` | Web Client ID đang dùng trên FE | Google Login |
| `GoogleSignIn__ChallengeCookieSameSite` | `None` nếu FE/API khác site trên HTTPS; `Strict` nếu cùng site | Google Login; mặc định `Strict` để giữ local hiện có |
| `Email__Provider` | `Resend` | Docker/Render mặc định chọn Resend |
| `Email__Resend__ApiKey` | API key của tài khoản Resend | Gửi xác minh email/reset password |
| `Email__Resend__FromAddress` | Địa chỉ gửi thuộc domain đã xác minh | Gửi cho người dùng thực |
| `Email__VerificationBaseUrl` | `https://your-service.onrender.com` | Sinh link tới `/verify-email.html#token` sẵn có ở BE |
| `Email__PasswordResetUrl` | URL trang reset HTTPS của FE, hoặc để trống | Trống vẫn gửi mã reset qua email |
| `SePay__BankId` | Mã ngân hàng dùng tạo VietQR | Tạo đơn thanh toán |
| `SePay__BankName` | Tên ngân hàng | Thông tin thanh toán |
| `SePay__AccountNumber` | Số tài khoản nhận | Tạo đơn/đối chiếu webhook |
| `SePay__AccountHolder` | Chủ tài khoản nhận | Thông tin thanh toán |
| `SePay__WebhookApiKey` | Secret riêng ít nhất 24 ký tự, khớp cấu hình SePay | Nhận webhook; đây không phải JWT hoặc API key gửi yêu cầu ra SePay |
| `SePay__OrderLifetimeMinutes` | `15` | Tùy chọn |
| `Storage__RootPath` | `/app/PrivateMedia` | Docker local/Render demo tạm; **không phải storage bền vững** |
| `DevelopmentAdmin__Enabled` | `false` | Giữ tắt; không tạo/reset Admin khi Production khởi động |

Render tự đặt `RENDER=true`; không cần nhập thủ công. Không đặt `ASPNETCORE_FORWARDEDHEADERS_ENABLED` thêm vì source đã cấu hình riêng cho Render. Không cần `Google Client Secret` cho luồng Google ID Token hiện tại. Tạo JWT key và webhook key độc lập, giữ JWT key ổn định giữa các lần deploy để token hiện tại tiếp tục có hiệu lực theo logic thu hồi hiện có.

### 3. Database SQL Server

Render Free Web Service **không chứa SQL Server của máy Windows**. `localhost` trong container là chính container, không phải máy đang chạy SQLEXPRESS. Chuẩn bị một SQL Server từ xa truy cập được qua TCP; kiểm tra DNS, cổng, TLS và firewall cho outbound IP của service Render.

Connection string mẫu, chỉ thay trong Environment:

```text
Server=tcp:sql.example.com,1433;Database=VietVan;User Id=YOUR_SQL_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30
```

Dùng TCP host/port cụ thể và SQL Authentication. Không dùng `Trusted_Connection=True`, `Integrated Security=True`, `(localdb)` hoặc `localhost\SQLEXPRESS`. Không công khai database cho mọi IP nếu có thể giới hạn firewall. Tài khoản ứng dụng cần các quyền nghiệp vụ cần thiết; dùng tài khoản vận hành riêng để thay đổi schema.

Database cloud hiện được chọn là **Somee SQL Server 2019 Express**, đã import dữ liệu. Dùng chuỗi chính thức trong mục Somee ở đầu tài liệu cho cả local runtime và Render. Giữ nguyên database và provider SQL Server.

Với database hiện có:

1. Giữ bản dự phòng SQL Server local và media. Database Somee đã import không cần import/restore hoặc tạo lại; bắt đầu bằng lệnh kiểm tra chỉ đọc ở mục Somee.
2. Đối chiếu schema/migration history bằng các script chẩn đoán trong `scripts/`, đọc [README_DRAMA.md](README_DRAMA.md) và [DATABASE_COMPATIBILITY_REPORT.md](DATABASE_COMPATIBILITY_REPORT.md). Không chọn nhầm database đang có dữ liệu để chạy baseline.
3. Các migration lịch sử và project bootstrap có guard/phụ thuộc schema cũ. **Không chạy mù `dotnet ef database update`, không chạy bootstrap local trên Render và không sửa history để vượt guard.** Chuẩn bị script nâng cấp đã review đúng schema của database đích; chạy ngoài bước khởi động Web Service sau khi có backup.
4. `VIETVAN_MIGRATION_CONNECTION` chỉ phục vụ design-time tooling của API khi người vận hành dùng EF. Connection runtime vẫn là `ConnectionStrings__DefaultConnection`. Không tự chạy migration trong Dockerfile/entrypoint hoặc trong `/health`.
5. Sau deploy, test login, danh sách kịch, draft/manifest và tạo đơn bằng database demo. `/health` 200 riêng lẻ không đủ xác nhận database đúng schema.

Bản sửa này không kết nối, tạo, xóa hoặc migrate database thật.

### 4. Xác minh email và quên mật khẩu

Render Free chặn outbound SMTP 25/465/587. Bản Docker dùng `Email__Provider=Resend`, gửi `POST https://api.resend.com/emails` qua HTTPS, dùng interface `IEmailSender` hiện có. Xem [giới hạn SMTP của Render](https://render.com/articles/can-i-send-email-over-smtp-from-render) và [API gửi email Resend](https://resend.com/docs/api-reference/emails/send-email).

Tự cấu hình tài khoản Resend theo hạn mức miễn phí hiện hành, API key và domain gửi đã xác minh. Không có tài khoản/domain/API key thật nào được tạo hoặc mua tự động. `onboarding@resend.dev` chỉ phù hợp kiểm thử tới địa chỉ của chính chủ tài khoản; gửi tới Gmail người dùng khác cần domain đã xác minh theo [hướng dẫn Resend](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain). Nếu chưa có domain, chưa thể khẳng định gửi cho mọi người dùng bằng Resend miễn phí.

Đặt `Email__VerificationBaseUrl` là URL HTTPS của BE vì trang `/verify-email.html` đã có sẵn trong source. Không sửa trang FE. Đăng ký vẫn trả trạng thái `verificationEmailSent` hiện có; provider từ chối thì không báo gửi thành công. Forgot-password vẫn trả thông báo chung tránh lộ tài khoản; 200 không có nghĩa thư đã tới Gmail. Token, expiry, nonce, password rules và cơ chế thu hồi JWT giữ nguyên. Kiểm tra Inbox/Spam và log trạng thái chấp nhận/lỗi; không ghi token/email body vào log.

Local vẫn mặc định SMTP với `Email:Smtp` trong User Secrets. Các biến SMTP tùy chọn đã có chú thích ở `.env.example`; không đưa Gmail App Password vào Git. Lệnh `--test-email` hiện có vẫn là bài kiểm tra SMTP dành cho Development, không phải bài test Resend.

### 5. Google Login, CORS và HTTPS

1. Trong Google Cloud chọn Web OAuth client VietVan, đặt Authorized JavaScript origins bằng **origin FE thực**. Nếu vẫn thử FE local, thêm `http://localhost:3001`. Client ID FE và `GoogleSignIn__ClientId` phải giống nhau; không đưa Client ID vào trường `idToken`.
2. Luồng đang dùng Google Identity Services popup/callback và API ID Token, không có endpoint redirect `/signin-google`. Không thêm `/register` như một origin hoặc tạo redirect URI cho API không tồn tại. Xem [cấu hình client Google](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid).
3. Cấu hình CORS đúng origin, không dùng wildcard với credentials. FE cần `credentials: "include"` trên cả `POST /api/auth/google/challenge` và `POST /api/auth/google`; dùng `clientId`/`nonce` từ challenge khi khởi tạo SDK và gửi `response.credential` làm `idToken`.
4. Nếu FE/API khác site, chọn `GoogleSignIn__ChallengeCookieSameSite=None`; API phải được gọi qua HTTPS. Forwarded Headers chạy trước controller để cookie có `Secure`/`HttpOnly`. HTTP localhost cùng site giữ `Strict` mặc định. Trình duyệt vẫn có thể chặn third-party cookies; khi đó dùng deployment cùng site/custom domain hoặc reverse proxy cùng origin, không bỏ nonce. Đây là hướng dẫn tích hợp, không sửa FE trong bản chuẩn bị này.
5. Challenge giữ 5 phút, dùng một lần, hiện lưu trong bộ nhớ một instance. Khi Render sleep/restart/deploy, tạo challenge mới. Bản Docker không chia sẻ challenge giữa nhiều instance. Xác minh ID Token và ký JWT Việt Văn vẫn dùng logic hiện tại.

### 6. SePay webhook

- URL nhận là `https://your-service.onrender.com/api/payments/webhooks/sepay`, phương thức POST JSON.
- Chọn xác thực API Key trên SePay để gửi header `Authorization: Apikey <secret>` và đặt cùng secret ở `SePay__WebhookApiKey`. Không gửi Bearer JWT của người mua cho webhook.
- Cấu hình đúng ngân hàng, số tài khoản, nội dung/chuyển khoản theo order code mà API trả về. Giữ nguyên xử lý idempotency, số tiền, tài khoản nhận và giao dịch cần review; không kích hoạt gói dựa trên FE báo đã thanh toán.
- Webhook hợp lệ đã lưu trả 201 với `success: true`; sai key trả 401, chưa cấu hình key trả 503. Giữ HTTPS URL và kiểm tra delivery log/retry của SePay, đặc biệt sau thời gian Render Free ngủ. Không chuyển tiền thật trong smoke test.

Tham khảo [hướng dẫn webhook SePay](https://docs.sepay.vn/tich-hop-webhooks.html) và [README_PAYMENT.md](README_PAYMENT.md).

### 7. Media: giới hạn hiện tại và phương án bền vững

**Storage hiện tại vẫn là local filesystem, chưa tích hợp object storage.** Render Free mất file ghi lúc runtime khi restart/redeploy/sleep và không hỗ trợ persistent disk miễn phí. Không dùng file local trên Render làm bản duy nhất. Xem [giới hạn Render Free](https://render.com/docs/free).

Hai nơi cần lưu bền vững là `PrivateMedia/drama-assets/` (video/audio/ảnh/GIF/asset tương tác, được API kiểm tra quyền) và `wwwroot/uploads/work-covers/` (cover tác phẩm). Chúng đã được loại khỏi Docker context/publish output; **file gốc trong workspace và metadata database không bị xóa hoặc đổi đường dẫn**. Lần rà soát ghi nhận 95 file private khoảng 291 MB và 6 cover khoảng 5,9 MB. Các asset đó không tự xuất hiện trong container mới.

Phương án đề xuất cho demo có hạn mức miễn phí: bucket private trên [Cloudflare R2 Standard](https://developers.cloudflare.com/r2/pricing/) hoặc một S3-compatible storage đã có. R2 hiện có quota Standard miễn phí nhưng có thể tính phí khi vượt hạn mức; tự xem điều kiện tài khoản/billing trước khi đăng ký. Không tự mở dịch vụ hoặc bật thanh toán.

Để triển khai storage mà vẫn giữ API contract, bước tiếp theo cần làm trước khi coi upload trên Render là bền vững:

1. Sao lưu database và cả hai thư mục media; kiểm tra số lượng, tổng byte và SHA-256 trước/sau copy. Dùng copy, không move/xóa file gốc.
2. Copy lên bucket bằng key giữ nguyên `RelativePath` của `DramaAssets`; chuẩn bị mapping cover riêng. Không thay URL metadata hàng loạt theo phỏng đoán.
3. Thêm adapter storage cho upload/read/delete bất đồng bộ, hỗ trợ streaming/range. API media vẫn kiểm tra JWT/PRO/Admin trước khi lấy object; bucket không public, credential chỉ ở BE. Giữ nguyên byte, MIME và GIF động.
4. Hiện `IFileStorageService.ExistingPath()`/`PhysicalFile()` và `WorkCoverStorage` chỉ hiểu đường dẫn local. **Chỉ nhập bucket URL vào `Storage__RootPath` hoặc chỉ copy file lên R2 sẽ chưa làm API đọc được object storage.** Cần adapter và test trước khi chuyển lưu trữ.
5. Sau khi test upload/tải lại và khôi phục qua redeploy thành công, mới chuyển nguồn đọc sang object storage. Nếu chưa tích hợp, chỉ dùng upload Render như demo tạm và giữ một bản sao ngoài Render.

Hạn mức file hiện giữ nguyên: ảnh/asset GIF 10 MiB, cover 5 MiB, audio 30 MiB, video 200 MiB; toàn request khoảng 202 MiB. Render Free có hạn mức tài nguyên/băng thông và cold start, cần test với media demo nhỏ trước. Xem thêm [MEDIA_STORAGE_OPERATIONS_VI.md](MEDIA_STORAGE_OPERATIONS_VI.md).

### 8. Push GitHub thủ công

Chạy tại thư mục gốc repo, kiểm tra remote đúng `https://github.com/VietVan-EXE101/viet-van-backend.git` và branch `main`:

```powershell
git status --short --branch
git diff --check
git diff
git add -- Dockerfile .dockerignore .env.example .gitignore README.md Program.cs VietVan.API.csproj Hosting appsettings.json appsettings.Development.json
git add -- Controllers/GoogleAuthController.cs Services/Authentication/GoogleIdTokenValidator.cs Services/Authentication/ResendEmailSender.cs scripts/Test-RenderHosting.ps1
git add -- tests/VietVan.Payment.Tests/DeploymentConfigurationTests.cs tests/VietVan.Payment.Tests/ResendEmailSenderTests.cs tests/VietVan.Payment.Tests/GoogleLoginHttpTests.cs
git diff --cached --stat
git commit -m "Prepare .NET 8 Docker deployment for Render Free"
git push origin main
```

Review nội dung đã stage trước commit. `.env` và `appsettings.Local.json` được Git bỏ qua; `.env.example` được phép theo dõi. Không dùng force push. Không có commit/push tự động trong lần chỉnh source này.

### 9. Tạo Render Web Service

| Mục | Giá trị |
|---|---|
| Repository | `VietVan-EXE101/viet-van-backend` |
| Branch | `main` |
| Language/Runtime | **Docker** |
| Root Directory | **Để trống** vì project ở gốc repo; nếu UI yêu cầu đường dẫn, dùng `.` |
| Dockerfile Path | **`./Dockerfile`** |
| Docker Build Context | **`.`** |
| Instance Type | **Free** |
| Docker Command / Start Command | Để trống, dùng ENTRYPOINT trong Dockerfile |
| Build Command | Không đặt lệnh build native; Dockerfile thực hiện restore/publish |
| Health Check Path | **`/health`** |

Kết nối GitHub, nhập các biến runtime trước deploy; không nhập password/secret vào Docker build arguments. Có thể để Manual Deploy khi demo để chủ động kiểm tra. Sau deploy, cập nhật host thật ở `AllowedHosts`, URL email, CORS, Google Console và SePay. Kiểm tra `/health`, `/swagger`, đăng nhập và luồng nghiệp vụ với database demo. Render Free có thể mất khoảng một phút để khởi động sau khi ngủ; không hứa API luôn sẵn sàng cho callback tức thời. Không đặt pre-deploy migration tự động.

### 10. Local và các lệnh kiểm tra

Thông tin connection local trước đây ở JSON được giữ trong file **`appsettings.Local.json` bị ignore**, chỉ dùng làm fallback trong Development khi environment/User Secrets không có connection. File này không vào Git, publish hoặc Docker. Nếu clone repo mới, tự đặt `ConnectionStrings:DefaultConnection` bằng User Secrets hoặc biến môi trường và cấu hình `Jwt:Key`; không có password database nào trong repo.

```powershell
# Chạy local theo profile hiện có; bỏ PORT/RENDER nếu từng tự đặt trong shell.
dotnet run --launch-profile http

# Build/publish không chạm database.
dotnet restore VietVan.API.csproj
dotnet build VietVan.API.csproj -c Release --no-restore -p:UseAppHost=false
dotnet publish VietVan.API.csproj -c Release --no-restore --no-build -p:UseAppHost=false -o artifacts/render-publish
dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj -c Release

# Windows: smoke test bản publish bằng secret ngẫu nhiên và SQL hostname .invalid.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-RenderHosting.ps1

# Khi đã có Docker Engine và bật Linux containers:
docker build -t vietvan-api:render .
# File .env thật phải được điền đầy đủ, không commit.
docker run --rm --env-file .env -p 10000:10000 vietvan-api:render
```

Kết quả trên máy hiện tại:

- Restore API thành công từ cache với `--ignore-failed-sources -p:NuGetAudit=false`; chưa kiểm tra vulnerability feed trực tuyến. Test project vẫn có cảnh báo NU1900 vì không truy cập được feed audit NuGet.
- Build Release thành công, 0 warning/0 error; publish Release thành công.
- **182/182 test đạt**; kết quả ở `tests/VietVan.Payment.Tests/TestResults/render-deployment.trx`. Gồm hồi quy nghiệp vụ, cấu hình PORT/SQL, HTTPS email giả lập và cookie Google qua reverse proxy.
- Smoke test published Production trong sandbox đạt: HTTP `/health`, Swagger, credentialed CORS, HTTPS Google challenge cookie, từ chối Client ID làm token, từ chối SePay key sai. Không dùng database thật, không gửi email hay xử lý tiền. Script dừng đúng process do nó tạo, không dừng Backend khác.
- Máy có runtime .NET 8 nhưng SDK đang dùng là 11 preview; chưa có SDK 8 cài local. Dockerfile cố định SDK/runtime 8.0 nhưng **chưa build image được vì không có Docker CLI/Engine**. Cần chạy Docker build trên máy có Docker hoặc xem build log Render sau khi bạn push.
- Chưa deploy lên tài khoản Render, chưa kiểm tra SQL Server remote/schema thực, gửi Resend/Gmail thật, Google Console/login thật hoặc SePay delivery thật. Chưa tích hợp object storage; upload local trên Render vẫn tạm thời. Không tự đăng ký dịch vụ trả phí, push code, sửa FE, xóa migration/database hay media.

Các file được thêm/sửa trong lần chuẩn bị này:

- Deploy: `Dockerfile`, `.dockerignore`, `.env.example`, `.gitignore`.
- Runtime: `Program.cs`, `Hosting/DeploymentConfiguration.cs`, `VietVan.API.csproj`, `appsettings.json`, `appsettings.Development.json`.
- Google/email: `Controllers/GoogleAuthController.cs`, `Services/Authentication/GoogleIdTokenValidator.cs`, `Services/Authentication/ResendEmailSender.cs`.
- Kiểm tra: `scripts/Test-RenderHosting.ps1`, `tests/VietVan.Payment.Tests/DeploymentConfigurationTests.cs`, `tests/VietVan.Payment.Tests/ResendEmailSenderTests.cs`, `tests/VietVan.Payment.Tests/GoogleLoginHttpTests.cs`.
- Hướng dẫn: `README.md`. Ngoài Git có `appsettings.Local.json` giữ cấu hình local, không phải file để push.
