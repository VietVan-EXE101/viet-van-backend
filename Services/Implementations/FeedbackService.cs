using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Services.Payments;

namespace VietVan.API.Services.Implementations;

public sealed class FeedbackService(VietVanDbContext db, EntitlementService entitlements)
{
    private async Task CheckAsync(int id, CancellationToken ct)
    {
        if (!await db.DramaProductions.AnyAsync(d => d.Id == id && d.IsPublished && d.Status == "PUBLISHED", ct)) throw new KeyNotFoundException("Không tìm thấy vở kịch.");
    }
    private IQueryable<Review> Published(int id) => db.Set<Review>().AsNoTracking().Where(r => r.DramaProductionId == id && r.Status == "Published");

    public async Task<object> ListAsync(int id, int? userId, int page, int pageSize, string sort, CancellationToken ct)
    {
        await CheckAsync(id, ct);
        if (sort is not ("newest" or "oldest" or "rating")) throw new ArgumentException("Thứ tự không hợp lệ.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Published(id); var total = await query.CountAsync(ct);
        var ordered = sort switch { "oldest" => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id), "rating" => query.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Id), _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) };
        var items = await ordered.Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(r => new { r.Id, r.Rating, r.Comment, r.CreatedAt, r.UpdatedAt, displayName = r.User.FullName ?? r.User.Username, isMine = r.UserId == userId }).ToListAsync(ct);
        return new { items, page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) };
    }

    public async Task<object> SummaryAsync(int id, CancellationToken ct)
    {
        await CheckAsync(id, ct);
        var groups = await Published(id).GroupBy(r => r.Rating).Select(g => new { Rating = g.Key, Count = g.Count() }).ToListAsync(ct);
        var total = groups.Sum(g => g.Count);
        return new { averageRating = total == 0 ? 0d : groups.Sum(g => (double)g.Rating * g.Count) / total, totalReviews = total,
            ratingDistribution = Enumerable.Range(1, 5).ToDictionary(x => x, x => groups.FirstOrDefault(g => g.Rating == x)?.Count ?? 0) };
    }

    private async Task CanWriteAsync(int dramaId, int userId, CancellationToken ct)
    {
        await CheckAsync(dramaId, ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId && !db.UserAuthStates.Any(s => s.UserId == userId && s.IsLocked), ct)) throw new UnauthorizedAccessException();
        var free = await db.DramaProductions.AnyAsync(d => d.Id == dramaId && d.FreeVideoAssetId != null, ct);
        if (!free && await entitlements.CurrentTierAsync(userId, ct) <= 0 && !await db.Users.AnyAsync(u => u.Id == userId && u.Role == "Admin", ct)) throw new UnauthorizedAccessException();
        if (!await db.DramaViewings.AnyAsync(v => v.UserId == userId && v.DramaProductionId == dramaId, ct))
            throw new InvalidOperationException("Cần mở video hoặc phiên tương tác hợp lệ trước khi đánh giá.");
    }

    public async Task<object> SaveAsync(int dramaId, int userId, int rating, string? comment, bool createOnly, CancellationToken ct)
    {
        if (rating is < 1 or > 5 || comment?.Length > 2000) throw new ArgumentException("Rating từ 1 đến 5, comment tối đa 2000 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, dramaId, ct);
        await DatabaseLocks.AcquireAsync(db, $"feedback:{userId}:{dramaId}", ct);
        await CanWriteAsync(dramaId, userId, ct);
        var review = await db.Set<Review>().SingleOrDefaultAsync(r => r.UserId == userId && r.DramaProductionId == dramaId, ct);
        if (review is not null && createOnly) throw new InvalidOperationException("Bạn đã đánh giá vở kịch này.");
        if (review is null) { review = new Review { UserId = userId, DramaProductionId = dramaId }; db.Add(review); }
        else review.UpdatedAt = DateTime.UtcNow;
        review.Rating = rating; review.Comment = comment?.Trim() ?? "";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { review.Id, review.Rating, review.Comment, review.Status, review.CreatedAt, review.UpdatedAt };
    }

    public async Task<object> UpdateAsync(long reviewId, int userId, int rating, string? comment, CancellationToken ct)
    {
        var review = await db.Set<Review>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy đánh giá của bạn.");
        if (review.DramaProductionId is not int dramaId) throw new InvalidOperationException("Đánh giá nội dung đọc đã ngừng sử dụng.");
        return await SaveAsync(dramaId, userId, rating, comment, false, ct);
    }

    public async Task DeleteAsync(int? dramaId, long? reviewId, int userId, CancellationToken ct)
    {
        var review = await db.Set<Review>().SingleOrDefaultAsync(r => r.UserId == userId && (dramaId != null ? r.DramaProductionId == dramaId : r.Id == reviewId), ct);
        if (review is null) throw new KeyNotFoundException("Không tìm thấy đánh giá của bạn.");
        db.Remove(review); await db.SaveChangesAsync(ct);
    }

    public async Task<object> AdminListAsync(int? workId, int? dramaId, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (status is not null and not ("Published" or "Hidden")) throw new ArgumentException("Trạng thái không hợp lệ.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Set<Review>().AsNoTracking().AsQueryable();
        if (workId != null) q = q.Where(r => r.LiteraryWorkId == workId);
        if (dramaId != null) q = q.Where(r => r.DramaProductionId == dramaId);
        if (status != null) q = q.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(r => r.Comment.Contains(search.Trim()));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.Id).Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(r => new { r.Id, r.LiteraryWorkId, r.DramaProductionId, r.Rating, r.Comment, r.Status, r.CreatedAt, r.UpdatedAt, displayName = r.User.FullName ?? r.User.Username }).ToListAsync(ct);
        return new { items, page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) };
    }

    public async Task<object> ModerateAsync(long id, string status, int adminUserId, CancellationToken ct)
    {
        if (status is not ("Published" or "Hidden")) throw new ArgumentException("Trạng thái không hợp lệ.");
        var review = await db.Set<Review>().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy đánh giá.");
        if (review.Status != status)
        {
            db.Add(new ReviewModerationLog { ReviewId = id, AdminUserId = adminUserId, OldStatus = review.Status, NewStatus = status });
            review.Status = status; review.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        }
        return new { review.Id, review.Status, review.UpdatedAt };
    }
}
