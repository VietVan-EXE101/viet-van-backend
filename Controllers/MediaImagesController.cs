using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Filters;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Controllers;

[ApiController]
public sealed class MediaImagesController(VietVanDbContext db, IFileStorageService storage,
    ILogger<MediaImagesController> logger) : ControllerBase
{
    [AllowAnonymous, HttpGet("api/media/work-covers/{fileName}")]
    public async Task<IActionResult> WorkCover(string fileName, CancellationToken ct)
    {
        if (!Regex.IsMatch(fileName, @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$")) return NotFound();
        var apiUrl = "/api/media/work-covers/" + fileName;
        if (!await db.LiteraryWorks.AsNoTracking().AnyAsync(w => w.IsActive && w.CoverImageUrl == apiUrl, ct)) return NotFound();
        var access = await storage.GetAccessAsync("free/thumbnails/works/0/" + fileName, ct);
        Response.Headers.CacheControl = "private, no-store";
        return access?.Url is string url ? Redirect(url) : NotFound();
    }

    [Authorize, HttpGet("api/users/me/avatar")]
    public async Task<IActionResult> Avatar(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var avatar = await db.Set<UserAvatar>().AsNoTracking().SingleOrDefaultAsync(a => a.UserId == id, ct);
        if (avatar is null) return NotFound();
        var access = await storage.GetAccessAsync(avatar.ObjectKey, ct, protectedAccess: true);
        if (access is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new { url = access.Url ?? "/api/users/me/avatar/media", access.ExpiresAt,
            avatar.FileName, avatar.ContentType, avatar.FileSize });
    }

    [Authorize, HttpGet("api/users/me/avatar/media")]
    public async Task<IActionResult> AvatarMedia(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var avatar = await db.Set<UserAvatar>().AsNoTracking().SingleOrDefaultAsync(a => a.UserId == id, ct);
        if (avatar is null) return NotFound();
        var access = await storage.GetAccessAsync(avatar.ObjectKey, ct, protectedAccess: true);
        if (access is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return access.Url is string url ? Redirect(url) : PhysicalFile(access.LocalPath!, avatar.ContentType);
    }

    [Authorize, HttpPost("api/users/me/avatar"), Consumes("multipart/form-data"), UploadRequest]
    [EnableRateLimiting("drama-upload")]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> SaveAvatar(IFormFile file, CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) throw new ArgumentException("Avatar chỉ hỗ trợ JPG, PNG, WebP.");
        DramaService.ValidateUpload(file, "Background", ext);
        // Đọc metadata trước upload: thiếu bảng avatar không tạo object mồ côi.
        var avatar = await db.Set<UserAvatar>().SingleOrDefaultAsync(a => a.UserId == id, ct);
        var oldKey = avatar?.ObjectKey;
        var saved = await storage.SaveAsync(id, "Background", file, ext, WorkCoverStorage.MaxSize, ct, "users/avatars");
        try
        {
            if (avatar is null) { avatar = new UserAvatar { UserId = id }; db.Add(avatar); }
            avatar.ObjectKey = saved.StorageKey; avatar.FileName = saved.OriginalName;
            avatar.ContentType = DramaService.MediaTypes[ext].Mime; avatar.FileSize = saved.SizeBytes;
            avatar.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch { await storage.DeleteAsync(saved.StorageKey); throw; }
        if (oldKey is not null) await Cleanup(oldKey);
        return Ok(new { avatar.FileName, avatar.ContentType, avatar.FileSize, url = "/api/users/me/avatar" });
    }

    [Authorize, HttpDelete("api/users/me/avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var avatar = await db.Set<UserAvatar>().SingleOrDefaultAsync(a => a.UserId == id, ct);
        if (avatar is null) return NoContent();
        db.Remove(avatar); await db.SaveChangesAsync(ct);
        await Cleanup(avatar.ObjectKey);
        return NoContent();
    }

    private async Task Cleanup(string key)
    {
        try { await storage.DeleteAsync(key); }
        catch (Exception ex) { logger.LogWarning("Không dọn được avatar cũ. ErrorType={ErrorType}", ex.GetType().Name); }
    }
}
