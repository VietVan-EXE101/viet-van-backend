using System.Security.Cryptography;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Services.Storage;

// Chỉ file tạm để kiểm tra signature/hash trước PUT; luôn dọn sau upload hoặc lỗi.
public sealed class MediaUploadStage : IAsyncDisposable
{
    public string Path { get; }
    public string Sha256 { get; private set; } = "";
    public long Size { get; private set; }
    private MediaUploadStage(string path) => Path = path;

    public static async Task<MediaUploadStage> CreateAsync(IFormFile file, string extension, long limit, CancellationToken ct)
    {
        if (file.Length <= 0) throw new ArgumentException("File rỗng.");
        if (file.Length > limit) throw new UploadException(413, "File vượt kích thước cho phép.");
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vietvan-uploads");
        Directory.CreateDirectory(folder);
        var result = new MediaUploadStage(System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + extension));
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(result.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    result.Size += count;
                    if (result.Size > limit || result.Size > file.Length) throw new UploadException(413, "File vượt kích thước khai báo.");
                    digest.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            if (result.Size != file.Length || !DramaService.HasExpectedSignature(result.Path, extension))
                throw new UploadException(415, "File không đầy đủ hoặc chữ ký file không hợp lệ.");
            result.Sha256 = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(Path)) File.Delete(Path);
        return ValueTask.CompletedTask;
    }
}
