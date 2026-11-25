# Vận hành media kịch

- File do Drama Studio tải lên nằm trong `PrivateMedia/drama-assets/`; metadata theo EF model hiện tại nằm trong `DramaAssets`. Sao lưu thư mục này cùng bản sao SQL Server nhất quán và kiểm tra khôi phục trên môi trường riêng. Không đưa thư mục này vào `wwwroot` hoặc public qua proxy.
- File chỉ được xóa sau khi API đã kiểm tra tham chiếu cảnh, preview và hotspot. Upload lỗi hoặc bị hủy được dọn khi tiến trình còn hoạt động; sự cố mất điện/tiến trình có thể để lại file mồ côi.
- Khi kiểm tra file mồ côi, so sánh đường dẫn tương đối trong `DramaAssets` với file dưới `PrivateMedia/drama-assets` trên bản sao lưu. Không tự động xóa: loại trừ upload đang chạy và file demo, kiểm tra bản sao lưu, chờ ít nhất 24 giờ rồi người vận hành xác nhận từng file trước khi xử lý.
- ASP.NET Core bind `IFormFile` có thể đệm nội dung vào file tạm. Bố trí dung lượng cho thư mục tạm và `PrivateMedia`, giới hạn upload tại reverse proxy, kiểm tra hạn mức đĩa và cấu hình backup. Mặc định mỗi instance giới hạn sáu lượt upload/phút theo người dùng; cần rate limit tập trung khi chạy nhiều instance.
- Kiểm tra malware bằng giải pháp phù hợp trước khi triển khai công khai. Kiểm tra chữ ký file không thay thế quét malware hoặc xác thực đầy đủ nội dung media.
- Nếu triển khai nhiều instance, dùng storage dùng chung với quyền hạn phù hợp hoặc thiết kế object storage riêng; file trên đĩa cục bộ không tự đồng bộ giữa các instance.
- File `VietVanNew.sql` trước đây mô tả `DramaMediaAssets` và `DramaPlaybackSegments`, khác EF model hiện tại (`DramaAssets`, `DramaScenes`). Không tự sửa/xóa hoặc chuyển URL cũ theo phỏng đoán. Đối chiếu schema và dữ liệu thực bằng `scripts/Diagnose_VietVan_New.sql` trước khi thiết kế chuyển đổi có kiểm soát.
