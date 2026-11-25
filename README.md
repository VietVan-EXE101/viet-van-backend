# viet-van-backend
Backend API for Viet Van Platform

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

Ưu tiên SQL Server có sẵn do nhóm/trường cung cấp nếu cần demo miễn phí. Có thể **tự đánh giá** [Azure SQL Database free tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier?view=azuresql): cùng họ SQL Database Engine, có điều kiện subscription/hạn mức và giới hạn đối với database hiện hữu. Nguồn hiện tại yêu cầu bắt đầu bằng database development/sample mới; không giả định restore/chuyển database cũ vào free tier được hỗ trợ. Chỉ chọn nếu phù hợp, đặt chế độ dừng khi hết quota; không tự bật trả phí hoặc tạo resource trong lượt chuẩn bị source này. Không đổi ứng dụng sang PostgreSQL.

Với database hiện có:

1. Sao lưu SQL Server và media nhất quán; khôi phục/kiểm tra trên môi trường riêng trước khi cấu hình host mới.
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
