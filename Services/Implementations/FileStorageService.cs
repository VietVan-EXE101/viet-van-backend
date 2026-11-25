using Microsoft.Extensions.Options;

namespace VietVan.API.Services.Implementations;

public sealed class StorageOptions
{
    public string RootPath { get; set; } = "PrivateMedia";
}

public sealed record StoredFile(string StorageKey, string OriginalName, long SizeBytes);

public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(int dramaId, string kind, IFormFile file, string extension, long limit, CancellationToken ct);
    string? ExistingPath(string storageKey);
    void Delete(string storageKey);
}

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string root;
    public LocalFileStorageService(IWebHostEnvironment environment, IOptions<StorageOptions> options)
    {
        root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        var web = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(web, StringComparison.OrdinalIgnoreCase) || root.StartsWith(web + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage:RootPath phải nằm ngoài wwwroot.");
    }

    public string? ExistingPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey) || storageKey.Contains(':')) return null;
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    public async Task<StoredFile> SaveAsync(int dramaId, string kind, IFormFile file, string extension, long limit, CancellationToken ct)
    {
        // Các thành phần đường dẫn do server sinh, không nhận storageKey từ client.
        var key = $"drama-assets/{dramaId}/{kind.ToLowerInvariant()}/{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = file.OpenReadStream())
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > limit || total > file.Length) throw new UploadException(413, "File vượt kích thước cho phép.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (total == 0 || total != file.Length) throw new ArgumentException("File rỗng hoặc không đầy đủ.");
            }
            if (!DramaService.HasExpectedSignature(path, extension)) throw new UploadException(415, "Chữ ký file không hợp lệ.");
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).ToArray());
            return new StoredFile(key, name.Length <= 260 ? name : name[^260..], file.Length);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    public void Delete(string storageKey)
    {
        var path = ExistingPath(storageKey);
        if (path is not null) File.Delete(path);
    }
}

public sealed class UploadException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
