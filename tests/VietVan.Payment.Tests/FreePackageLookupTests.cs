using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class FreePackageLookupTests
{
    [Fact]
    public void Registration_query_only_reads_free_package_fields()
    {
        var options = new DbContextOptionsBuilder<VietVanDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new VietVanDbContext(options);
        var sql = FreePackageLookup.Query(db.SubscriptionPackages).ToQueryString();
        Assert.Contains("[Id]", sql);
        Assert.Contains("[DurationInDays]", sql);
        Assert.Contains("[TierLevel]", sql);
        Assert.DoesNotContain("[Description]", sql);
        Assert.DoesNotContain("[Benefits]", sql);
        Assert.DoesNotContain("[BillingPeriod]", sql);
        Assert.DoesNotContain("[Code]", sql);
        Assert.DoesNotContain("[CreatedAt]", sql);
        Assert.DoesNotContain("[UpdatedAt]", sql);
    }
}
