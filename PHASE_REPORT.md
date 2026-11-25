# Báo cáo 7 giai đoạn — Module Payment & Subscription Việt Văn

Ngày kiểm tra source: **23/11/2026**. Nguồn được mở: `VietVan.API(1).zip`, `VietVan_Interactive_Drama_Upgrade.zip`. Nền sử dụng để chỉnh sửa: **bản nâng cấp Drama**, tránh viết lại Auth và Works hiện có. Đối chiếu tài liệu webhook SePay chính thức tại `https://docs.sepay.vn/tich-hop-webhooks.html`.

**QUAN TRỌNG:** Không có .NET SDK, SQL Server hoặc cấu hình SePay/Ngân hàng trong môi trường tạo source. **Chưa thể xác nhận** `dotnet build`, migration database thực tế, test concurrency bằng DB thật, webhook provider hay một giao dịch chuyển khoản thật. Việc triển khai mới dừng ở mức code + kiểm tra tĩnh; chưa đủ điều kiện được coi là production-ready.

## Giai đoạn 1 — Kiểm tra source
- **Đã thực hiện:** Đọc `Entities/Models.cs`, `Data/VietVanDbContext.cs`, `Program.cs`, `Controllers`, `Services`, `VietVanDb.sql`, scripts Drama; xác minh `RevenueTransactions` (không phải bảng Payments), `SubscriptionPackages`, `UserSubscriptions`, Auth JWT/Role, và đường dẫn Drama. Không có SePay/payment order trong source được chọn.
- **File thay đổi:** chưa có trong giai đoạn đánh giá.
- **API:** không thay đổi.
- **Kiểm thử:** kiểm tra nội dung ZIP, source và SQL; không mở được SQL Server đang chạy của máy người dùng.
- **Rủi ro:** đây là bản sao được gửi, DB người dùng triển khai có thể đã đổi schema.

## Giai đoạn 2 — Thiết kế DB / Gateway
- **Đã thêm:** `Entities/PaymentModels.cs`, `DTOs/Payments/PaymentDtos.cs`, `Services/Payments/PaymentOptions.cs`, `VietQrGateway.cs`, `SePayWebhookParser.cs`, `scripts/PaymentUpgrade.sql`, `Data/Migrations/AddPaymentModule.cs`.
- **Đã sửa:** `Entities/Models.cs`, `Data/VietVanDbContext.cs` (bổ sung các cột và cấu hình EF additive).
- **API:** định nghĩa chuẩn REST trong README_PAYMENT.md.
- **Test:** kiểm tra tĩnh đủ bảng, FK, unique `(UserId, IdempotencyKey)` và `(Provider, ProviderTransactionId)`; chưa chạy SQL migration.
- **Rủi ro:** cần review schema thật; migration mới không có baseline/snapshot hoàn chỉnh từ dự án gốc.

## Giai đoạn 3 — Backend / SePay webhook
- **Đã thêm:** `Services/Payments/PaymentService.cs`, `EntitlementService.cs`, `PaymentExpiryWorker.cs`, `Controllers/PaymentController.cs`.
- **Đã sửa:** `Program.cs`, `appsettings.json` (chỉ placeholder).
- **API mới:** packages, subscriptions/me, orders, history, cancel, webhook SePay, admin payments + reconciliation + packages.
- **An toàn được thiết kế:** JWT customer ownership, Admin Role, snapshot amount, API key constant-time comparison, account check, unique provider ID, SQL `sp_getapplock`, SQL transaction và audit.
- **Test:** test parser được viết nhưng chưa chạy vì thiếu .NET; webhook và concurrent transaction chưa chạy.
- **Rủi ro:** chỉ hỗ trợ SePay webhook `Api_Key` JSON; chưa triển khai HMAC, retry/catch-up từ Provider API, refund thực tế hoặc manual settlement.

## Giai đoạn 4 — Frontend
- **Đã thêm:** `wwwroot/vip.html`, `wwwroot/admin-payments.html`.
- **Đã sửa:** `wwwroot/drama-studio.html`, `wwwroot/drama-viewer.html`.
- **Chức năng:** mua gói, xác nhận, QR, countdown, refresh/poll, trạng thái/review, lịch sử, subscription, danh sách đơn Admin, gói và kiểm tra giao dịch.
- **Test:** chạy `node --check` cho toàn bộ 4 đoạn JavaScript: **PASS**; chưa test trình duyệt end-to-end.
- **Rủi ro:** FE demo dùng sessionStorage, cần tích hợp UI và error handling vào FE production thật.

## Giai đoạn 5 — Phân quyền VIP với nội dung
- **Đã sửa:** `Entities/DramaModels.cs`, `DTOs/Drama/DramaDtos.cs`, `Services/Implementations/DramaService.cs`, `Controllers/DramaController.cs`, `Controllers/WorksController.cs`, `Services/Implementations/AuthService.cs`, `Program.cs`.
- **Đã thêm:** `Controllers/DramaAccessController.cs` (media API có kiểm tra quyền).
- **Đã chuyển:** `PrivateMedia/drama-assets/demo/*` từ `wwwroot/drama-assets/demo/*`.
- **API:** Giữ nguyên route Drama cũ nhưng bổ sung access check, thêm API nội dung Works và media protected.
- **Test:** đã rà soát đường dẫn static và middleware; chưa có browser/SQL test.
- **Rủi ro:** URL media cũ thay đổi có chủ đích, FE khác cần sửa để lấy URL mới; Viewer dùng blob để xem media VIP, chưa tối ưu streaming video lớn.

## Giai đoạn 6 — Build, migration, test
- **Đã thêm:** `tests/VietVan.Payment.Tests` (test JSON/parser), `scripts/VerifyPaymentSchema.sql`, checklist thủ công trong `README_PAYMENT.md`.
- **Kiểm tra đã chạy:** cú pháp JavaScript 4/4 PASS; JSON cấu hình hợp lệ; kiểm tra static cấu trúc SQL/API. Không phát hiện khóa SePay thật trong source.
- **Chưa chạy:** dotnet restore/build/test, ef database update, SQL Server constraints, concurrent webhook, SePay send test, giao dịch thật. Không tuyên bố pass cho các test này.
- **Rủi ro:** cần sửa lỗi build nếu phát sinh tại môi trường có .NET SDK, chạy cả regression test Auth/Works/Drama.

## Giai đoạn 7 — Tài liệu vận hành
- **Đã thêm:** `README_PAYMENT.md`, `PHASE_REPORT.md`.
- **Đã sửa:** `README_DRAMA.md`, cấu hình `appsettings.json`.
- **Nội dung:** PowerShell env vars, cấu hình SePay/API Key, kết nối SQL Server, nâng cấp private media, chạy API, checklist real-payment.
- **Rủi ro tồn tại:** cần HTTPS + hardening reverse proxy + secure secret store + bank statement reconciliation + monitoring alert + backup/restore rehearsal + merchant acceptance testing trước go-live.
