# Subscription Free / Plus / Pro

## Dữ liệu và tương thích

Migration `20261124_SubscriptionTiers` bổ sung gói FREE, PLUS_MONTHLY, PRO_MONTHLY, PRO_YEARLY và không sửa hoặc xóa các gói cũ. Tier 0 là Free, tier 1 là VIP cũ, tier 2 là Plus, tier 3 là Pro. VIP cũ tiếp tục xem toàn bộ và không bị giới hạn insight như trước; không tự động nhận các quyền Pro mới. Subscription có hiệu lực được xác định từ trạng thái và khoảng thời gian trong database; `Users.VipExpiresAt` chỉ phục vụ tương thích.

Chạy sau schema gốc, `scripts/DramaUpgrade.sql` và migration payment. Kiểm tra và sao lưu database trước khi triển khai; migration này chưa được chạy trên database thực. Migration phụ thuộc các bảng Drama và Payment đã tồn tại; dự án hiện không có baseline EF đầy đủ cho database mới. Không chạy `Down` tự động vì dữ liệu mới có thể đã phát sinh.

## Media preview

Free chỉ nhận cảnh nằm trọn trong giới hạn cấu hình tối đa 120 giây. Các file audio/video gốc mặc định bị chặn với Free, kể cả HTTP Range. Biên tập viên phải tự tạo file preview riêng, xác minh file không chứa dữ liệu vượt preview, tải lên, gọi `PUT /api/admin/dramas/{dramaId}/assets/{assetId}/preview` với `{"isPreview":true}`, sau đó gọi `PUT /api/admin/dramas/{dramaId}/scenes/{sceneId}/preview-media` với `{"audioAssetId":...,"videoAssetId":...}`. Có thể dùng null cho loại không có. `PUT /api/admin/dramas/{dramaId}/preview` nhận `{"previewDurationMs":120000}`. Việc đánh dấu không tự kiểm tra thời lượng hay mã hóa file: đánh dấu nhầm file đầy đủ sẽ làm lộ nội dung. Không đánh dấu file gốc; cần quy trình kiểm duyệt media trước vận hành.

Các cảnh băng qua giới hạn preview bị ẩn hoàn toàn; cần tách cảnh nếu muốn hiển thị một phần. Tài nguyên preview chưa được tạo tự động.

## Quyền và thanh toán

`GET /api/subscriptions/me/entitlements` trả chính sách hiện hành. `GET /api/dramas/{id}/insights/usage` trả hạn mức đã dùng; mở insight yêu cầu đăng nhập. Lượt mở khác nhau được lưu trên SQL Server, khóa theo user và drama bằng application lock. Giao dịch và khóa này cần được kiểm thử đồng thời trên SQL Server thực.

Đơn lưu giá, tier, thời hạn và chu kỳ tại thời điểm tạo. Gói mới bắt đầu sau thời điểm kết thúc muộn nhất của các subscription trả phí đã xếp lịch; cùng cấp cũng được gia hạn theo cách này. Do đó việc nâng hoặc hạ cấp không có hiệu lực tức thời. Đơn cũ không có snapshot chu kỳ tiếp tục dùng số ngày cũ. Webhook vẫn dùng kiểm tra và cơ chế chống trùng hiện tại; chưa xác nhận với SePay thực.

## Chưa triển khai

Source chưa có dịch vụ shadowing, virtual ticket booth, dịch tự động hoặc xuất flashcard. Entitlement chỉ mô tả chính sách dự kiến; chưa có tính năng tương ứng hoặc API thực để áp dụng. Chưa kiểm thử SQL Server, các request đồng thời, Range thực tế, migration, luồng SePay hoặc trình duyệt đầu cuối. Chưa xác nhận các tiêu chí bảo mật và tương thích để đưa production.
