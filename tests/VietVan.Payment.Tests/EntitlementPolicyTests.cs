using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

public class EntitlementPolicyTests
{
    [Theory]
    [InlineData("Public", false, 0, true)]
    [InlineData("Public", true, 0, true)]
    [InlineData("FreeAuthenticated", false, 0, false)]
    [InlineData("FreeAuthenticated", true, 0, true)]
    [InlineData("VIP", false, 0, false)]
    [InlineData("VIP", true, 0, false)]
    [InlineData("VIP", true, 1, true)]
    [InlineData("Unknown", true, 3, false)]
    public void Drama_access_checks_level_and_current_tier(string access, bool authenticated, int tier, bool expected) =>
        Assert.Equal(expected, EntitlementService.CanAccessDrama(access, authenticated, tier));

    [Fact]
    public void Free_package_without_duration_does_not_expire_immediately()
    {
        var now = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DateTime.MaxValue, SubscriptionRules.FreeEndDate(now, 0));
        Assert.Equal(now.AddDays(30), SubscriptionRules.FreeEndDate(now, 30));
    }
    [Theory]
    [InlineData(0, true, 0, false)]
    [InlineData(2, true, null, true)]
    [InlineData(3, true, null, true)]
    [InlineData(1, true, null, true)]
    public void All_paid_packages_grant_pro_and_free_watches_video(int tier, bool full, int? insights, bool pro)
    {
        var p = EntitlementService.Policy(tier);
        Assert.Equal(full, p.CanWatchFullDrama);
        Assert.Equal(insights, p.MaxCulturalInsightsPerDrama);
        Assert.Equal(pro, p.CanAccessInteractive);
        Assert.Equal(pro ? "PRO" : "FREE", p.AccessLevel);
        Assert.False(p.CanUseAdvancedShadowing);
        Assert.False(p.CanUseAutoTranslation);
        Assert.False(p.CanExportFlashcards);
        Assert.False(p.CanUseVirtualTicketBooth);
    }

    [Theory]
    [InlineData(0, 120000, 120000, true)]
    [InlineData(0, 120001, 200000, false)]
    [InlineData(60000, 90000, 60000, false)]
    [InlineData(500, 500, 120000, false)]
    public void Preview_never_exceeds_configured_two_minute_cap(int start, int end, int limit, bool allowed) =>
        Assert.Equal(allowed, SubscriptionRules.IsPreviewScene(start, end, limit));

    [Fact]
    public void Billing_period_uses_calendar_months_and_years()
    {
        var date = new DateTime(2024, 1, 31);
        Assert.Equal(new DateTime(2024, 2, 29), SubscriptionRules.EndDate(date, "Monthly", 30));
        Assert.Equal(new DateTime(2025, 1, 31), SubscriptionRules.EndDate(date, "Yearly", 365));
        Assert.Equal(date.AddDays(30), SubscriptionRules.EndDate(date, null, 30));
    }
}
