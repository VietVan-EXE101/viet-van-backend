namespace VietVan.API.Services.Payments;

public static class SubscriptionRules
{
    // Gói Free có thời hạn 0 là không thời hạn; dùng giá trị tối đa của datetime2.
    public static DateTime FreeEndDate(DateTime start, int durationDays) =>
        durationDays <= 0 ? DateTime.MaxValue : start.AddDays(durationDays);
    public static bool IsPreviewScene(int startMs, int endMs, int previewDurationMs) =>
        startMs >= 0 && endMs > startMs && endMs <= Math.Clamp(previewDurationMs, 0, 120000);

    public static DateTime EndDate(DateTime start, string? period, int durationDays) => period switch
    {
        "Monthly" => start.AddMonths(1), "Yearly" => start.AddYears(1), _ => start.AddDays(durationDays)
    };
}
