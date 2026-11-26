using Microsoft.EntityFrameworkCore;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class EmailVerificationInspectionTests
{
    [Theory]
    [InlineData(false, false, "PENDING_VERIFICATION")]
    [InlineData(true, false, "ALREADY_VERIFIED")]
    [InlineData(false, true, "ACCOUNT_LOCKED")]
    public async Task ReportsCurrentEligibilityAndMetadataWithoutChangingAccountOrLeakingTokens(bool verified, bool locked, string expected)
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync(); var user = await fixture.AddUserAsync(verified: verified);
        var state = await fixture.Db.UserAuthStates.SingleAsync(); state.IsLocked = locked;
        var hash = Enumerable.Repeat((byte)72, 32).ToArray();
        var created = DateTime.UtcNow.AddSeconds(-5);
        fixture.Db.EmailVerificationTokens.Add(new EmailVerificationRecord { UserId = user.Id, EmailSnapshot = user.Email,
            TokenHash = hash, CreatedAt = created, ExpiresAt = created.AddHours(1) });
        await fixture.Db.SaveChangesAsync(); fixture.Db.ChangeTracker.Clear();
        using var output = new StringWriter();
        Assert.Equal(0, await EmailVerificationInspection.RunCoreAsync(fixture.Db, user.Email, output, default));
        var text = output.ToString(); Assert.Contains(expected, text); Assert.Contains("CooldownNow=True", text);
        Assert.DoesNotContain(user.Email, text); Assert.DoesNotContain(Convert.ToHexString(hash), text);
        Assert.DoesNotContain(Convert.ToBase64String(hash), text);
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
        Assert.Equal(verified, (await fixture.Db.UserAuthStates.AsNoTracking().SingleAsync()).EmailVerified);
        var after = await fixture.Db.EmailVerificationTokens.AsNoTracking().SingleAsync();
        Assert.Equal(hash, after.TokenHash); Assert.Null(after.ConsumedAt); Assert.Equal(created, after.CreatedAt);
    }

    [Fact]
    public async Task UnknownEmailDoesNotCreateAccountOrToken()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync(); using var output = new StringWriter();
        Assert.Equal(0, await EmailVerificationInspection.RunCoreAsync(fixture.Db, "unknown@example.test", output, default));
        Assert.Contains("Account=NOT_FOUND", output.ToString());
        Assert.Equal(0, await fixture.Db.Users.CountAsync()); Assert.Equal(0, await fixture.Db.EmailVerificationTokens.CountAsync());
    }
}
