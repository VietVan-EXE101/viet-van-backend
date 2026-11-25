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

    [HttpGet("{dramaId:int}/assets/{assetId:int}/media")]
    public async Task<IActionResult> GetMedia(int dramaId, int assetId, CancellationToken ct)
    {
        var admin = User.IsInRole("Admin");
        var drama = await platform.FindAsync(dramaId, admin, ct);
        var asset = await db.DramaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId && a.DramaProductionId == dramaId, ct);
        if (asset is null) return NotFound();
        var cover = asset.Kind == "Background" && asset.Title == DramaService.CoverAssetTitle;
        var video = asset.Id == drama.FreeVideoAssetId;
        if (!admin && !cover && !video)
        {
            if (UserId is null) return Unauthorized();
            if (await entitlement.CurrentTierAsync(UserId.Value, ct) <= 0)
                return StatusCode(403, new { code = "SUBSCRIPTION_REQUIRED", message = "Cần PRO còn hiệu lực để truy cập tài nguyên tương tác." });
            if (!await platform.IsPublishedAssetAsync(dramaId, assetId, ct)) return NotFound();
        }
        var path = storage.ExistingPath(asset.RelativePath);
        if (path is null) return NotFound();
        if (video) await platform.RecordViewAsync(dramaId, UserId, "FREE_VIDEO", ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, asset.ContentType, enableRangeProcessing: true);
    }
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/dramas")]
public class AdminDramaMediaController(VietVanDbContext db, IFileStorageService storage) : ControllerBase
{
    [HttpGet("{dramaId:int}/assets/{assetId:int}/media")]
    public async Task<IActionResult> Get(int dramaId, int assetId, CancellationToken ct)
    {
        var asset = await db.DramaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId && a.DramaProductionId == dramaId, ct);
        var path = asset is null ? null : storage.ExistingPath(asset.RelativePath);
        if (path is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return PhysicalFile(path, asset!.ContentType, enableRangeProcessing: true);
    }

    [HttpPut("{dramaId:int}/preview"), HttpPut("{dramaId:int}/assets/{assetId:int}/preview"), HttpPut("{dramaId:int}/scenes/{sceneId:int}/preview-media")]
    [Obsolete("FREE dùng video đầy đủ, PRO dùng manifest.")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Retired() => StatusCode(410, new { message = "Preview đã ngừng sử dụng. Dùng video FREE hoặc manifest PRO." });
}
