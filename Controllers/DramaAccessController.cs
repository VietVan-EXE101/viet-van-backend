using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;

namespace VietVan.API.Controllers;

// PrivateMedia hoặc Storage:RootPath phải nằm ngoài wwwroot.
[ApiController, Route("api/dramas")]
public class DramaMediaController(VietVanDbContext db, EntitlementService entitlement,
    DramaPlatformService platform, IFileStorageService storage) : ControllerBase
{
    private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("{dramaId:int}/video")]
    public async Task<IActionResult> Video(int dramaId, CancellationToken ct)
    {
        var drama = await platform.FindAsync(dramaId, User.IsInRole("Admin"), ct);
        if (drama.FreeVideoAssetId is not int assetId) return NotFound();
        return await GetMedia(dramaId, assetId, ct);
    }

    [HttpGet("{dramaId:int}/video/access")]
    public async Task<IActionResult> VideoAccess(int dramaId, CancellationToken ct)
    {
        var drama = await platform.FindAsync(dramaId, User.IsInRole("Admin"), ct);
        return drama.FreeVideoAssetId is int assetId ? await Serve(dramaId, assetId, true, ct) : NotFound();
    }

    [HttpGet("{dramaId:int}/assets/{assetId:int}/access")]
    public Task<IActionResult> Access(int dramaId, int assetId, CancellationToken ct) => Serve(dramaId, assetId, true, ct);

    [HttpGet("{dramaId:int}/assets/{assetId:int}/media")]
    public Task<IActionResult> GetMedia(int dramaId, int assetId, CancellationToken ct) => Serve(dramaId, assetId, false, ct);

    private async Task<IActionResult> Serve(int dramaId, int assetId, bool json, CancellationToken ct)
    {
        var admin = User.IsInRole("Admin");
        var drama = await platform.FindAsync(dramaId, admin, ct);
        var asset = await db.DramaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId && a.DramaProductionId == dramaId, ct);
        if (asset is null) return NotFound();
        var cover = asset.Kind == "Background" && asset.Title == DramaService.CoverAssetTitle;
        var video = asset.Id == drama.FreeVideoAssetId;
        DateTimeOffset? notAfter = null;
        if (!admin && !cover && !video)
        {
            if (UserId is null) return Unauthorized();
            if (await entitlement.CurrentTierAsync(UserId.Value, ct) <= 0)
                return StatusCode(403, new { code = "SUBSCRIPTION_REQUIRED", message = "Cần PRO còn hiệu lực để truy cập tài nguyên tương tác." });
            if (!await platform.IsPublishedAssetAsync(dramaId, assetId, ct)) return NotFound();
            var until = await entitlement.VipUntilAsync(UserId.Value, ct);
            if (until is null || until <= DateTime.UtcNow) return Forbid();
            notAfter = new DateTimeOffset(DateTime.SpecifyKind(until.Value, DateTimeKind.Utc));
        }
        var access = await storage.GetAccessAsync(asset.RelativePath, ct, notAfter, !cover && !video);
        if (access is null) return NotFound();
        if (video) await platform.RecordViewAsync(dramaId, UserId, "FREE_VIDEO", ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (json) return Ok(new { url = access.Url ?? $"/api/dramas/{dramaId}/assets/{assetId}/media",
            access.ExpiresAt, asset.ContentType, asset.SizeBytes });
        // Giữ URL API cũ; R2 trả redirect để video/audio không đi qua RAM/băng thông Backend.
        return access.Url is string url ? Redirect(url) : PhysicalFile(access.LocalPath!, asset.ContentType, enableRangeProcessing: true);
    }
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/dramas")]
public class AdminDramaMediaController(VietVanDbContext db, IFileStorageService storage,
    ILogger<AdminDramaMediaController>? logger = null) : ControllerBase
{
    [HttpGet("{dramaId:int}/assets/{assetId:int}/media")]
    public Task<IActionResult> Get(int dramaId, int assetId, CancellationToken ct) => Serve(dramaId, assetId, false, ct);

    [HttpGet("{dramaId:int}/assets/{assetId:int}/access")]
    public Task<IActionResult> Access(int dramaId, int assetId, CancellationToken ct) => Serve(dramaId, assetId, true, ct);

    private async Task<IActionResult> Serve(int dramaId, int assetId, bool json, CancellationToken ct)
    {
        var asset = await db.DramaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId && a.DramaProductionId == dramaId, ct);
        if (asset is null)
        {
            logger?.LogWarning("Admin media 404 Reason=METADATA_NOT_FOUND DramaId={DramaId} AssetId={AssetId} TraceId={TraceId}",
                dramaId, assetId, HttpContext.TraceIdentifier);
            return NotFound();
        }
        var access = await storage.GetAccessAsync(asset.RelativePath, ct, protectedAccess: true);
        if (access is null)
        {
            // Log ID/provider/trace để chẩn đoán; không in key hoặc signed URL.
            logger?.LogWarning("Admin media 404 Reason=FILE_NOT_FOUND DramaId={DramaId} AssetId={AssetId} StorageType={StorageType} TraceId={TraceId}",
                dramaId, assetId, storage.GetType().Name, HttpContext.TraceIdentifier);
            return NotFound();
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (json) return Ok(new { url = access.Url ?? $"/api/admin/dramas/{dramaId}/assets/{assetId}/media",
            access.ExpiresAt, asset!.ContentType, asset.SizeBytes });
        return access.Url is string url ? Redirect(url) : PhysicalFile(access.LocalPath!, asset!.ContentType, enableRangeProcessing: true);
    }

    [HttpPut("{dramaId:int}/preview"), HttpPut("{dramaId:int}/assets/{assetId:int}/preview"), HttpPut("{dramaId:int}/scenes/{sceneId:int}/preview-media")]
    [Obsolete("FREE dùng video đầy đủ, PRO dùng manifest.")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Retired() => StatusCode(410, new { message = "Preview đã ngừng sử dụng. Dùng video FREE hoặc manifest PRO." });
}
