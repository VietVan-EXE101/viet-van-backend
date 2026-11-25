# Khởi tạo VietVan_New bằng Windows Authentication

Baseline `20261124071000_FreshBaseline` được tạo từ model EF Core hiện tại trong một assembly riêng. Hai migration lịch sử của API phụ thuộc schema cũ và không phải baseline cho database trống; chúng không bị sửa. Baseline bổ sung bốn foreign key cho lượt xem insight và đơn kích hoạt; các quan hệ này chưa được khai báo đầy đủ trong model API gốc.

**Chưa chạy trên SQL Server thực.** Chỉ dùng cho `VietVan_New` đã tồn tại và hoàn toàn trống; không chạy trên database chứa bảng, views hoặc dữ liệu cần giữ. Không có script nào dưới đây kết nối hay xác minh database tự động trong sandbox. Database cũ không được động tới.

## Cách 1 — SSMS

1. Đăng nhập SSMS bằng Windows Authentication, kết nối đúng `localhost\SQLEXPRESS`. Cần tài khoản có quyền tạo bảng, constraint và index trong `VietVan_New`, với default schema `dbo`.
2. Trước khi chạy, chọn database `VietVan_New`, xem các bảng, views, migration history và xác nhận database hoàn toàn trống. Tài khoản phải có quyền xem metadata đầy đủ; nếu thiếu quyền, danh sách có thể không đầy đủ.
3. Mở `scripts/VietVan_New_Baseline.sql`, xác nhận dropdown database ở SSMS là `VietVan_New`, rồi Execute toàn bộ script. Không chọn riêng một phần script.
4. Script kiểm tra tên database, tên instance, default schema và bảng hiện có trước khi tạo bảng. Sau đó tạo schema trong một transaction; gặp lỗi sẽ rollback và báo lỗi. Chạy lại sau khi thành công sẽ từ chối vì database không còn trống. Việc chỉ kiểm tra tên instance không xác nhận tên máy; người chạy phải kiểm tra server ở bước 1.

Không dùng `artifacts/FreshBaseline.sql` hay SQL từ `dotnet ef migrations script` trực tiếp trong SSMS: bản EF mặc định có nhiều batch `GO` và tạo history trước guard. Để tái tạo bản script SSMS sau khi sửa migration, build project bootstrap rồi chạy `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/GenerateFreshBaseline.ps1` ở thư mục gốc.

## Cách 2 — EF Core từ PowerShell của người dùng

Đây là phương án thay thế, **không chạy sau cách 1**. Đầu tiên thực hiện bước kiểm tra trống và quyền như ở trên. EF có thể tạo bảng `__EFMigrationsHistory_Bootstrap` trước guard trong migration, nên bắt buộc kiểm tra database trước khi gọi EF. Nếu đã có bất kỳ bảng nào, dừng lại và kiểm tra thay vì chạy lệnh.

Tại thư mục gốc dự án:

```powershell
dotnet restore tests/VietVan.DatabaseBootstrap/VietVan.DatabaseBootstrap.csproj
dotnet ef database update --project tests/VietVan.DatabaseBootstrap/VietVan.DatabaseBootstrap.csproj --startup-project tests/VietVan.DatabaseBootstrap/VietVan.DatabaseBootstrap.csproj --context FreshDatabaseContext
```

Factory bootstrap dùng Windows Authentication và giới hạn connection string ở `localhost\SQLEXPRESS` / `VietVan_New`. Nếu cần truyền lại cùng đích và các tùy chọn kết nối, biến môi trường `VIETVAN_BOOTSTRAP_CONNECTION` có thể được thiết lập trong PowerShell; không đưa mật khẩu vào source. Migration tạo bốn gói Free, Plus Monthly, Pro Monthly, Pro Yearly; không tạo tài khoản hoặc giao dịch giả. Không rollback baseline bằng EF: `Down` bị vô hiệu hóa.

## Khi gặp lỗi 51001

Không chạy lại migration hoặc xóa bảng ngay. Mở `scripts/Diagnose_VietVan_New.sql` trong SSMS, chọn `VietVan_New` và chạy toàn bộ script chỉ đọc. Gửi các result set về bảng, cột, index, khóa ngoại và migration history để xác định bảng nào chặn guard; thống kê số hàng chỉ mang tính gần đúng. Nếu `CanViewDatabaseDefinition` và `HasDatabaseControl` đều bằng 0, metadata có thể không đầy đủ và cần nhờ người có quyền phù hợp kiểm tra. Script đọc nội dung history nên cũng cần quyền SELECT trên các bảng history nếu chúng tồn tại.

Guard của migration cho phép duy nhất `dbo.__EFMigrationsHistory_Bootstrap`; mọi bảng khác đều gây lỗi 51001. Nếu chỉ có bảng bootstrap history, với schema đúng và không có bản ghi baseline, lệnh EF ở trên là hướng tiếp theo sau khi người có quyền đã kiểm tra. Script SSMS baseline khác: nó từ chối mọi bảng có sẵn, kể cả history. Nếu có bảng ứng dụng hoặc history khác, giữ nguyên và đánh giá schema/dữ liệu; cân nhắc database Development trống khác sau khi cấu hình và kiểm thử riêng. Không xóa bảng hoặc chỉnh lịch sử để vượt guard.

## Kiểm tra sau khi chạy

Trong SSMS, chọn `VietVan_New` và chạy:

```sql
SELECT DB_NAME() AS DatabaseName, SUSER_SNAME() AS SqlLogin;
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;
SELECT name FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.PaymentOrders') AND name IN (N'Status', N'ExpiresAt');
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory_Bootstrap;
SELECT Code, TierLevel, Price FROM dbo.SubscriptionPackages ORDER BY TierLevel, Price;
```

Có 14 bảng nghiệp vụ và một bảng history bootstrap. Sau đó chạy backend Development và kiểm tra Swagger, worker và API thực tế. Nếu lỗi, ghi lại lỗi; không chạy lại script lên schema đã tạo một phần nếu chưa điều tra.

## Khác biệt và giới hạn

Baseline dùng history riêng `__EFMigrationsHistory_Bootstrap`. Không chạy `dotnet ef database update` của project API trên database này: EF sẽ coi các migration cũ chưa chạy và có thể cố áp dụng lại. Cần thiết kế, kiểm thử việc hợp nhất lịch sử và các migration tiếp theo; không tự chèn history để che khác biệt schema.

Model hiện tại không có bảng Payments, Authors, Genres, Roles hay DramaSegments riêng: giao dịch dùng `PaymentOrders`, `PaymentBankTransactions`, `PaymentWebhookEvents`, `PaymentAuditLogs`, `RevenueTransactions`; role là cột trong `Users`. Bảng và constraint được scaffold từ model cùng một số constraint Payment/Drama trong migration. Một số SQL default, giới hạn độ dài và check constraint trong script lịch sử không có trong Fluent API, nên baseline không hoàn toàn giống các script cũ. Các script demo cũ có `USE VietVanDb` và giao dịch giả: không chạy trực tiếp trên database mới.
