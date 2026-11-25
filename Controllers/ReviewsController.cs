using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Controllers;

public sealed class ReviewRequest
{
    [Range(1, 5)] public int Rating { get; set; }
    [MaxLength(2000)] public string? Comment { get; set; }
}
public sealed class ReviewStatusRequest
{
    [Required, RegularExpression("^(Published|Hidden)$")] public string Status { get; set; } = "";
}

[ApiController]
public class ReviewsController(FeedbackService service) : ControllerBase
{
    private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("api/dramas/{id:int}/reviews")]
    public async Task<IActionResult> List(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string sort = "newest", CancellationToken ct = default) => Ok(await service.ListAsync(id, UserId, page, pageSize, sort, ct));

    [HttpGet("api/dramas/{id:int}/reviews/summary")]
    public async Task<IActionResult> Summary(int id, CancellationToken ct) => Ok(await service.SummaryAsync(id, ct));

    [Authorize, EnableRateLimiting("review-write"), HttpPost("api/dramas/{id:int}/reviews")]
    [Obsolete("Dùng PUT feedback để tạo/cập nhật đánh giá của mình.")]
    public async Task<IActionResult> Create(int id, ReviewRequest request, CancellationToken ct) =>
        StatusCode(201, await service.SaveAsync(id, UserId!.Value, request.Rating, request.Comment, true, ct));

    [Authorize, EnableRateLimiting("review-write"), HttpPut("api/dramas/{id:int}/feedback")]
    public async Task<IActionResult> Upsert(int id, ReviewRequest request, CancellationToken ct) =>
        Ok(await service.SaveAsync(id, UserId!.Value, request.Rating, request.Comment, false, ct));

    [Authorize, EnableRateLimiting("review-write"), HttpPut("api/reviews/{reviewId:long}")]
    [Obsolete("Dùng PUT /api/dramas/{id}/feedback.")]
    public async Task<IActionResult> Update(long reviewId, ReviewRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(reviewId, UserId!.Value, request.Rating, request.Comment, ct));

    [Authorize, EnableRateLimiting("review-write"), HttpDelete("api/reviews/{reviewId:long}")]
    [Obsolete("Dùng DELETE /api/dramas/{id}/feedback.")]
    public async Task<IActionResult> Delete(long reviewId, CancellationToken ct)
    { await service.DeleteAsync(null, reviewId, UserId!.Value, ct); return NoContent(); }

    [Authorize, EnableRateLimiting("review-write"), HttpDelete("api/dramas/{id:int}/feedback")]
    public async Task<IActionResult> DeleteMine(int id, CancellationToken ct)
    { await service.DeleteAsync(id, null, UserId!.Value, ct); return NoContent(); }

    [Authorize(Roles = "Admin"), HttpGet("api/admin/reviews")]
    public async Task<IActionResult> AdminList([FromQuery] int? workId, [FromQuery] int? dramaId, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await service.AdminListAsync(workId, dramaId, status, search, page, pageSize, ct));

    [Authorize(Roles = "Admin"), EnableRateLimiting("review-write"), HttpPatch("api/admin/reviews/{reviewId:long}/status")]
    public async Task<IActionResult> Moderate(long reviewId, ReviewStatusRequest request, CancellationToken ct) =>
        Ok(await service.ModerateAsync(reviewId, request.Status, UserId!.Value, ct));

    [HttpGet("api/works/{id:int}/reviews"), HttpGet("api/works/{id:int}/reviews/summary"), HttpPost("api/works/{id:int}/reviews")]
    [Obsolete("Dùng feedback của drama.")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Retired() => StatusCode(410, new { message = "Đánh giá tác phẩm đọc đã ngừng sử dụng. Dùng /api/dramas/{id}/feedback." });
}
