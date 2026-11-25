using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;

namespace VietVan.API.Controllers;

[ApiController, Route("api/dramas")]
public class DramaController(DramaService legacy, DramaPlatformService platform, EntitlementService entitlements) : ControllerBase
{
    private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool Admin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await legacy.ListAsync());

    [HttpGet("{dramaId:int}")]
    public async Task<IActionResult> Detail(int dramaId, CancellationToken ct) =>
        Ok(await platform.DetailAsync(dramaId, UserId, Admin || (UserId.HasValue && await entitlements.CurrentTierAsync(UserId.Value, ct) > 0), Admin, ct));

    [Authorize(Policy = "Pro"), HttpGet("{dramaId:int}/interactive")]
    public async Task<IActionResult> Interactive(int dramaId, CancellationToken ct, [FromQuery] bool preview = false)
    {
        if (preview && !Admin) return Forbid();
        return Ok(await platform.InteractiveAsync(dramaId, UserId, Admin, ct, preview));
    }

    // Adapter chỉ đọc snapshot cũ đã xuất bản và luôn kiểm tra PRO.
    [Authorize(Policy = "Pro"), HttpGet("{dramaId:int}/scenes/{sceneId:int}")]
    [Obsolete("Dùng API interactive.")]
    public async Task<IActionResult> Scene(int dramaId, int sceneId, CancellationToken ct)
    {
        var snapshot = await platform.LegacySnapshotAsync(dramaId, Admin, ct);
        var scene = snapshot.Scenes.FirstOrDefault(x => x.Id == sceneId);
        return scene is null ? NotFound() : Ok(scene);
    }

    [Authorize(Policy = "Pro"), HttpGet("{dramaId:int}/hotspots/{hotspotId:int}")]
    [Obsolete("Dùng manifest trong API interactive.")]
    public async Task<IActionResult> Hotspot(int dramaId, int hotspotId, CancellationToken ct)
    {
        var snapshot = await platform.LegacySnapshotAsync(dramaId, Admin, ct);
        var hotspot = snapshot.Scenes.SelectMany(x => x.Hotspots).FirstOrDefault(x => x.Id == hotspotId && x.IsEnabled);
        return hotspot is null ? NotFound() : Ok(hotspot);
    }

    [Authorize(Policy = "Pro"), HttpGet("{dramaId:int}/insights/usage")]
    [Obsolete("PRO không giới hạn lượt tương tác.")]
    public async Task<IActionResult> InsightUsage(int dramaId, CancellationToken ct)
    {
        await platform.FindAsync(dramaId, Admin, ct);
        return Ok(new { used = 0, limit = (int?)null, remaining = (int?)null });
    }
}
