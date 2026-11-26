# Đối chiếu VietVan_New — kết quả Somee và phần schema cần bổ sung

## Xác nhận từ kiểm tra chỉ đọc ngày 26/11/2026

Output người dùng cung cấp xác nhận kết nối Somee database VietVan_New, SQL Server 15.0.2000.5, compatibility level 150. Kiểm tra 13 bảng đạt 10 bảng và lỗi 3 bảng:

| Bảng | Kết quả |
| --- | --- |
| DramaScenes | Thiếu AudioAssetId, AudioOffsetMs, BackgroundAssetId, EndMs, SceneKey, StartMs, Title, VideoAssetId |
| DramaHotspots | Thiếu DramaSceneId, EndMs, Height, IsEnabled, MeaningEn, MeaningVi, StartMs, TargetSegmentIdsJson, VisualAssetId, Width, X, Y, ZIndex |
| CulturalInsightViews | Không tìm thấy hoặc không nhìn thấy bảng, SQL error 208 |

Các projection LiteraryWorks, Users, UserAuthStates, UserSubscriptions, SubscriptionPackages, DramaProductions, DramaAssets, DramaManifests, DramaViewings và Reviews đều PASS. Đây là xác nhận tên bảng/cột/projection rỗng; chưa xác nhận kiểu dữ liệu, nội dung, quan hệ hoặc HTTP API trên Render.

Trạng thái này giải thích được lỗi đọc scene/hotspot legacy. Không chỉ cần bổ sung các cột preview; không được dùng baseline hoặc thêm các giá trị timeline/tọa độ/khóa tham chiếu giả để làm kiểm tra PASS. Cần metadata các cột hiện có, PK/FK/index/check và số hàng trước khi thiết kế mapping và chuyển đổi. `scripts/Diagnose_Drama_Schema.sql` đã mở rộng để lấy metadata toàn bộ bảng Drama và CulturalInsightViews, không sửa schema hoặc đọc nội dung bản ghi. Script đã kiểm tra cú pháp SQL Server 2019, chưa được agent chạy trên Somee.

CulturalInsightViews còn nằm trong model nhưng source hiện không truy cập DbSet này trong service nghiệp vụ; endpoint insight usage cũ trả dữ liệu quyền PRO cố định. Vì vậy lỗi bảng này cần được đối chiếu với thiết kế đã import, không mặc định coi đây là nguyên nhân của mọi lỗi kịch hoặc tự tạo bảng trùng chức năng.

Media là vấn đề độc lập: database giữ RelativePath, file thực nằm trong PrivateMedia; Docker loại thư mục này khỏi build. Local hiện có 95 file drama-assets (290826359 byte), chưa đối chiếu từng file với Somee. Cần khôi phục/chuyển storage theo khóa file hiện có và giữ ID asset; schema được sửa không tự đưa file lên Render.

## Ghi nhận trước khi có kết quả Somee

File `VietVan_New_Full_Standalone.sql` chưa được tìm thấy trong các lần tìm kiếm trước. Kết nối SQL Server local từ sandbox ở các lượt trước thất bại khi tạo SSPI context. Đây không phải bằng chứng SQL Server hoặc Windows Authentication của người dùng bị lỗi. Các ghi nhận dưới đây là lịch sử đối chiếu giữa source và *danh sách tên bảng trong yêu cầu*; kết quả Somee mới hơn ở đầu file là nguồn xác nhận cho các cột thiếu đã liệt kê, còn cấu trúc cột hiện có/FK/data mapping vẫn cần bổ sung.

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
