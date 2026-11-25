using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
namespace VietVan.API.Services.Payments;

public sealed record SubscriptionEntitlements(int TierLevel, string TierName, bool CanWatchFullDrama,
    int? MaxCulturalInsightsPerDrama, bool CanUseBasicShadowing, bool CanUseAdvancedShadowing,
    bool CanUseVirtualTicketBooth, bool CanUseAutoTranslation, bool CanExportFlashcards, int ShadowingPauseSeconds)
{
    public bool CanAccessInteractive => TierLevel > 0;
    public string AccessLevel => TierLevel > 0 ? "PRO" : "FREE";
}

public sealed class EntitlementService(VietVanDbContext db)
{
    // Giữ tier/giá gói đã bán; mọi tier trả phí còn hạn đều cấp cùng quyền PRO.
    public static SubscriptionEntitlements Policy(int tier) => new(tier,
        tier > 0 ? "Pro" : "Free", true, tier > 0 ? null : 0, false, false, false, false, false, 0);
    public async Task<SubscriptionEntitlements> GetAsync(int? userId, CancellationToken ct = default) =>
        Policy(userId.HasValue ? await CurrentTierAsync(userId.Value, ct) : 0);
    public async Task<int> CurrentTierAsync(int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // JWT không quyết định quyền VIP: dữ liệu DB + thời gian UTC mới là nguồn tin cậy.
        return await db.UserSubscriptions.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.StartDate <= now && x.EndDate > now)
            .Select(x => (int?)(x.GrantedTierLevel ?? x.Package.TierLevel)).MaxAsync(ct) ?? 0;
    }
    public async Task<DateTime?> VipUntilAsync(int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var paid = db.UserSubscriptions.AsNoTracking().Where(x => x.UserId == userId && x.IsActive && (x.GrantedTierLevel ?? x.Package.TierLevel) > 0);
        var until = await paid.Where(x => x.StartDate <= now && x.EndDate > now).Select(x => (DateTime?)x.EndDate).MaxAsync(ct);
        if (until is null) return null;
        // Gia hạn nối tiếp phải hiển thị toàn bộ thời hạn đã mua; không nối qua khoảng trống chưa có quyền.
        while (true)
        {
            var next = await paid.Where(x => x.StartDate <= until.Value && x.EndDate > until.Value).Select(x => (DateTime?)x.EndDate).MaxAsync(ct);
            if (next is null) return until;
            until = next;
        }
    }
    public async Task<bool> CanAccessAsync(string accessLevel, int? userId, CancellationToken ct = default)
    {
        // Chỉ truy vấn subscription khi mức VIP thực sự cần kiểm tra.
        var tier = accessLevel == "VIP" && userId.HasValue ? await CurrentTierAsync(userId.Value, ct) : 0;
        return CanAccessDrama(accessLevel, userId.HasValue, tier);
    }
    public static bool CanAccessDrama(string accessLevel, bool authenticated, int tier) => accessLevel switch
    {
        "Public" => true,
        "FreeAuthenticated" => authenticated,
        "VIP" => authenticated && tier > 0,
        _ => false
    };
}
