namespace VietVan.API.Services.Implementations;

public sealed class WorkCoverStorage(IWebHostEnvironment environment)
{
    public const long MaxSize = 5 * 1024 * 1024;
    public record SavedCover(string PhysicalPath, string Url);

    public async Task<SavedCover> SaveAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > MaxSize)
            throw new ArgumentException("Ảnh bìa phải có dung lượng từ 1 byte đến 5 MiB.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expected = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (expected is null || !string.Equals(file.ContentType, expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP với loại nội dung phù hợp.");

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var count = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => count >= 3 && header.AsSpan(0, 3).SequenceEqual(new byte[] { 0xff, 0xd8, 0xff }),
            ".png" => count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".webp" => count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new ArgumentException("Nội dung ảnh không khớp định dạng được khai báo.");

        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "work-covers");
        Directory.CreateDirectory(directory);
        var name = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(directory, name);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(header.AsMemory(0, count), ct);
            await input.CopyToAsync(output, ct);
            return new SavedCover(path, $"/uploads/work-covers/{name}");
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}
