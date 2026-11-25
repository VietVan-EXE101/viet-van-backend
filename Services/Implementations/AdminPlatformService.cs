using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;

namespace VietVan.API.Services.Implementations;

public sealed class AdminPlatformService(VietVanDbContext db)
{
    private IQueryable<PaymentOrder> Confirmed() => db.PaymentOrders.AsNoTracking().Where(p => p.Status == PaymentStates.Paid && p.PaidAt != null &&
        p.BankTransactions.Any(t => t.Provider == "SePay" && t.Status == PaymentStates.Paid && t.Amount == p.Amount));

    public async Task<object> DashboardAsync(DateTime? fromUtc, DateTime? toUtc, string groupBy, CancellationToken ct)
    {
        if (groupBy is not ("day" or "month")) throw new ArgumentException("groupBy phải là day hoặc month.");
        var now = DateTime.UtcNow; var local = now.AddHours(7);
        var today = local.Date.AddHours(-7); var month = new DateTime(local.Year, local.Month, 1).AddHours(-7);
        var from = fromUtc ?? month; var to = toUtc ?? now;
        if (from >= to || to - from > TimeSpan.FromDays(3660)) throw new ArgumentException("Khoảng thời gian không hợp lệ (tối đa 10 năm).");
        var paid = Confirmed();
        var totalRevenue = await paid.SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var todayRevenue = await paid.Where(p => p.PaidAt >= today && p.PaidAt < today.AddDays(1)).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var monthRevenue = await paid.Where(p => p.PaidAt >= month && p.PaidAt < month.AddMonths(1)).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var range = paid.Where(p => p.PaidAt >= from && p.PaidAt < to);
        var rangeRevenue = await range.SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var periods = await range.GroupBy(p => new { Year = p.PaidAt!.Value.AddHours(7).Year, Month = p.PaidAt.Value.AddHours(7).Month, Day = groupBy == "day" ? p.PaidAt.Value.AddHours(7).Day : 1 })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Revenue = g.Sum(p => p.Amount), Transactions = g.Count() })
            .OrderBy(p => p.Year).ThenBy(p => p.Month).ThenBy(p => p.Day).ToListAsync(ct);
        var successfulTransactions = await paid.CountAsync(ct);
        var pendingTransactions = await db.PaymentOrders.CountAsync(p => p.Status == PaymentStates.Pending && p.ExpiresAt > now, ct);
        var customers = db.Users.Where(u => u.Role == "Customer");
        var proUsers = await customers.CountAsync(u => db.UserSubscriptions.Any(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now && (s.GrantedTierLevel ?? s.Package.TierLevel) > 0), ct);
        var totalCustomers = await customers.CountAsync(ct);
        var totalUsers = await db.Users.CountAsync(ct);
        var totalDramas = await db.DramaProductions.CountAsync(ct);
        var publishedDramas = await db.DramaProductions.CountAsync(d => d.IsPublished && d.Status == "PUBLISHED", ct);
        var disabledDramas = await db.DramaProductions.CountAsync(d => d.Status == "DISABLED", ct);
        var averageRating = await db.Set<Review>().Where(r => r.DramaProductionId != null && r.Status == "Published").AverageAsync(r => (double?)r.Rating, ct) ?? 0;
        return new { totalRevenue, todayRevenue, monthRevenue, rangeRevenue, fromUtc = from, toUtc = to, timeZone = "Asia/Ho_Chi_Minh", periods,
            successfulTransactions, pendingTransactions, totalUsers, proUsers, freeUsers = totalCustomers - proUsers, totalDramas, publishedDramas, disabledDramas, averageRating };
    }

    public async Task<object> UsersAsync(string? search, string? accessLevel, bool? locked, int page, int pageSize, int? id, CancellationToken ct)
    {
        if (accessLevel is not null and not ("FREE" or "PRO")) throw new ArgumentException("accessLevel phải là FREE hoặc PRO.");
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100); var now = DateTime.UtcNow;
        var q = db.Users.AsNoTracking().AsQueryable();
        if (id.HasValue) q = q.Where(u => u.Id == id);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.Username.Contains(search.Trim()) || u.Email.Contains(search.Trim()) || (u.FullName != null && u.FullName.Contains(search.Trim())));
        if (locked.HasValue) q = q.Where(u => db.UserAuthStates.Any(s => s.UserId == u.Id && s.IsLocked) == locked.Value);
        if (accessLevel is not null) q = q.Where(u => db.UserSubscriptions.Any(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now && (s.GrantedTierLevel ?? s.Package.TierLevel) > 0) == (accessLevel == "PRO"));
        var total = await q.CountAsync(ct);
        if (id.HasValue && total == 0) throw new KeyNotFoundException("Không tìm thấy tài khoản.");
        var items = await q.OrderBy(u => u.Id).Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(u => new { u.Id, u.Username, u.FullName, u.Email, u.Role, u.CreatedAt,
                isLocked = db.UserAuthStates.Any(s => s.UserId == u.Id && s.IsLocked),
                emailVerified = db.UserAuthStates.Any(s => s.UserId == u.Id && s.EmailVerified),
                accessLevel = db.UserSubscriptions.Any(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now && (s.GrantedTierLevel ?? s.Package.TierLevel) > 0) ? "PRO" : "FREE",
                proUntil = db.UserSubscriptions.Where(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now && (s.GrantedTierLevel ?? s.Package.TierLevel) > 0).Max(s => (DateTime?)s.EndDate) }).ToListAsync(ct);
        return new { items, page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) };
    }

    public async Task<object> LockAsync(int adminId, int userId, bool locked, CancellationToken ct)
    {
        if (adminId == userId && locked) throw new ArgumentException("Không thể tự khóa tài khoản quản trị đang sử dụng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.AcquireAsync(db, $"auth-user:{userId}", ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw new KeyNotFoundException("Không tìm thấy tài khoản.");
        var state = await db.UserAuthStates.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (state is null) { state = new UserAuthState { UserId = userId, EmailVerified = true }; db.Add(state); }
        if (state.IsLocked != locked)
        {
            state.IsLocked = locked; state.TokenVersion++;
            db.AdminAuditLogs.Add(new AdminAuditLog { AdminUserId = adminId, TargetUserId = userId, Action = locked ? "AccountLocked" : "AccountUnlocked" });
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { userId, isLocked = state.IsLocked };
    }
}
