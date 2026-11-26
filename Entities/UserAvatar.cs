namespace VietVan.API.Entities;

// Tách metadata avatar để không thêm cột bắt buộc vào truy vấn Users/đăng nhập hiện có.
public sealed class UserAvatar
{
    public int UserId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
