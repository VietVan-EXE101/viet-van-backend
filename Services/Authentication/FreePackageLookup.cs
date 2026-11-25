using VietVan.API.Entities;

namespace VietVan.API.Services.Authentication;

public sealed record FreePackageInfo(int Id, int DurationInDays);

public static class FreePackageLookup
{
    // Đăng ký chỉ cần khóa và thời hạn; không tải các cột mô tả/billing của gói.
    public static IQueryable<FreePackageInfo> Query(IQueryable<SubscriptionPackage> packages) =>
        packages.Where(x => x.TierLevel == 0).Select(x => new FreePackageInfo(x.Id, x.DurationInDays));
}
