using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.DTOs.Analytics;
using VietVan.API.Entities;
using VietVan.API.Services.Interfaces;

namespace VietVan.API.Services.Implementations;

public sealed class AnalyticsService(VietVanDbContext db) : IAnalyticsService
{
    public async Task<RevenueAnalyticsResponse> RevenueAsync(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from > to) throw new ArgumentException("fromDate phải nhỏ hơn hoặc bằng toDate.");
        var q = db.PaymentOrders.AsNoTracking().Where(p => p.Status == PaymentStates.Paid && p.PaidAt != null && p.BankTransactions.Any(t => t.Provider == "SePay" && t.Status == PaymentStates.Paid && t.Amount == p.Amount));
        if (from.HasValue) q = q.Where(p => p.PaidAt >= from.Value);
        if (to.HasValue) { var end = to.Value.Date.AddDays(1); q = q.Where(p => p.PaidAt < end); }
        var total = await q.SumAsync(p => (decimal?)p.Amount) ?? 0;
        var count = await q.CountAsync();
        var recent = await q.OrderByDescending(p => p.PaidAt).Take(20).Select(p => new TransactionDto(p.Id, p.UserId, p.User.Username, p.PackageNameSnapshot, p.Amount, p.PaidAt!.Value, p.Status)).ToListAsync();
        return new(total, count, recent);
    }
}
