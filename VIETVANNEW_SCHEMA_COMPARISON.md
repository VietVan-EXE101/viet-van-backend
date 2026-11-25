# Đối chiếu source với VietVanNew.sql

Nguồn đối chiếu: file `C:\Users\HoangMinhHua\Downloads\VietVanNew.sql` do người dùng cung cấp và EF model hiện tại. File SQL tự mô tả là schema dựng lại, chưa được xác minh khớp model. Đây là đối chiếu với **nội dung file**, không phải snapshot trực tiếp của database đang chạy. Ví dụ, ảnh SSMS người dùng cung cấp sau đó đã xác nhận `dbo.Users.VipExpiresAt` tồn tại, dù file gốc không có cột này.

## Đăng ký và xác minh email

- `Users`: file gốc có Id, Username, Email, PasswordHash, Role, CreatedAt; không có `VipExpiresAt`. Ảnh SSMS sau đó cho thấy cột này đã được bổ sung.
- `UserSubscriptions`: file gốc thiếu `ActivationPaymentOrderId` và `GrantedTierLevel`; cả hai là nullable trong entity hiện tại. File `scripts/Fix_UserSubscriptions_PaymentColumns.sql` là script bổ sung tập trung và chỉ nên chạy sau khi kiểm tra database thực.
- File gốc không tạo `UserAuthStates`, `EmailVerificationTokens` hay `UserExternalLogins`; source hiện tại dùng các bảng này. Chưa có bằng chứng database live có hoặc không có các bảng đó. Nếu thiếu, `scripts/Upgrade_Auth_Runtime.sql` chỉ có thể được cân nhắc sau khi kiểm tra schema và backup; nó sẽ dừng nếu các bảng auth đã tồn tại, không tự hòa giải các bảng cũ.
- `SubscriptionPackages`: file gốc thiếu `Code`, `BillingPeriod`, `Description`, `Benefits`, `CreatedAt`, `UpdatedAt`. Đăng ký hiện dùng phép chiếu Id/DurationInDays/TierLevel, nhưng danh sách gói và PaymentService vẫn dùng các cột khác. File gốc có check `DurationInDays > 0`, trong khi baseline tier mới dùng `0` cho Free; không seed hoặc đổi giá/quyền hàng loạt khi chưa thiết kế migration phù hợp.

## Payment

Không coi SePay đã tương thích chỉ vì các bảng cùng tên. Trong file gốc, `PaymentOrders` dùng `PackageName`, `Price`, `DurationInDays`, `TierLevel`, còn entity dùng `PackageNameSnapshot`, `Amount`, `DurationDaysSnapshot`, `TierLevelSnapshot`; entity còn cần `BillingPeriodSnapshot`, `Currency`, `ReviewReason`. File gốc cho `IdempotencyKey` nullable, entity yêu cầu giá trị.

`PaymentBankTransactions` trong file gốc thiếu các trường entity `Currency`, `AccountNumberMasked`, `ClaimedOrderCode`, `ReviewReason`; có các cột khác như `AccountNumber`, `TransferContent` và `TransactionAt`. `PaymentWebhookEvents` trong file gốc không có `PayloadHash`/`Outcome`. `PaymentAuditLogs` dùng `Details` trong khi entity dùng `Detail`. Cần kiểm tra dữ liệu thực tế, các cột NOT NULL/DEFAULT, unique index và audit trước khi thiết kế thay đổi. Không tự rename, xóa cột, ghi Paid hoặc sao chép dữ liệu theo phỏng đoán.

## Drama

File gốc có `DramaMediaAssets`, `DramaCharacters`, `DramaPlaybackSegments`, `DramaHotspotTargetSegments`, `DramaProgress`. Code hiện tại dùng `DramaProductions`, `DramaAssets`, `DramaScenes`, `DramaHotspots`, `CulturalInsightViews`. Hai bảng `DramaScenes` và `DramaHotspots` trùng tên nhưng **khác cấu trúc**; không thể xem chúng là tương đương chỉ bằng `ToTable` hoặc tạo bảng đè. Cần quyết định mapping/migration và kiểm thử Player/Studio trên bản sao dữ liệu.

## Bước tiếp theo

1. Chạy `scripts/Diagnose_AuthRegister.sql` và `scripts/Diagnose_VietVan_New.sql` bằng SSMS trên `VietVan_New`, dùng tài khoản có đủ quyền xem metadata. Gửi các result set; script chỉ đọc, không xuất bản ghi tài khoản.
2. Đối chiếu các thay đổi đã thực hiện sau khi chạy file gốc. Sao lưu và kiểm thử trên bản sao trước khi áp dụng bất kỳ script nâng cấp nào.
3. Không chạy lại file khởi tạo `VietVanNew.sql` trên database đã có bảng, và không chạy `FreshBaseline` hoặc chèn migration history để che mismatch.
4. Không tuyên bố đăng ký, email, Drama hay SePay hoạt động cho tới khi kiểm thử trực tiếp sau khi schema đã được xác nhận.
