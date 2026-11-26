# Vận hành media kịch

## Kiểm tra bảo toàn dữ liệu khi sửa gửi email (26/11/2026)

Lượt sửa email không thay Dockerfile, Storage__Provider, tên biến R2, connection string, object key, entity kịch, ID/revision/manifest hoặc dữ liệu thật. Render vẫn mặc định R2, từ chối cấu hình Local và không fallback khi R2 lỗi. Startup không chạy migrate/seed/media recovery; công cụ --apply chỉ chạy khi người vận hành gọi tường minh. Không xóa hoặc tạo lại bucket/kịch/asset để sửa email.

Test mới `NewContainersReadSameMediaAndPreserveDraftPublishedSnapshotsAndObjectKeys` tạo hai filesystem/DI provider/DbContext độc lập dùng cùng database SQLite và object store giả lập: cả bìa, video, GIF và audio tiếp tục nhận URL mới; ID/key/byte, trạng thái publish, liên kết FREE video, revision 5 và draft/published JSON giữ nguyên. Test cùng các test phân quyền PRO/Admin và upload hiện có thuộc **277/277 test đạt**, báo cáo `tests/VietVan.Payment.Tests/TestResults/email-media-redeploy-regression.trx`. Đây là kiểm thử offline, không thay thế kiểm chứng Render/R2 thật. Chưa có report planner mới hoặc audit cloud mới để xác nhận 78 key thiếu trước đó đã được phục hồi.

Để đối chiếu trước/sau redeploy mà không sửa dữ liệu, chạy lệnh sau trên tiến trình có cấu hình **cùng database và bucket với Render** rồi lưu output bên ngoài container tạm thời:

```powershell
dotnet run --launch-profile http -- --audit-media-storage-readonly
```

Khi không có ai chỉnh kịch ở giữa hai lần, `DatabaseTargetRef`, `MetadataFingerprint` và các số matched/missing/mismatched phải giữ nguyên. Fingerprint đổi có thể do chỉnh kịch hợp lệ hoặc đổi database; cần đối chiếu, không reset dữ liệu. Sau deploy phải thấy `Provider=R2 BucketName=vietvan-media` trong log Render và thử route media API bằng quyền tương ứng; không dùng lại signed URL đã hết hạn. Lệnh audit chỉ SELECT/HEAD, không PUT/DELETE/UPDATE. File thiếu từ trước vẫn cần xác minh nguồn/key riêng, không phải chạy lại seed hoặc upload lại toàn bộ.

## Lỗi sau Render redeploy: kết quả kiểm tra ngày 26/11/2026

**Nguyên nhân đã tái hiện trong source, chưa xác nhận là nguyên nhân duy nhất của Production:** trước bản sửa, `StorageOptions.Provider` mặc định `Local`; `AddMediaStorage` chỉ chọn R2 khi `Storage:Provider=R2`, còn Dockerfile không đặt provider. Nhập đủ 5 biến `R2__...` không tự chọn R2. Test mới dùng đúng cấu hình này đã thất bại với `Expected: R2; Actual: Local` trước khi sửa. Nhánh Local tìm file trong filesystem container thay vì đọc bucket, nên không tìm thấy media trên R2 sau khi container được thay thế. Trên máy Development, cấu hình Local JSON có thể chọn R2 nhưng file này không được publish sang Render; vì vậy kết quả local không chứng minh provider trên Render.

