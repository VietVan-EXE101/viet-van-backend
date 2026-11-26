# viet-van-backend
Backend API for Viet Van Platform

## Cloudflare R2 private storage

Tích hợp .NET 8, `AWSSDK.S3` 4.0.104.12 và `AWSSDK.Core` 4.0.103 được cố định phiên bản để tránh dependency không hỗ trợ .NET 8, bucket private `vietvan-media`. Xem dependency chính thức [S3 4.0.104.12](https://www.nuget.org/packages/AWSSDK.S3/4.0.104.12) và [Core 4.0.103](https://www.nuget.org/packages/AWSSDK.Core/4.0.103). Không sửa FE, auth/Google/SePay hoặc Connection String. Bộ khóa xuất hiện trong ảnh cần được thu hồi và thay bằng khóa mới; không dùng hoặc đưa chúng vào source, log, README hay lệnh shell có chứa secret.

### Cấu hình local và Render

Local ngoài Docker vẫn mặc định `Storage:Provider=Local` nếu chưa cấu hình. Docker đặt `Storage__Provider=R2`; trên Render (`RENDER=true`), thiếu biến provider cũng chọn R2, còn cấu hình `Local` bị từ chối khi khởi động. R2 lỗi không fallback Local. Giữ 5 tên biến R2 dưới đây; khuyến nghị khai báo rõ `Storage__Provider=R2` trên Render → Environment rồi Save and deploy:

| Biến | Giá trị |
|---|---|
| `Storage__Provider` | `R2` |
| `R2__AccountId` | Cloudflare Account ID |
| `R2__AccessKeyId` | Access Key ID mới, quyền Object Read & Write cho riêng bucket |
| `R2__SecretAccessKey` | Secret Access Key mới |
| `R2__BucketName` | `vietvan-media` |
| `R2__ServiceUrl` | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` |
| `R2__PublicUrlMinutes` | `60`, tùy chọn 1–120 |
| `R2__ProtectedUrlMinutes` | `5`, tùy chọn 1–15 |

Không nhập token Cloudflare quản trị vào SecretAccessKey. R2 dùng cặp S3 credentials. Endpoint chỉ chấp nhận HTTPS của đúng Account ID, không kèm tên bucket hoặc đường dẫn. Giữ nguyên `ConnectionStrings__DefaultConnection` Somee hiện có. `Storage__RootPath` vẫn chỉ đường dẫn local, không phải R2 endpoint.

Nếu media mất sau redeploy, xem [chẩn đoán và kiểm thử redeploy](MEDIA_STORAGE_OPERATIONS_VI.md). Lệnh `dotnet run --launch-profile http -- --audit-media-storage-readonly` chỉ SELECT liên kết/revision và HEAD các key trong bucket, không upload lại media, không sửa database. Log startup phải có `Provider=R2 BucketName=vietvan-media`.

Local dùng User Secrets hoặc chèn hai section **ở cấp gốc** vào file `appsettings.Local.json` đã bị ignore; giữ nguyên các section/Connection String đang có:

```json
"Storage": { "Provider": "R2", "RootPath": "PrivateMedia" },
"R2": {
  "AccountId": "REPLACE_ACCOUNT_ID",
  "AccessKeyId": "REPLACE_NEW_ACCESS_KEY_ID",
  "SecretAccessKey": "REPLACE_NEW_SECRET_ACCESS_KEY",
  "BucketName": "vietvan-media",
  "ServiceUrl": "https://REPLACE_ACCOUNT_ID.r2.cloudflarestorage.com",
  "PublicUrlMinutes": 60,
  "ProtectedUrlMinutes": 5
}
```

Đây là phần chèn vào JSON hiện có, không phải nội dung để ghi đè cả file. Với User Secrets dùng tên key dạng `R2:AccountId`, `R2:AccessKeyId`, `R2:SecretAccessKey`, `R2:BucketName`, `R2:ServiceUrl`, `Storage:Provider`. Cấu hình có hiệu lực của ASP.NET Core (CLI/env/User Secrets/JSON chuẩn) ưu tiên hơn file Local fallback. File Local chỉ được nạp khi Development và không phải Render; Production dùng env. Không in file cấu hình thật để chẩn đoán.

S3 client sử dụng `ServiceURL`, `AuthenticationRegion="auto"`, `ForcePathStyle=true`. Theo hướng dẫn R2 .NET, PUT dùng `DisablePayloadSigning=true`, `DisableDefaultChecksumValidation=true`, `UseChunkEncoding=false`; checksum policy của SDK chỉ bật khi bắt buộc để tránh streaming trailers R2 không hỗ trợ. Request vẫn được SDK ký SigV4, chỉ gửi qua HTTPS; file migration được kiểm chứng SHA-256 bằng đọc lại byte trên R2. Không tự triển khai thuật toán ký. Xem [R2 .NET](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/) và [S3 compatibility](https://developers.cloudflare.com/r2/api/s3/api/).

### Upload, đọc và thay thế media

Các POST multipart hiện có giữ nguyên: `kind/title/file` hoặc `assetType/files` cho thư viện asset; `file` cho video/bìa; `coverImage` khi tạo tác phẩm/kịch. Ảnh asset 10 MiB, audio 30 MiB, video 200 MiB, bìa/avatar 5 MiB. Giữ kiểm tra đuôi, MIME, signature và byte count; GIF asset vẫn là `image/gif`, giữ nguyên toàn bộ frame/timing/loop. Avatar và bìa chỉ JPG/JPEG, PNG, WebP như phạm vi hiện có. File staging có tên GUID trong temp, đọc/ghi theo buffer 80 KiB và dọn khi thành công hoặc lỗi; không giữ media lâu dài trên Render.

Key mới được tạo tự động theo `free/videos/`, `free/thumbnails/`, `pro/characters/`, `pro/backgrounds/`, `pro/audio/`, `pro/assets/`, `users/avatars/`; có ID/GUID tránh trùng. Prefix không quyết định quyền: Backend vẫn kiểm tra trạng thái kịch, JWT và subscription từ DB. Bucket giữ private; không cần tạo folder thủ công hoặc bật public access.

| API | Chức năng |
|---|---|
| `POST /api/admin/dramas/{id}/assets` | Upload asset đơn/nhiều file, giữ multipart hiện tại |
| `POST /api/admin/dramas/{id}/video` | Upload/thay video FREE bằng ID asset mới như luồng hiện có |
| `POST /api/admin/dramas/{id}/cover` | Upload/thay bìa kịch, giữ snapshot cũ |
| `PUT /api/admin/dramas/{id}/assets/{assetId}/file` | Thay file giữ ID của asset có thể chỉnh sửa; field `file`; từ chối asset trong snapshot đã xuất bản |
| `DELETE /api/admin/dramas/{id}/assets/{assetId}` | Xóa asset theo guard tham chiếu hiện có |
| `GET /api/dramas/{id}/video` | Local trả file/range; R2 trả 302 tới presigned GET sau kiểm tra quyền |
| `GET /api/dramas/{id}/video/access` | JSON URL video FREE và hạn dùng |
| `GET /api/dramas/{id}/assets/{assetId}/access` | JSON URL asset, kiểm tra quyền giống `/media` |
| `GET /api/dramas/{id}/assets/{assetId}/media` | Giữ URL API cũ; Local trả file, R2 redirect |
| `GET /api/admin/dramas/{id}/assets/{assetId}/access` | Admin lấy JSON URL, kể cả draft |
| `GET /api/admin/dramas/{id}/assets/{assetId}/media` | Admin tải file/redirect |
| `POST /api/admin/works` | Tạo tác phẩm với `coverImage`, giữ contract hiện có |
| `POST /api/admin/works/{id}/cover` | Thay bìa tác phẩm, field `file` |
| `DELETE /api/admin/works/{id}/cover` | Gỡ bìa và dọn file riêng khi không còn tham chiếu |
| `GET /api/media/work-covers/{fileName}` | URL bìa ổn định trong metadata, redirect R2 cho tác phẩm active |
| `POST /api/users/me/avatar` | Upload/thay avatar của chính JWT user, field `file` |
| `GET /api/users/me/avatar` | JSON URL avatar của chính user |
| `GET /api/users/me/avatar/media` | File/redirect avatar của chính user |
| `DELETE /api/users/me/avatar` | Xóa avatar của chính user |

Không lưu URL đã ký vào DB. DramaAssets.RelativePath giữ object key, các trường filename/MIME/size hiện có giữ nguyên. Bìa tác phẩm lưu URL API ổn định, suy ra key server-side. Avatar dùng metadata `UserAvatars` (UserId, ObjectKey, FileName, ContentType, FileSize, UpdatedAt), không thêm cột vào Users nên không làm truy vấn login phụ thuộc cột mới. Chỉ để bật avatar: sao lưu, xem trước rồi áp dụng `scripts/Upgrade_User_Avatars.sql` với `@ApplyChanges=1`; không gọi script khi startup. Thiếu bảng chỉ làm API avatar báo schema 503. Các API kịch khác không cần bảng avatar.

### FE nhận presigned URL

Ví dụ JSON từ `/access`:

```json
{ "url": "https://<R2_ENDPOINT>/<BUCKET>/<KEY>?<SIGNATURE>",
  "expiresAt": "2026-11-26T12:05:00+00:00", "contentType": "image/gif", "sizeBytes": 12345 }
```

FE gọi API Backend bằng JWT rồi gán `url` cho img/video/audio. GET R2 dùng URL nguyên vẹn, không thêm Bearer JWT, không gửi cookie Google và không bỏ/đổi query signature. Ví dụ tích hợp (hướng dẫn, không sửa source FE):

```javascript
const res = await fetch(`${API_BASE}/api/dramas/22/video/access`, {
  headers: { Authorization: `Bearer ${token}` }
});
if (!res.ok) throw new Error(`Media access: ${res.status}`);
const media = await res.json();
video.src = media.url;
// Cấp lại URL trước expiresAt, giữ currentTime/play state khi đổi nguồn.
```

API `/media` và `/video` cũ trả 302 khi R2 hoạt động; client theo redirect tự động hoặc dùng `/access` nếu cần URL/hạn dùng rõ ràng. Backend không proxy byte video/audio. HTTP Range do R2 xử lý trên presigned GET. URL PRO mặc định 5 phút và bị giới hạn tới thời điểm hết quyền subscription; FE nên xin lại URL trước hạn khoảng 30–60 giây. Backend kiểm tra quyền lại ở mỗi lần cấp; URL đã cấp vẫn dùng được tới hạn ngay cả khi tài khoản bị khóa/thu hồi gói giữa chừng. Muốn thu hồi tức thời mọi request cần cơ chế proxy khác; không tuyên bố signed URL có khả năng thu hồi tức thời. URL Admin draft cũng dùng hạn ngắn khi lấy qua luồng asset PRO.

### CORS bucket private

R2 → bucket vietvan-media → Settings → CORS Policy, nhập nội dung `configuration/R2_CORS.example.json`. Thêm đúng origin HTTPS của FE đã deploy vào AllowedOrigins; file mẫu có localhost:3001 và Backend Render để Swagger/trang demo gọi được. CORS chỉ cho GET/HEAD và các header Range cần thiết; không bật public bucket. Xem [R2 CORS](https://developers.cloudflare.com/r2/buckets/cors/) và [presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/). Đừng gọi HEAD lên URL đã ký cho GET: phương thức HTTP nằm trong chữ ký.

### Chuyển media cũ, giữ nguyên ID

Trên máy đang còn PrivateMedia và wwwroot/uploads/work-covers, cấu hình Somee hiện có và R2 bằng khóa mới. Sao lưu DB trước apply và dừng các thao tác chỉnh sửa/upload từ Admin trong lúc chuyển. Không import lại DB hoặc chạy baseline.

```powershell
dotnet restore VietVan.API.csproj -p:NuGetAudit=false
dotnet build VietVan.API.csproj
# Chỉ đọc DB/file local; không cần khóa R2, không gọi R2.
dotnet run --launch-profile http -- --migrate-media-to-r2
# Đọc kế hoạch và các dòng FAIL trước khi áp dụng.
dotnet run --launch-profile http -- --migrate-media-to-r2 --apply
```

Tool chỉ chuyển file được DramaAssets/LiteraryWorks tham chiếu; file local không tham chiếu được giữ nguyên. Key đích xác định theo ID và filename gốc, không dùng GUID mới mỗi lần migration. PUT có If-None-Match:*; object có sẵn phải khớp size/MIME/SHA-256 khi đọc lại, nếu khác thì không ghi đè và không cập nhật DB. Chỉ sau xác minh mới CAS UPDATE RelativePath/CoverImageUrl của bản ghi chưa bị sửa đồng thời. Không đổi ID, scene/hotspot/manifest, giá gói, user hoặc Connection String; không xóa local hoặc remote object cũ. Tool có thể hoàn tất một phần khi một số file lỗi; trả exit code 1 và liệt kê ID cần xử lý, có thể chạy lại. Các asset đã có R2 key được HEAD kiểm tra, không upload lại. Dừng/chạy lại an toàn nếu upload đã xong nhưng chưa cập nhật DB.

Sau khi chuyển, cấu hình Render Storage__Provider=R2 cùng khóa mới và deploy source mới. Giữ appsettings.Local.json ở ngoài Git/Docker. Khi dùng cùng Somee đã đổi RelativePath sang R2 key, local runtime cũng cần chọn R2 để xem media; bản database local dự phòng không bị công cụ đụng tới.

### Kiểm tra thật sau khi nhập khóa mới

```powershell
dotnet run --launch-profile http -- --test-r2
```

Lệnh chỉ upload một GIF thử vào key GUID riêng dưới pro/assets/_diagnostics, xác minh HEAD và hash byte gốc, tạo URL GET 1 phút, thử Range 0–5 phải trả 206/image/gif rồi xóa đúng object thử. Không in URL đã ký hoặc secret, không sửa DB/media hiện có. Nếu mạng lỗi khi cleanup, kiểm tra/xóa object thử còn lại trong bucket. Các kiểm tra permission Free/PRO dùng HTTP test fixture, không thử mua gói/chuyển tiền thật.

Nếu thiếu cấu hình, lệnh liệt kê **tên key** như `R2:AccessKeyId`, không in giá trị. Khi có lỗi kết nối, output có `Step`, `Code` và HTTP status nếu nhận được response: `TLS_ERROR`/`NETWORK_ERROR` là nhóm lỗi kết nối; `ACCESS_DENIED` cần kiểm tra quyền Object Read & Write cho bucket; `INVALID_ACCESS_KEY`/`SIGNATURE_MISMATCH` cần kiểm tra cặp S3 credentials; `BUCKET_NOT_FOUND` cần đối chiếu bucket/account; `CLOCK_SKEW` cần kiểm tra giờ máy. Không gửi secret hoặc signed URL trong ảnh log. Chỉ có `PASS upload` mới chứng minh đã upload thành công; cấu hình đúng định dạng chưa chứng minh khóa được Cloudflare chấp nhận.

Với upload lớn hơn khả năng Render, hướng tiếp theo là direct upload ba bước: Backend cấp PUT tới key tạm gắn user/Admin; FE PUT trực tiếp R2; Backend finalize bằng HEAD/đọc signature, kiểm tra size/MIME/quyền rồi mới tạo metadata. **Các endpoint direct upload này chưa được triển khai**; multipart hiện tại vẫn hoạt động trong giới hạn 200 MiB. Không cấp PUT tùy ý rồi tin metadata FE gửi lên.

R2 không sửa lỗi schema Somee đã xác nhận: DramaScenes thiếu 8 cột, DramaHotspots thiếu 13 cột, CulturalInsightViews thiếu/không nhìn thấy. Công cụ migration media không truy vấn các cột scene này, nhưng xem tương tác/publish legacy vẫn cần chuyển đổi schema dựa trên kết quả Diagnose_Drama_Schema.sql. Không coi tích hợp R2 là đã sửa xong schema kịch.

### File thay đổi và tình trạng kiểm thử R2

| Nhóm | File |
|---|---|
| Dependency/cấu hình | `VietVan.API.csproj`, `.env.example`, `Program.cs`, `Hosting/MediaStorageConfiguration.cs` |
| Storage | `Services/Storage/R2Options.cs`, `R2ObjectClient.cs`, `R2FileStorageService.cs`, `MediaUploadStage.cs`; `Services/Implementations/FileStorageService.cs`, `WorkCoverStorage.cs` |
| API/nghiệp vụ media | `Controllers/DramaAccessController.cs`, `Controllers/Admin/AdminDramasController.cs`, `AdminWorksController.cs`, `AdminWorkMediaController.cs`, `Controllers/MediaImagesController.cs`; `Services/Implementations/DramaService.cs`, `DramaEditingService.cs`; `Middlewares/ApiExceptionMiddleware.cs` |
| Metadata avatar | `Entities/UserAvatar.cs`, `Data/VietVanDbContext.cs`, `scripts/Upgrade_User_Avatars.sql` |
| Công cụ/tài liệu/test | `Hosting/MediaMigration.cs`, `Hosting/R2SmokeCheck.cs`, `configuration/R2_CORS.example.json`, `tests/VietVan.Payment.Tests/R2StorageTests.cs`, `R2MediaAuthorizationTests.cs`, `scripts/Test-RenderHosting.ps1`, `README.md` |

Kiểm tra tĩnh đã thực hiện: `git diff --check` không có lỗi whitespace; CORS JSON parse hợp lệ; script avatar được `TSql150Parser` kiểm tra với 0 lỗi cú pháp; `appsettings.Local.json` được Git ignore. Chưa chạy script SQL, chưa upload/chuyển/xóa object R2 và chưa đổi dữ liệu Somee.

**Kết quả bản cuối ngày 26/11/2026:** người dùng đã restore thành công tại Terminal; assets xác nhận đúng hai phiên bản SDK 4.0 nêu trên, không còn System.IO.Hashing 11. Build và publish Release thành công. **216/216 test đạt, 0 bỏ qua**, báo cáo `tests/VietVan.Payment.Tests/TestResults/r2-configuration-diagnostics.trx`, gồm kiểm tra chẩn đoán không lộ secret. Không còn cảnh báo dependency; máy dùng SDK .NET 11 preview để build target net8.0, Dockerfile vẫn dùng SDK/runtime 8.0. Các action upload mới hoạt động với Swagger và vẫn nhận multipart field `file`.

Publish output tại `artifacts/r2-publish` không chứa Local/Development settings hoặc media local. `scripts/Test-RenderHosting.ps1` đạt **5/5 kiểm tra HTTP**: PORT/health không truy cập DB, Swagger Production, HTTPS proxy + credentialed CORS đúng origin, Google challenge cookie + từ chối token sai, SePay từ chối API key sai. Dùng cấu hình giả và Storage Local riêng cho smoke hosting; không truy cập Somee/R2 thật, không gửi email/chuyển tiền. Không có Docker CLI nên chưa build image.

**Kiểm tra bucket thật đã đạt:** Terminal người dùng ngày 26/11/2026 chạy `--test-r2` và xác nhận đủ 4 dòng PASS: upload object mới; HEAD/SHA-256 byte gốc; presigned GET/HTTP Range 206; xóa object thử. Đây là kết quả do người dùng trực tiếp chạy bằng cấu hình local, không phải kiểm tra trên Render. **Chưa chuyển media cũ hoặc thay dữ liệu Somee.** Bước tiếp theo là dry-run `--migrate-media-to-r2`, đối chiếu kế hoạch/file thiếu trước khi apply.

**Dry-run media cũ đã đạt:** log Terminal người dùng có 91 dòng PLAN asset, không có FAIL hoặc SKIP; `moved=0; skipped=0; failed=0` đúng vì chưa apply. Kịch 22 có 76 asset, kịch 23 có 15 asset. Đối chiếu file local hiện tại thấy đủ 91 file, tổng 287840506 byte (274,51 MiB), gồm 8 GIF và 2 video. Phân bổ đích: 2 free/thumbnails, 2 free/videos, 9 pro/backgrounds, 42 pro/characters, 18 pro/assets, 18 pro/audio. Không có PLAN work trong log này, nên không kết luận bìa tác phẩm đã được kiểm tra/chuyển. Trước apply cần sao lưu DB và media, tạm dừng Admin upload/chỉnh sửa; Render phải có source và env R2 để đọc các key sau khi chuyển. Chưa xác nhận apply hoàn tất.

Các lệnh để chạy lại:

```powershell
dotnet restore tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj -p:NuGetAudit=false
dotnet build VietVan.API.csproj -c Release --no-restore
dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj -c Release --no-restore
dotnet publish VietVan.API.csproj -c Release --no-restore -o artifacts/r2-publish /p:UseAppHost=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-RenderHosting.ps1 -PublishDirectory artifacts/r2-publish
# Nếu đã cài Docker:
docker build -t vietvan-api:r2 .
```

Các test mới kiểm tra byte GIF, MIME/signature/size, key traversal, SDK ký offline với khóa giả, migration dry-run/rerun/conflict, HTTP cấp URL theo publish/Admin/PRO và hạn subscription, thay file giữ ID và guard snapshot, thay/xóa bìa tác phẩm và avatar riêng từng user. Smoke R2 thật với GIF đã đạt theo Terminal người dùng; cần thử MP4/video seek và CORS trên trình duyệt sau deploy. Không tự push GitHub. Dockerfile Path vẫn `./Dockerfile`, Root Directory để trống (repo root).

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

Lệnh `--check-db-readonly` chỉ chạy SELECT: tên database/SQL version/compatibility level và các projection `TOP (0)` của 13 bảng: LiteraryWorks, Users, UserAuthStates, UserSubscriptions, SubscriptionPackages, DramaProductions, DramaAssets, DramaScenes, DramaHotspots, DramaManifests, DramaViewings, CulturalInsightViews, Reviews. Sáu bảng cuối được bổ sung sau lần kiểm tra ban đầu 7 bảng để bao phủ thêm luồng chi tiết/tương tác kịch. Lệnh đối chiếu tên cột thực tế với model EF, in riêng `PASS`/`FAIL` cho từng bảng và tên cột còn thiếu; tiếp tục kiểm tra bảng sau khi một bảng lệch schema. Không đọc ra user record/password hash, không trả nội dung tác phẩm, không UPDATE/INSERT/DELETE, không seed/migration và không khởi động PaymentExpiryWorker. Mật khẩu placeholder/Windows Authentication Somee bị chặn trước khi kết nối. Lỗi cấu hình/schema chỉ in bước kiểm tra, tên bảng/cột, loại/mã lỗi phù hợp, không in chuỗi hoặc provider message chứa dữ liệu nhạy cảm. Kiểm tra này xác nhận bảng/cột và projection rỗng; không thay thế kiểm tra kiểu dữ liệu, dữ liệu thực tế, FK/index hoặc API nghiệp vụ.

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

### Chẩn đoán kịch không hoạt động sau deploy

Database lưu bản ghi kịch, scene/hotspot, JSON manifest và khóa `DramaAssets.RelativePath`; ảnh/GIF/audio/video thực được LocalFileStorageService lưu trong `PrivateMedia`. Import SQL không chuyển các file này. Docker hiện loại PrivateMedia khỏi build; Render Free chỉ có filesystem tạm. Kiểm tra local hiện thấy 95 file drama-assets, tổng 290826359 byte; chưa đối chiếu từng file với các asset trên Somee, chưa xác nhận chúng tồn tại trên Render. Không kết luận mất toàn bộ dữ liệu database từ lỗi 404 media.

Làm rõ từng lớp trước khi sửa:

- Chạy lại `dotnet run --launch-profile http -- --check-db-readonly` để kiểm tra 13 bảng; kết quả 7 bảng trước đây chưa xác nhận schema scene/hotspot/manifest/view/review.
- Trong SSMS kết nối Somee, chọn VietVan_New, chạy **`scripts/Diagnose_Drama_Deployment.sql`** (mặc định kịch 22). Script chỉ SELECT: số hàng xấp xỉ, trạng thái public, danh sách asset/khóa file, cờ có bản nháp/bản xuất bản và số scene/hotspot. Không in nội dung JSON hoặc thông tin đăng nhập; không publish hoặc sửa dữ liệu. Không có scene/manifest có thể hợp lệ với kịch chỉ có video.
- So sánh `GET /api/admin/dramas/22`, `/api/admin/dramas/22/manifest`, `/api/admin/dramas/22/assets` và API media với JWT Admin trên Render. Metadata đọc được nhưng media 404 cần kiểm tra file/khóa; response 503 DATABASE_UPGRADE_REQUIRED cần đối chiếu schema; API public chỉ trả kịch IsPublished=true và Status=PUBLISHED, tài nguyên tương tác còn cần quyền PRO/Admin.

Khôi phục media cần chuyển file thực theo đúng storage key, giữ nguyên ID asset và liên kết scene/hotspot/manifest. Upload lại bằng POST assets sẽ tạo ID mới, không tự thay thế asset cũ. Giải pháp lâu dài cần tích hợp storage bên ngoài cho upload và đọc media, giữ kiểm tra quyền hiện có; chỉ đổi Storage__RootPath không tự upload file lên cloud. Chưa tích hợp provider hoặc di chuyển file trong bước chẩn đoán này.

Kiểm thử sau khi mở rộng chẩn đoán: **201/201 test đạt** (`drama-deployment-diagnostics.trx`), gồm thiếu cột scene preview, snapshot manifest cũ và phiên bản lượt xem. Chưa chạy lại 13 bảng trên Somee; không tự động push/deploy hoặc áp dụng thay đổi database.

Kết quả 13 bảng mới do người dùng cung cấp: 10 PASS; DramaScenes thiếu 8 cột, DramaHotspots thiếu 13 cột, CulturalInsightViews báo SQL 208. Xem `DATABASE_COMPATIBILITY_REPORT.md` để biết danh sách cụ thể. Đã mở rộng `scripts/Diagnose_Drama_Schema.sql` lấy cấu trúc toàn bộ bảng Drama (gồm media/playback segment/target segment nếu có), PK/FK/index/check và số hàng. Cần đối chiếu metadata này trước khi viết bản chuyển đổi; chưa tạo/chạy migration sửa scene/hotspot, chưa tự thêm timeline, tọa độ hoặc liên kết giả. Script chỉ đọc, kiểm tra cú pháp SQL Server 2019 đạt.

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

Local vẫn mặc định SMTP với `Email:Smtp` trong User Secrets. Các biến SMTP tùy chọn đã có chú thích ở `.env.example`; không đưa Gmail App Password vào Git. `--test-email YOUR_OWN_EMAIL` chỉ chạy trong Development, dùng đúng `Email:Provider` đã chọn (SMTP hoặc Resend), không sửa database/media; chỉ chạy khi thực sự muốn gửi một thư thử.

**Chẩn đoán ngày 26/11/2026:** người vận hành xác nhận Render chưa có `Email__Provider`, `Email__Resend__ApiKey`, `Email__Resend__FromAddress`, chỉ có hai biến GoogleSignIn. Docker mặc định chọn Resend nhưng không cung cấp API key hoặc địa chỉ gửi; đây là cấu hình thiếu đã xác nhận. Google OAuth Client ID không phải khóa gửi thư. Người dùng đã đính chính: **chưa có tài khoản Resend và chưa có domain gửi**; cần tạo tài khoản/lấy key trước khi cấu hình Render. Chưa kiểm chứng gửi Gmail thật.

Trong Render → service `viet-van-backend` → Environment, thêm đúng tên sau; giữ nguyên tất cả biến R2, SQL, JWT, Google và SePay:

| Key | Value |
| --- | --- |
| `Email__Provider` | `Resend` |
| `Email__Resend__ApiKey` | API key từ Resend → API Keys, có quyền gửi thư; nhập trực tiếp vào Render, không đưa vào chat/Git |
| `Email__Resend__FromAddress` | Khi chưa có domain: `onboarding@resend.dev`, **chỉ thử tới địa chỉ email chủ tài khoản Resend** |
| `Email__VerificationBaseUrl` | `https://viet-van-backend.onrender.com` |

Muốn gửi tới Gmail của người dùng khác: Resend → Domains → thêm domain bạn có quyền DNS → tạo đúng các bản ghi Resend yêu cầu → đợi Verified → đổi `Email__Resend__FromAddress` sang địa chỉ thuộc domain đó. Không tự mua domain/tạo dịch vụ trả phí. Chưa có domain thì không thể khẳng định luồng gửi thư cho mọi người dùng đã hoạt động; không dùng Client ID Google hoặc khóa R2 thay email key.

Sau Save/redeploy, Render Logs sẽ có `Email delivery configured Provider=Resend ConfigurationReady=True InvalidKeys=none`. Nếu thiếu biến, log chỉ in tên biến. Dùng sender thử sẽ có thêm `TEST_SENDER_RESTRICTION`. Đây chỉ là kiểm tra cấu hình, chưa chứng minh thư được chấp nhận hoặc tới Inbox. Chờ ít nhất một phút giữa các lần gửi lại, gọi `POST /api/auth/resend-verification` với email đã đăng ký, rồi xem `EmailSendAcceptedByProvider` hoặc `EmailSendFailed ... HttpStatus=...` và Resend → Emails. Response 200 chung của resend tránh lộ tài khoản, không phải cam kết gửi thành công. `EmailVerificationSkipped` ghi rõ Cooldown/AlreadyVerified/AccountLocked/AuthStateMissing vào log nội bộ.

HTTP 401 từ Resend: kiểm tra API key; 403: kiểm tra quyền key, domain hoặc giới hạn địa chỉ nhận thử; 429: kiểm tra rate limit/hạn mức trong Resend. Không log/chép email body, token, provider body hoặc API key để chẩn đoán. Tham khảo [mã lỗi Resend](https://resend.com/docs/api-reference/errors).

Kiểm tra cấu hình cục bộ, không gửi email hoặc truy cập database/R2:

```powershell
dotnet run --launch-profile http -- --check-email-config
```

Kết quả local chỉ phản ánh cấu hình local/User Secrets/env của tiến trình đó, không đọc được biến trong Render. Kiểm tra schema đầy đủ `--check-db-readonly` hiện có thêm `dbo.EmailVerificationTokens`; lệnh này chỉ SELECT TOP (0), không tự nâng cấp schema hoặc seed.

Kiểm thử lượt sửa này: **277/277 test đạt**, gồm cấu hình chỉ có Google không đủ gửi mail, không lộ secret/status provider, token một lần/hết hạn, quyền media PRO/Admin, upload R2 và mô phỏng hai container có filesystem/DI/DbContext mới dùng cùng database/object store. Snapshot draft/published, revision, asset ID/key/byte và liên kết video giữ nguyên trong test. Báo cáo `tests/VietVan.Payment.Tests/TestResults/email-media-redeploy-regression.trx`. Chưa kiểm chứng domain/key/gửi Gmail thật hoặc redeploy Render thật; agent không sửa database, media, biến Render hay push code.

Publish Release đạt tại `artifacts/email-media-redeploy-publish`. `scripts/Test-RenderHosting.ps1` đạt trên bản publish Production local: `/health`, Swagger, proxy HTTPS/CORS, challenge cookie và từ chối token Google/API key SePay không hợp lệ. Dùng SQL hostname `.invalid`, R2/key giả; không kiểm tra cloud thật hoặc gửi mail. Log startup xác minh email thiếu cấu hình vẫn cho API/Storage R2 khởi động, không fallback Local. Lệnh `--check-email-config` trên máy hiện tại trả SMTP/local cấu hình hợp lệ; đây không phải cấu hình Render.

File thay đổi lượt này: `Program.cs`, `Hosting/DatabaseReadOnlyCheck.cs`, `Services/Authentication/EmailDeliveryConfiguration.cs`, `Services/Authentication/AccountVerificationService.cs`, `Services/Authentication/ResendEmailSender.cs`, hai test `EmailDeliveryConfigurationTests.cs`/`MediaStorageDeploymentTests.cs`, `.env.example`, README này và `MEDIA_STORAGE_OPERATIONS_VI.md`. Không thay storage implementation, entity/model kịch, migration, Dockerfile, Auth token rules hoặc API contract.

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

**Source có adapter Cloudflare R2; cần cấu hình và chuyển media cũ theo mục Cloudflare R2 ở đầu tài liệu.** Mặc định chưa cấu hình vẫn dùng local filesystem. Render Free dùng filesystem tạm; không dùng file local trên Render làm bản duy nhất. Xem [giới hạn Render Free](https://render.com/docs/free).

Hai nơi cần lưu bền vững là `PrivateMedia/drama-assets/` (video/audio/ảnh/GIF/asset tương tác, được API kiểm tra quyền) và `wwwroot/uploads/work-covers/` (cover tác phẩm). Chúng đã được loại khỏi Docker context/publish output; **file gốc trong workspace và metadata database không bị xóa hoặc đổi đường dẫn**. Lần rà soát ghi nhận 95 file private khoảng 291 MB và 6 cover khoảng 5,9 MB. Các asset đó không tự xuất hiện trong container mới.

Phương án đề xuất cho demo có hạn mức miễn phí: bucket private trên [Cloudflare R2 Standard](https://developers.cloudflare.com/r2/pricing/) hoặc một S3-compatible storage đã có. R2 hiện có quota Standard miễn phí nhưng có thể tính phí khi vượt hạn mức; tự xem điều kiện tài khoản/billing trước khi đăng ký. Không tự mở dịch vụ hoặc bật thanh toán.

Các bước vận hành trước khi chuyển nguồn đọc sang R2:

1. Sao lưu database và cả hai thư mục media; kiểm tra số lượng, tổng byte và SHA-256 trước/sau copy. Dùng copy, không move/xóa file gốc.
2. Chạy `--migrate-media-to-r2` để xem mapping, sau đó mới dùng `--apply`; công cụ xác minh bản copy trước khi cập nhật key, giữ nguyên asset ID và file local.
3. Chọn `Storage__Provider=R2` và nhập các biến `R2__...`; API kiểm tra quyền trước khi cấp presigned GET, R2 xử lý tải byte và Range.
4. `Storage__RootPath` chỉ dành cho local; không nhập bucket URL vào biến này. Giữ bucket private và chỉ đặt S3 credentials ở Backend.
5. Kiểm tra upload/tải lại, GIF động, video seek và quyền Free/PRO trên Render trước và sau redeploy. Hướng dẫn và giới hạn signed URL ở mục R2 phía trên.

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
