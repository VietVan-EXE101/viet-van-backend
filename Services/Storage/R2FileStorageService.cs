using Microsoft.Extensions.Options;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Services.Storage;

public sealed class R2FileStorageService(IR2ObjectClient objects, IOptions<R2Options> options) : IFileStorageService
{
    public static string Prefix(string kind, bool cover = false, bool freeVideo = false) =>
        cover ? "free/thumbnails" : freeVideo ? "free/videos" : kind switch
        {
            "Character" => "pro/characters", "Background" => "pro/backgrounds",
            "Audio" or "Music" => "pro/audio", _ => "pro/assets"
        };

    public async Task<StoredFile> SaveAsync(int dramaId, string kind, IFormFile file, string extension, long limit,
        CancellationToken ct, string? prefix = null)
    {
        DramaService.ValidateUpload(file, kind, extension);
        await using var stage = await MediaUploadStage.CreateAsync(file, extension, limit, ct);
        var key = R2Options.Key($"{prefix ?? Prefix(kind)}/{dramaId}/{Guid.NewGuid():N}{extension}");
        // Ghi byte gốc, không resize/chuyển mã nên GIF giữ frame, timing và loop.
        var mime = DramaService.MediaTypes[extension].Mime;
        if (!await objects.PutFileAsync(key, stage.Path, mime, stage.Sha256, ct))
            throw new IOException("Object key đã tồn tại; file cũ được giữ nguyên.");
        var name = System.IO.Path.GetFileName(file.FileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return new(key, name.Length <= 260 ? name : name[^260..], stage.Size);
    }

    public async Task<MediaAccess?> GetAccessAsync(string storageKey, CancellationToken ct,
        DateTimeOffset? notAfter = null, bool protectedAccess = false)
    {
        if (await objects.HeadAsync(storageKey, ct) is null) return null;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(protectedAccess ? options.Value.ProtectedUrlMinutes : options.Value.PublicUrlMinutes);
        if (notAfter.HasValue && notAfter.Value < expires) expires = notAfter.Value;
        if (expires <= now) return null;
        return new(null, await objects.PresignGetAsync(storageKey, expires), expires);
    }

    public string? ExistingPath(string storageKey) => null;
    public void Delete(string storageKey) => throw new NotSupportedException("R2 dùng DeleteAsync.");
    public Task DeleteAsync(string storageKey, CancellationToken ct = default) => objects.DeleteAsync(storageKey, ct);
}
