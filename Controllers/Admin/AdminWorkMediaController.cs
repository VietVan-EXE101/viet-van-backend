using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Filters;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Controllers.Admin;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/works")]
public sealed class AdminWorkMediaController(VietVanDbContext db, WorkCoverStorage covers,
    ILogger<AdminWorkMediaController> logger) : ControllerBase
{
    [HttpPost("{id:int}/cover"), Consumes("multipart/form-data"), UploadRequest, EnableRateLimiting("drama-upload")]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceCover(int id, IFormFile file, CancellationToken ct)
    {
        var work = await db.LiteraryWorks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct);
        if (work is null) return NotFound();
        var saved = await covers.SaveAsync(file, ct);
        try
        {
            var changed = await db.LiteraryWorks.Where(w => w.Id == id && w.CoverImageUrl == work.CoverImageUrl)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.CoverImageUrl, saved.Url).SetProperty(w => w.UpdatedAt, DateTime.UtcNow), ct);
            if (changed != 1) throw new DbUpdateConcurrencyException();
        }
        catch { await covers.DeleteAsync(saved.PhysicalPath); throw; }
        await Cleanup(work.CoverImageUrl);
        return Ok(new { id, coverImageUrl = saved.Url });
    }

    [HttpDelete("{id:int}/cover")]
    public async Task<IActionResult> DeleteCover(int id, CancellationToken ct)
    {
        var work = await db.LiteraryWorks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct);
        if (work is null) return NotFound();
        if (work.CoverImageUrl is null) return NoContent();
        var changed = await db.LiteraryWorks.Where(w => w.Id == id && w.CoverImageUrl == work.CoverImageUrl)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CoverImageUrl, (string?)null).SetProperty(w => w.UpdatedAt, DateTime.UtcNow), ct);
        if (changed != 1) throw new DbUpdateConcurrencyException();
        await Cleanup(work.CoverImageUrl);
        return NoContent();
    }

    private async Task Cleanup(string? oldUrl)
    {
        if (oldUrl is null || await db.LiteraryWorks.AnyAsync(w => w.CoverImageUrl == oldUrl)) return;
        try { await covers.DeleteByUrlAsync(oldUrl); }
        catch (Exception ex) { logger.LogWarning("Không dọn được bìa cũ. ErrorType={ErrorType}", ex.GetType().Name); }
    }
}
