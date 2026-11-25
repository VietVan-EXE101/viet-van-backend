# Báo cáo nghiệp vụ Drama — 25/11/2026

## Trước và sau

- Trước: endpoint kịch cho phép truy cập preview VIP mà không kiểm tra VIP nhất quán; kịch Public/Free cũng bị cắt theo thời lượng preview khi người xem không có gói trả phí. Kịch liên kết với tác phẩm bị ẩn khi tác phẩm không hoạt động.
- Sau trong source: Public cho guest; FreeAuthenticated cần đăng nhập; VIP cần subscription đang hiệu lực có tier lớn hơn 0. Detail, scene, hotspot và media cùng kiểm tra mức truy cập. Nội dung Public/FreeAuthenticated được truy cập đầy đủ sau khi đạt điều kiện. Kịch đã xuất bản vẫn hiển thị khi tác phẩm gốc bị ẩn. Danh sách chỉ trả kịch đã xuất bản.
- Hotspot Public cho phép guest sử dụng; tài khoản đăng nhập vẫn chịu hạn mức cultural insight đã có theo gói. API hotspot vẫn trả nội dung và target khi được gọi; detail không lộ toàn bộ ý nghĩa trước khi tương tác.
- `LiteraryWorkId` vẫn tùy chọn để giữ dữ liệu kịch độc lập và tương thích với yêu cầu trước của người dùng; khi cung cấp ID, backend kiểm tra tác phẩm tồn tại. Không ép gắn lại kịch cũ.

## API và giao diện

- Giữ AdminWorks, AdminDramas, Drama, Reviews, Auth, Subscription, Payment và các contract cũ. Không xóa `GET /api/works/{id}/content` hoặc các route review work do chưa chứng minh mọi client đã ngừng dùng; chúng không thuộc luồng chính theo nghiệp vụ mới.
- Thay đổi hành vi `GET /api/dramas/{id}`, `/scenes/{sceneId}`, `/hotspots/{hotspotId}` và `/assets/{assetId}/media` để áp dụng AccessLevel. Endpoint media kiểm tra quyền trước khi mở file, kể cả ảnh bìa; liên kết trực tiếp không bypass VIP. Với người chưa đăng nhập trả 401, đã đăng nhập thiếu quyền trả 403.
- `GET /api/dramas` và review drama không còn phụ thuộc `LiteraryWork.IsActive`. Trạng thái Published vẫn được kiểm tra.
- Trong `wwwroot` có Drama Player và giao diện hỗ trợ, không tìm thấy màn đọc truyện hoặc nút Đọc ngay. Không tìm thấy source frontend React hiển thị trong ảnh người dùng trong workspace, nên chưa thể kiểm tra/sửa frontend đó. Drama Player có widget review drama từ thay đổi trước.
- `TargetSegmentIds` hiện được kiểm tra là ID **scene** thuộc cùng drama; frontend điều hướng tới scene. Chưa có mô hình/API playback segment hay subtitle/lời thoại trong source hiện tại. Không tuyên bố các chức năng này hoàn tất.

## File sửa trong lượt này

`Controllers/DramaController.cs`, `Controllers/DramaAccessController.cs`, `Controllers/ReviewsController.cs`, `Services/Implementations/DramaService.cs`, `Services/Payments/EntitlementService.cs`, `tests/VietVan.Payment.Tests/EntitlementPolicyTests.cs` và báo cáo này. Không sửa frontend trong lượt này.

## Database và giới hạn

Không thay đổi schema hay dữ liệu và không chạy migration. Không thể kiểm tra database live trong sandbox; yêu cầu truy vấn SQL ngoài sandbox trước đó bị từ chối. Lỗi SQL 207 do `DramaScenes` không khớp model EF đã được người dùng cung cấp vẫn chưa được xử lý. Các thay đổi quyền vì thế chưa chứng minh API chạy được trên VietVan_New. `scripts/Diagnose_Drama_Schema.sql` và `scripts/Diagnose_VietVan_New.sql` là script chỉ đọc để thu thập schema và migration history. Không chạy FreshBaseline trên dữ liệu hiện có.

## Kết quả kiểm thử

- `dotnet restore VietVan.API.csproj --ignore-failed-sources`: thành công.
- `dotnet build VietVan.API.csproj --no-restore -o artifacts/drama-business-build`: thành công, 0 lỗi, 0 cảnh báo.
- `dotnet test tests/VietVan.Payment.Tests/VietVan.Payment.Tests.csproj --no-restore -o artifacts/drama-business-tests`: 79 đạt, 0 thất bại; gồm test bảng quyết định Public/FreeAuthenticated/VIP và kiểm tra tier. Đây là unit test, không chứng minh kiểm thử HTTP, database, media hay payment live.
- Chưa kiểm thử guest/Free/VIP qua HTTP, upload, review, branch, payment hoặc frontend end-to-end; cần database test độc lập tương thích và account/test double phù hợp.

## Kiểm tra Swagger sau khi triển khai

Sau khi đồng bộ schema an toàn và khởi động lại API: tạo kịch và scene thử trong database test, publish; gọi detail, scene, hotspot và media cho từng mức bằng guest, Free và VIP. Public cho guest; FreeAuthenticated trả 401 cho guest; VIP trả 401 cho guest, 403 cho Free và cho VIP hợp lệ truy cập. Kiểm tra trực tiếp media URL và review, không dùng database thật để xóa dữ liệu hoặc tạo giao dịch thanh toán thật.
