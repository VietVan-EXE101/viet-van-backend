namespace VietVan.API.Services.Implementations;

public sealed class WorkCoverStorage(IWebHostEnvironment environment, IFileStorageService? storage = null,
    Microsoft.Extensions.Options.IOptions<StorageOptions>? settings = null)
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

        if (settings?.Value.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase) == true && storage is not null)
        {
            var saved = await storage.SaveAsync(0, "Background", file, extension, MaxSize, ct, "free/thumbnails/works");
            return new SavedCover("r2:" + saved.StorageKey, "/api/media/work-covers/" + Path.GetFileName(saved.StorageKey));
        }

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

    public async Task DeleteAsync(string savedPath, CancellationToken ct = default)
    {
        if (savedPath.StartsWith("r2:", StringComparison.Ordinal))
        {
            if (storage is null) throw new InvalidOperationException("Storage chưa sẵn sàng.");
            await storage.DeleteAsync(savedPath[3..], ct);
            return;
        }
        var root = Path.GetFullPath(Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "work-covers"));
        var candidate = Path.GetFullPath(savedPath);
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Đường dẫn bìa không hợp lệ.");
        if (File.Exists(candidate)) File.Delete(candidate);
    }

    public Task DeleteByUrlAsync(string? url, CancellationToken ct = default)
    {
        if (url is null) return Task.CompletedTask;
        var remotePrefix = "/api/media/work-covers/";
        var localPrefix = "/uploads/work-covers/";
        var remote = url.StartsWith(remotePrefix, StringComparison.Ordinal);
        if (!remote && !url.StartsWith(localPrefix, StringComparison.Ordinal)) return Task.CompletedTask;
        var name = url[(remote ? remotePrefix.Length : localPrefix.Length)..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$"))
            throw new ArgumentException("Tên bìa không hợp lệ.");
        return DeleteAsync(remote ? "r2:free/thumbnails/works/0/" + name : Path.Combine(
            environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "work-covers", name), ct);
    }
}