Ban đầu người dùng xác nhận bucket private `vietvan-media` có 91 object, tổng 287.85 MB. Kết quả Terminal mới do người dùng cung cấp ngày 26/11/2026 đã xác nhận cả 91 asset cũ (#17–#107) đều HEAD_MATCH; thêm #112/#114 đã khôi phục và xác minh SHA256 thành công. Audit tổng cộng 171 asset: **93 khớp, 78 thiếu, 0 sai size/MIME, 0 key URL/sai, 0 migration candidate, 0 lỗi bìa tác phẩm, 0 snapshot chứa dấu hiệu signed/localhost URL**. HEAD_MATCH kiểm tra size/MIME, riêng hai file phục hồi đã đọc lại byte để kiểm tra SHA256. Đây không phải LIST toàn bucket hoặc xác minh GET/Range trên Render.

| Kịch | Asset khớp | Asset thiếu |
| --- | --- | --- |
| 22 | 76 | 1: #109 (video) |
| 23 | 15 | 1: #108 (video) |
| 24 | 0 | 2: #110 (ảnh), #111 (video) |
| 25 | 2: #112, #114 | 74: #113, #115–#187 |

Các key thiếu đều ở dạng legacy `drama-assets/...`, trong khi 91 key cũ đã có R2 dùng `free/...` hoặc `pro/...`. Source Local tạo namespace legacy này; dữ liệu audit chưa chứng minh file gốc bị mất ở máy nào hoặc thời điểm nào. Không tự liên kết lại theo file giống tên/kích thước nếu chưa xác minh nội dung.

Mốc đối chiếu từ audit người dùng chạy: `DatabaseTargetRef=91E0370CBFD83A28`, `MetadataFingerprint=9b1ecc6f48f57f7e5105cd0799f5d8fd58d3034300e6f7c3f37e71e0dfaaf43b`. Bản ghi evidence theo từng ID/key nằm trong file ignored `artifacts/media-recovery/audit-evidence-2026-11-26.json`. Chưa có mốc sau redeploy để so sánh. Đã tìm tên file chính xác của 78 key thiếu trong dự án và Downloads/Pictures/Desktop của máy hiện tại nhưng không thấy. Bản gốc có thể tồn tại với tên khác hoặc trong backup; cần thư mục gốc/nội dung được xác nhận trước khi phục hồi, giữ nguyên ID/key.

Đối chiếu Git local: R2 được thêm ở `11e9b79`; `git diff 11e9b79..830541c` không thay đổi `R2Options`, chọn storage, tạo key/ký URL, media controller, Program hay Dockerfile. Hai commit cuối `46bd8b1..830541c` chỉ liên quan payment/tests/tài liệu. Không có bằng chứng rằng các commit này đổi tên biến R2. Chưa có SHA bản chạy tốt/bản lỗi từ Render Events để xác định chính xác cặp deploy; không kết luận đây là diff good/bad đã xác nhận. Các module đó không được chỉnh trong lượt sửa media này.

Kiểm tra source hiện tại:

- `R2Options` bind đúng `AccountId`, `AccessKeyId`, `SecretAccessKey`, `BucketName`, `ServiceUrl` từ section `R2`; ASP.NET Core ánh xạ tên Environment Variables `R2__...`. Test dùng Environment Variables thật với prefix riêng xác nhận đủ 5 tên và độ ưu tiên so với JSON. Không đổi tên biến, không cần cấp lại key vì bản sửa này.
- Upload lưu `StoredFile.StorageKey` vào `DramaAssets.RelativePath`; DTO trả route `/api/dramas/{dramaId}/assets/{assetId}/media` ổn định. `R2FileStorageService.GetAccessAsync` HEAD rồi ký URL mới mỗi request. Không tìm thấy luồng source tự lưu presigned URL vào database. Bản dữ liệu Production hoặc snapshot lịch sử vẫn cần kiểm tra bằng audit.
- Normal startup không gọi `Migrate`, `EnsureDeleted`, `EnsureCreated`, script seed hoặc migration media. Lệnh migration/khôi phục chỉ chạy khi người vận hành truyền flag riêng rồi thoát trước Web Service/worker. Chưa có snapshot database trước/sau deploy để chứng minh Production không bị tác động bởi người dùng hoặc tiến trình bên ngoài.
- Quyền cover/FREE/PRO được kiểm tra trước khi cấp URL; PRO vẫn cần người dùng, subscription còn hạn và asset thuộc snapshot đã xuất bản. URL PRO bị giới hạn theo hạn subscription. Bucket và các prefix vẫn private; prefix `free` không tự mở object ra public.

Đã sửa trực tiếp:

| File | Thay đổi trong lượt này |
| --- | --- |
| `Hosting/MediaStorageConfiguration.cs` | Render mặc định R2, từ chối Local/giá trị khác và dừng khi thiếu cấu hình R2; không fallback Local. Local Development ngoài Render giữ hành vi cũ. |
| `Dockerfile` | Đặt mặc định `Storage__Provider=R2` trong runtime image .NET 8. |
| `Program.cs` | Log Provider/BucketName/Environment/Render, thêm lệnh audit chỉ đọc; không đổi endpoint/contract. |
| `Services/Storage/R2FileStorageService.cs` | Log lỗi thiếu object, quyền/mạng và key sai bằng mã an toàn cùng ObjectRef hash; không in exception provider, key, signed URL hoặc credential. Không đổi cơ chế key/ký URL. |
| `Hosting/MediaStorageAudit.cs` | Lệnh SELECT/HEAD toàn bộ liên kết media và bìa tác phẩm; phát hiện key URL/sai, key migration dự kiến, size/MIME không khớp và snapshot chứa URL đáng kiểm tra. Chỉ báo cáo, không tự đổi liên kết theo phỏng đoán. |
| `Hosting/DramaMediaInspection.cs` | Lệnh inspect cũ dùng cùng cách xác định provider với runtime mới. |
| `.env.example`, `README.md`, tài liệu này | Thống nhất tên biến, mặc định Render và cách chẩn đoán sau deploy. |
| `scripts/Test-RenderHosting.ps1` | Smoke test dùng R2 với credential giả, không gọi bucket thật; tương thích việc từ chối Local trên Render. |
| `tests/VietVan.Payment.Tests/MediaStorageDeploymentTests.cs` | Tái hiện lỗi trước sửa, kiểm tra binding 5 biến, precedence, cấu hình thiếu/sai, local và log không lộ secret. |
| `tests/VietVan.Payment.Tests/MediaStorageAuditTests.cs` | Audit không ghi/ký URL, fingerprint ổn định, không tự thay key và không in presigned URL đã lưu sai. |
| `tests/VietVan.Payment.Tests/MediaRestartTests.cs` | Hai Web host mới, ổ local trống, chung R2 giả lập/SQLite: cover, video FREE, background, character, object và audio PRO vẫn có URL mới; Admin truy cập được; khách ẩn danh không nhận URL PRO; ID/key/revision không đổi. |

Workspace còn các file inspect/recovery từ lượt xử lý trước (`DramaMediaRecovery`, `DramaMediaInspectionTests`, `DramaMediaRecoveryTests` và log 404 Admin). Không xóa các thay đổi đang có; khi commit Program cần đưa các file CLI được tham chiếu vào cùng commit. Chưa commit hoặc push GitHub.

**Environment trên Render:** giữ nguyên 5 biến R2 hiện có và `R2__BucketName=vietvan-media`. Khai báo rõ `Storage__Provider=R2`; nếu đang có giá trị Local thì đổi sang R2. Không đưa endpoint vào `Storage__RootPath`, không sửa Connection String Somee hoặc bất kỳ biến Auth/Payment/Subscription/SePay nào. Không bật public bucket và không chạy migration media `--apply` để chữa lỗi chọn provider.

Sau redeploy:

1. Đối chiếu commit SHA trong Render Events với commit vừa push. Đọc Logs, xác nhận `Media storage configured Provider=R2 BucketName=vietvan-media ... Render=True`. Thiếu/sai cấu hình sẽ dừng startup với tên field cần sửa, thay vì chạy Local và trả 404 hàng loạt.
2. Kiểm tra `/health` trả 200. Sau đó chạy `dotnet run --launch-profile http -- --audit-media-storage-readonly` với cấu hình cùng database/bucket để đối chiếu toàn bộ key. Lệnh trên máy chỉ chứng minh trạng thái tiến trình/mục tiêu đang kiểm tra; nếu có Render Shell thì chạy `dotnet VietVan.API.dll --audit-media-storage-readonly` trực tiếp trong container. Không cần Shell để dùng API.
3. Lưu `DatabaseTargetRef`, `MetadataFingerprint` và số `matched/missing/mismatched/invalidOrUrlKey` trước và sau restart, khi không có Admin sửa media/bản nháp. Cùng target/fingerprint cho biết liên kết/revision/JSON không đổi; không chứng minh mọi bảng database bất biến. HEAD_MATCH chỉ khớp size/MIME, không phải hash byte toàn object. Lệnh không LIST toàn bucket nên số key được kiểm tra không mặc định bằng số object trong dashboard.
4. Admin gọi `/api/admin/dramas/{id}/assets/{assetId}/access` và `/media` cho cover, video, audio, GIF. Access phải trả URL HTTPS mới cùng `expiresAt`; media trả redirect R2. Không lưu/log/chia sẻ query presigned URL. Dùng link còn hạn kiểm tra ảnh, âm thanh, GIF và video seek/Range 206; không suy từ `/health` rằng media đã tốt.
5. Gọi cover/FREE video công khai; kiểm tra user FREE không lấy được URL PRO, user PRO còn hạn lấy được asset đã xuất bản, Admin lấy được draft. Đợi link cũ hết hạn rồi gọi lại API access để nhận link mới. Khởi động/redeploy lần nữa và lặp lại các kiểm tra; không upload lại media. Bản nháp #25 phải tiếp tục revision 5 nếu không có thao tác chỉnh sửa.
6. Nếu `OBJECT_NOT_FOUND` vẫn xuất hiện, dùng ID/trace log của API và ObjectRef trong log R2 để đối chiếu audit. Nếu thấy migration candidate, cần kiểm chứng file/hash trước khi sửa key theo từng bản ghi. Không tự thay ID/cảnh hoặc chạy migration toàn bộ. Hai asset #112/#114 đã từng được xác nhận thiếu trên R2 ở lượt trước là vấn đề file cụ thể, khác với việc chọn nhầm Local; số 91 object không tự chứng minh hai file này đã được phục hồi.

**Kết quả test:** restore, build qua test, publish Release thành công. 252/252 test đạt, báo cáo `tests/VietVan.Payment.Tests/TestResults/media-redeploy-regression.trx`. Smoke test bản publish Production đạt PORT/health, Swagger, HTTPS proxy/CORS và các kiểm tra bảo vệ tích hợp hiện có; dùng database `.invalid` và R2 giả, không gửi email/thanh toán hoặc sửa database thật. Log xác nhận Provider=R2, bucket smoke. SDK cài trên máy là preview .NET 11 nhưng target app/test vẫn net8.0; Docker giữ SDK/runtime 8.0. Máy chưa có Docker CLI nên chưa chạy Docker build.

**Giới hạn kiểm tra Production:** HTTP Render trong sandbox thất bại; yêu cầu kiểm tra ngoài sandbox bị từ chối. Sau đó người dùng đã chạy audit thành công tại Terminal: R2/bucket `vietvan-media`/database `VietVan_New`, với kết quả SELECT/HEAD nêu trên. Người dùng cũng đã phục hồi hai object #112/#114 bằng CLI, không sửa database. Ảnh Render Logs do người dùng cung cấp xác nhận `Provider=R2 BucketName=vietvan-media Environment=Production Render=True`; bản đang chạy đã chọn R2. Agent chưa truy cập trực tiếp Render Environment/Logs, chưa có SHA deploy good/bad, chưa xác minh Render dùng cùng database/fingerprint trước-sau, chưa thử phát/Range bằng quyền thật trên Render. Không tự push/deploy, đổi Connection String, xóa object hoặc sửa liên kết media. Cần đối chiếu những file người dùng xác nhận còn trên R2 dưới key khác trước khi khôi phục 78 liên kết thiếu.

## Đối chiếu 78 key thiếu với toàn bộ object đang có trên R2

Người dùng xác nhận các file vẫn còn trên R2. `MISSING` trong audit chỉ có nghĩa **key đang lưu trong database không tìm thấy object**; không chứng minh nội dung đó không có dưới key khác. Chưa có mapping đã xác minh cho 78 ID. Không yêu cầu upload lại 91 file cũ, không tự trỏ các ID mới sang file khác chỉ vì cùng dung lượng.

Đã thêm lệnh dành cho người vận hành:

```powershell
dotnet run --launch-profile http -- --plan-missing-media-recovery
```

Lệnh LIST toàn bộ bucket (có phân trang), HEAD object, SELECT metadata gồm tên file gốc/size/MIME/key và video FREE đang cấu hình cho từng kịch. Nó đối chiếu cả key không nằm trong database, tìm bản sao cùng tên gốc/size/MIME ở các asset khác và những ứng viên chỉ khớp size/MIME. Đây chỉ là gợi ý kiểm tra nội dung; không tự áp dụng mapping, không đổi ID/key, không PUT/DELETE hoặc migration/seed. Không GET toàn bộ media và không in credential/signed URL.

Output in đường dẫn `artifacts/media-recovery/plan-.../report.json` (ignored). Trong report:

- `SAME_ORIGINAL_NAME_SIZE_MIME`: bản sao có cùng tên gốc, dung lượng và MIME; vẫn cần xác nhận đúng nội dung khi target không có hash gốc.
- `SIZE_MIME_ONLY`: chưa đủ bằng chứng xác định cùng file; cần đối chiếu nội dung, không tự chọn ngay cả khi chỉ có một ứng viên.
- `READY_LOCAL_EXACT_KEY`: tìm đúng file local tại chính key cần phục hồi, khớp size/MIME/signature; tạo batch plan có SHA256 để chạy bằng CLI recovery hiện có. Mỗi batch tối đa 20 asset cùng kịch. Chưa PUT cho đến khi người vận hành chạy recovery `--apply`.
- `NEEDS_ORIGINAL_OR_CONFIRMED_COPY` không có candidate: cần tìm thêm object/backup hoặc xác nhận bản gốc, không tạo file giả.

CLI phục hồi đã được mở rộng để nhận cả audio/video theo cùng giới hạn upload: ảnh 10 MiB, audio 30 MiB, video 200 MiB. Giữ kiểm tra đuôi/MIME/signature, SHA256, conditional PUT, không ghi đè và không sửa database. Hai ảnh #112/#114 đã phục hồi không cần chạy lại.

Lần chạy planner trong sandbox dừng `NETWORK_ERROR` ở LIST, chưa đọc được inventory thật; yêu cầu chạy ngoài sandbox bị từ chối. Cần người vận hành chạy lệnh trên rồi cung cấp output/đường dẫn report để tiếp tục đối chiếu và lập mapping cụ thể. Không kết luận 78 file đã được phục hồi. Đã kiểm tra thêm `VietVan.API.zip`: chỉ có 4 media demo và 6 cover tác phẩm, không có tên file gốc của 78 key thiếu.

Kiểm thử hiện tại: **259/259 test đạt**, báo cáo `tests/VietVan.Payment.Tests/TestResults/media-recovery-planner-regression.trx`. Bao gồm LIST có phân trang, lỗi inventory không lộ secret, tìm object ngoài DB, giữ ứng viên trùng size để xác nhận thay vì tự áp dụng, không ghi đè, kế hoạch local không ghi R2, phục hồi video/audio vào đúng key bằng byte giả lập. Chưa xác minh mapping/byte của 78 file thật trên cloud.

- Với `Storage:Provider=Local`, file do Drama Studio tải lên nằm trong `PrivateMedia/drama-assets/`; với R2, `DramaAssets.RelativePath` là key trong bucket private. Xem cấu hình R2 và công cụ chuyển file cũ trong README. Sao lưu media cùng bản sao SQL Server nhất quán; không đưa thư mục private vào `wwwroot` hoặc public qua proxy.
- File chỉ được xóa sau khi API đã kiểm tra tham chiếu cảnh, preview và hotspot. Upload lỗi hoặc bị hủy được dọn khi tiến trình còn hoạt động; sự cố mất điện/tiến trình có thể để lại file mồ côi.
- Khi kiểm tra file mồ côi, so sánh đường dẫn tương đối trong `DramaAssets` với file dưới `PrivateMedia/drama-assets` trên bản sao lưu. Không tự động xóa: loại trừ upload đang chạy và file demo, kiểm tra bản sao lưu, chờ ít nhất 24 giờ rồi người vận hành xác nhận từng file trước khi xử lý.
- ASP.NET Core bind `IFormFile` có thể đệm nội dung vào file tạm. Bố trí dung lượng cho thư mục tạm và `PrivateMedia`, giới hạn upload tại reverse proxy, kiểm tra hạn mức đĩa và cấu hình backup. Mặc định mỗi instance giới hạn sáu lượt upload/phút theo người dùng; cần rate limit tập trung khi chạy nhiều instance.
- Kiểm tra malware bằng giải pháp phù hợp trước khi triển khai công khai. Kiểm tra chữ ký file không thay thế quét malware hoặc xác thực đầy đủ nội dung media.
- Nếu triển khai nhiều instance, dùng storage dùng chung với quyền hạn phù hợp hoặc thiết kế object storage riêng; file trên đĩa cục bộ không tự đồng bộ giữa các instance.
- File `VietVanNew.sql` trước đây mô tả `DramaMediaAssets` và `DramaPlaybackSegments`, khác EF model hiện tại (`DramaAssets`, `DramaScenes`). Không tự sửa/xóa hoặc chuyển URL cũ theo phỏng đoán. Đối chiếu schema và dữ liệu thực bằng `scripts/Diagnose_VietVan_New.sql` trước khi thiết kế chuyển đổi có kiểm soát.

## Kiểm tra 404 mà giữ nguyên ID, cảnh và bản nháp

Ví dụ kịch 25, ảnh bìa 112 và ảnh nền 114:

```powershell
dotnet run --launch-profile http -- --inspect-drama-media=25:112,114
```

Lệnh thoát trước khi khởi động Web Service/worker: chỉ SELECT metadata/revision, kiểm tra file local và HEAD key R2. Không PUT/DELETE, không SaveChanges, không sửa scene, manifest, revision hoặc key. Output gồm database, provider của **tiến trình đang chạy lệnh**, revision, key tương đối, size/MIME mong đợi và trạng thái local/R2. Không in secret, Connection String, signed URL hoặc toàn bộ JSON bản nháp. Provider/env của máy local chưa chứng minh cấu hình Render giống nhau.

- `METADATA_NOT_FOUND_FOR_DRAMA`: không có asset đúng ID/kịch trong database đang kiểm tra.
- `LOCAL=MATCH`: bản local khớp size/MIME khai báo và chữ ký file; `R2=MATCH`: HEAD khớp size/MIME, chưa phải xác minh hash byte remote.
- `FILE_EXISTS_ON_R2` kèm local missing: nếu Render vẫn 404, đối chiếu `Storage__Provider`, bucket và database trên Render trước khi upload lại.
- `LOCAL_COPY_AVAILABLE`: có bản local phù hợp; chỉ khôi phục sau khi xác định đúng key/file, giữ nguyên ID.
- `MIGRATION_CANDIDATE ... EXISTS_REQUIRES_VERIFICATION`: tìm thấy key đích theo quy tắc migration cũ, chưa tự đổi metadata hoặc khẳng định nội dung đúng. Cần đối chiếu hash/metadata trước khi cập nhật.
- `NOT_CHECKED` hoặc `R2_CHECK_FAILED` không có nghĩa object đã mất. Kiểm tra cấu hình/kết nối/quyền rồi thực hiện lại HEAD.

API Admin ghi log 404 với `Reason=METADATA_NOT_FOUND` hoặc `Reason=FILE_NOT_FOUND`, ID kịch/asset, loại storage và traceId; không ghi key hoặc signed URL. Giữ nguyên response 404 và quyền Admin. Dùng trace mới sau khi deploy để đối chiếu Render Logs.

Nếu cần khôi phục byte ảnh, ưu tiên đặt lại bản gốc đã xác minh vào đúng object key đang lưu, không tạo asset mới. Khi cần upload thay file qua API, dùng `PUT /api/admin/dramas/25/assets/114/file` hoặc `/112/file`, multipart field `file`; endpoint giữ nguyên asset ID và áp dụng guard snapshot đã xuất bản hiện có. Không xóa/tạo lại kịch hoặc cảnh; không chạy migration tất cả media để xử lý hai ID này. Cần bản gốc hoặc bản sao lưu để khôi phục; không thay bằng ảnh khác hoặc dựng lại ảnh theo phỏng đoán.

## Phục hồi ảnh gốc đã xác nhận vào đúng key R2

Kết quả kiểm tra cloud do người vận hành chạy ngày 26/11/2026: revision 5; cả hai key gốc và key migration dự kiến đều `R2=MISSING`. Đã tìm được hai bản trong kịch 22 khớp dung lượng; người dùng xác nhận đúng nội dung cả PNG năm thầy bói và GIF bầu trời. Đã sao chép local vào đúng đường dẫn kịch 25, giữ nguyên bản trong kịch 22 và xác minh SHA256:

| Asset | Key đang lưu trong database | Bytes | SHA256 bản gốc đã xác nhận |
| --- | --- | --- | --- |
| 112 | `drama-assets/25/background/1ef9d16139b44cd8a1dfeef1442b138a.png` | 3778506 | `6f3a44df299c46a39407d9df6bebd7837d8d09f7e6642606b24fedf975f18365` |
| 114 | `drama-assets/25/background/bd87acf36ae74a90af602899c42a162f.gif` | 3997553 | `cda29ea53980e52ce69d61d8c930cece76e51c48cf9312d4731e0df975d5f006` |

Kế hoạch phục hồi riêng của máy ở `artifacts/media-recovery/drama-25.json`; file này và media private được gitignore. Không cần đưa media hoặc credentials lên GitHub. CLI chỉ dành cho người vận hành; không có endpoint công khai mới.

```powershell
# Chỉ xác minh nguồn và HEAD/hash remote, không ghi:
dotnet run --launch-profile http -- --restore-missing-r2-media=artifacts/media-recovery/drama-25.json
# Phục hồi object thiếu:
dotnet run --launch-profile http -- --restore-missing-r2-media=artifacts/media-recovery/drama-25.json --apply
# Kiểm tra metadata/revision sau phục hồi:
dotnet run --launch-profile http -- --inspect-drama-media=25:112,114
```

CLI phục hồi không kết nối database hoặc khởi động Web Service/worker. Nó kiểm tra tất cả file nguồn trước PUT: key đúng kịch, đuôi/MIME/chữ ký file, giới hạn ảnh 10 MiB/audio 30 MiB/video 200 MiB, dung lượng và SHA256 đã xác nhận. File nguồn được giữ khóa đọc đến khi xong. Chỉ tạo object thiếu với `If-None-Match: *`; object có sẵn phải khớp SHA256/size/MIME, nếu khác thì dừng, không ghi đè. Sau copy phải HEAD và đọc lại toàn bộ byte để xác minh SHA256. Không chuyển mã ảnh nên GIF giữ nguyên mọi frame. Không DELETE hoặc cập nhật ID/key trong database; cảnh và revision tiếp tục trỏ đúng tài nguyên.

Hai object không được ghi theo một transaction: nếu mạng ngắt sau object đầu, file đã phục hồi được giữ lại. Có thể chạy lại cùng lệnh an toàn; object khớp không được PUT lần nữa. Khi thành công phải thấy `PASS asset=112`, `PASS asset=114` và `Recovery completed: verified=2`. Sau đó Admin thử lại hai route media trên Render và mở bản nháp #25. Nếu R2 đã khớp nhưng Render vẫn 404, kiểm tra `Storage__Provider=R2`, bucket và database trên Render; không tạo lại asset.

Trạng thái cập nhật theo Terminal người dùng ngày 26/11/2026: **đã phục hồi cả local và R2 cho #112/#114**. CLI trả `PASS` size/MIME/SHA256 cho từng ID và `Recovery completed: verified=2`; audit tiếp theo xác nhận cả hai key HEAD_MATCH. Không cần chạy lại apply cho hai ảnh này. ID/key và database không bị thay đổi bởi lệnh phục hồi. GIF giữ nguyên byte gốc. Chưa truy cập Render Logs hoặc kiểm chứng hai route trên Render. Trace ảnh nền ban đầu: `00-167e51c3858fa49be06630e1f77d7435-737a7e21f34c25cc-00`.

Kiểm thử offline: **242/242 test đạt**, báo cáo `tests/VietVan.Payment.Tests/TestResults/drama-media-recovery-regression.trx`. Bao gồm chẩn đoán không ghi, giữ ID/revision/tham chiếu cảnh qua API thay file, phục hồi nguyên byte GIF vào đúng key, dry-run không PUT, chạy lại không PUT trùng, từ chối hash/chữ ký/MIME/key kịch/đường dẫn sai, và không ghi đè object khác nội dung. Kết quả này xác minh source, không thay thế kiểm tra cloud thật.
