using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietVan.API.DTOs.Works;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Interfaces;
using VietVan.API.Entities;
using VietVan.API.Data;
using Microsoft.EntityFrameworkCore;

namespace VietVan.API.Controllers.Admin;

[ApiController, Route("api/admin/works"), Authorize(Roles = "Admin")]
public class AdminWorksController(IWorkService service, WorkCoverStorage covers, ILogger<AdminWorksController> logger) : ControllerBase
{
    // Admins can select inactive works as well; the public catalogue remains unchanged.
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminWorkListItem>>> List(
        [FromServices] VietVanDbContext db, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.LiteraryWorks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(w => w.Title.Contains(term) || w.Author.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var items = await query.OrderBy(w => w.Title).ThenBy(w => w.Id)
            .Skip(offset).Take(pageSize)
            .Select(w => new AdminWorkListItem(w.Id, w.Title, w.Author, w.IsActive, w.RequiredTierLevel))
            .ToListAsync(ct);
        return Ok(new PagedResponse<AdminWorkListItem>(items, page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpPost, Consumes("application/json", "text/json", "application/*+json")]
    [ProducesResponseType(typeof(WorkMetadataDto), StatusCodes.Status201Created)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Create([FromBody] CreateWorkRequest request)
    {
        var work = await service.CreateAsync(request);
        return Created($"/api/admin/works/{work.Id}", WorkMetadataDto.From(work));
    }

    [HttpPost, Consumes("multipart/form-data")]
    [VietVan.API.Filters.UploadRequest]
    [ProducesResponseType(typeof(WorkMetadataDto), StatusCodes.Status201Created)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> CreateWithCover([FromForm] CreateWorkFormRequest request, CancellationToken ct)
    {
        string? savedPath = null;
        try
        {
            if (request.CoverImage is not null)
            {
                var saved = await covers.SaveAsync(request.CoverImage, ct);
                savedPath = saved.PhysicalPath;
                request.CoverImageUrl = saved.Url;
            }
            var work = await service.CreateAsync(request);
            return Created($"/api/admin/works/{work.Id}", WorkMetadataDto.From(work));
        }
        catch (Exception ex)
        {
            if (savedPath is not null)
            {
                try { await covers.DeleteAsync(savedPath); }
                catch (Exception cleanupError) { logger.LogError(cleanupError, "Không thể xóa ảnh bìa sau khi tạo tác phẩm thất bại."); }
            }
            if (ex is ArgumentException) return BadRequest(new { message = ex.Message });
            throw;
        }
    }

    [HttpPatch("{id:int}/toggle-status")]
    public async Task<IActionResult> Toggle(int id)
    {
        try { var w = await service.ToggleAsync(id); return Ok(new { w.Id, w.IsActive, w.UpdatedAt }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, [FromServices] WorkDeletionService deletion, CancellationToken ct)
    {
        if (id <= 0) return BadRequest(new { message = "ID tác phẩm phải lớn hơn 0." });
        return await deletion.DeleteAsync(id, ct) switch
        {
            WorkDeleteResult.Deleted => NoContent(),
            WorkDeleteResult.NotFound => NotFound(new { message = "Không tìm thấy tác phẩm." }),
            _ => Conflict(new { message = "Không thể xóa tác phẩm vì đang có dữ liệu liên quan. Bạn có thể ẩn tác phẩm bằng toggle-status." })
        };
    }
}
