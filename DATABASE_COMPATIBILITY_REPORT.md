# Đối chiếu VietVan_New — chưa có schema SQL đích

File `VietVan_New_Full_Standalone.sql` chưa được tìm thấy trong workspace, thư mục dự án cha hoặc các thư mục Desktop, Documents, Downloads được kiểm tra. Kết nối SQL Server từ sandbox ở các lượt trước thất bại khi tạo SSPI context. Đây không phải bằng chứng SQL Server hoặc Windows Authentication của người dùng bị lỗi. Không có dữ liệu schema live hoặc nội dung file đích, nên mọi kết luận dưới đây là đối chiếu giữa source và *danh sách tên bảng trong yêu cầu*, không phải xác nhận bảng/cột thực tế.

## Danh sách cần đối chiếu

| Nhóm | EF/source hiện tại | Tên trong yêu cầu | Trạng thái |
| --- | --- | --- | --- |
| Người dùng/tác phẩm | Users, LiteraryWorks | Users, LiteraryWorks | Trùng tên; cột, kiểu và quan hệ chưa xác minh |
| Subscription | SubscriptionPackages, UserSubscriptions, RevenueTransactions | Cùng tên | Trùng tên; cột, tier và ràng buộc chưa xác minh |
| Payment | PaymentOrders, PaymentBankTransactions, PaymentWebhookEvents, PaymentAuditLogs | Cùng tên | Trùng tên; snapshot, index, FK và kiểu dữ liệu chưa xác minh |
| Kịch | DramaProductions, DramaAssets, DramaScenes, DramaHotspots, CulturalInsightViews | DramaMediaAssets, DramaCharacters, DramaScenes, DramaPlaybackSegments, DramaHotspots, DramaHotspotTargetSegments, DramaProgress | Tên và mô hình khác nhau; không thể xác định mapping hoặc dữ liệu tương ứng chỉ từ tên |

Không thể phân loại bảng nào khớp hoàn toàn, thiếu/thừa cột, sai kiểu hay sai FK khi chưa có SQL hoặc schema live. Source hiện tại truy vấn trực tiếp các bảng và cột theo EF model cũ, bao gồm DramaProductions, DramaAssets, CulturalInsightViews, PreviewDurationMs và TargetSegmentIdsJson; nếu SQL đích không có đúng cấu trúc đó, các API tương ứng sẽ lỗi. Điều kiện đó chưa được xác minh.

## Migration

`20261123_AddPaymentModule` và `20261124_SubscriptionTiers` phụ thuộc schema tồn tại trước đó. `20261124071000_FreshBaseline` là baseline riêng cho database trống và dùng `__EFMigrationsHistory_Bootstrap`; không chạy nó lên database SQL đã tạo bảng. Không ghi hoặc chỉnh migration history để giả lập baseline. Sau khi nhận schema thực, cần đối chiếu đầy đủ và thiết kế migration additive riêng, kiểm thử trên bản sao trước khi áp dụng.

## Chức năng chưa xác minh

- Source hiện có đăng ký/đăng nhập JWT, role trong Users, Works, Drama cũ, subscription và SePay; các API này chưa được xác nhận chạy trên schema mới.
- Chưa có mapping EF cho DramaCharacters, DramaPlaybackSegments, DramaHotspotTargetSegments hoặc DramaProgress theo danh sách yêu cầu. Editor, phân công và duyệt xuất bản chưa được xác nhận là có cơ chế trong source.
- Worker và webhook chưa được kiểm thử SQL Server/SePay thực. Không suy luận thanh toán đã hoạt động chỉ từ unit test.
- `scripts/Diagnose_VietVan_New.sql` là truy vấn chỉ đọc để thu thập bảng, cột, index, FK, defaults, checks và history. Cần quyền xem metadata đầy đủ và quyền đọc history; không xóa hay thay đổi database.
