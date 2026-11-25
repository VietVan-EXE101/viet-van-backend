using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Entities;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

public class PaymentBusinessTests
{
    private static PaymentService Service(PlatformTestDatabase f)
    {
        var options = Options.Create(new SePayOptions { BankId = "TEST", BankName = "Test only", AccountNumber = "123456789", AccountHolder = "TEST ONLY", OrderLifetimeMinutes = 15 });
        return new PaymentService(f.Db, new VietQrGateway(options), options, new EntitlementService(f.Db));
    }
    private static SePayReceipt Receipt(string code, string id = "tx1", decimal amount = 50000, string direction = "in", string account = "123456789") =>
        new(id, amount, account, direction, code, code, "VND", "hash");
    private static async Task<(User user, SubscriptionPackage package)> SeedAsync(PlatformTestDatabase f)
    {
        var user = await f.AddUserAsync();
        var package = new SubscriptionPackage { Name = "Existing price", Price = 50000, TierLevel = 2, DurationInDays = 30 };
        f.Db.Add(package); await f.Db.SaveChangesAsync(); return (user, package);
    }

    [Fact]
    public async Task PriceOwnershipIdempotencyAndNoSelfActivation()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var (user, pkg) = await SeedAsync(f); var service = Service(f);
        var order = await service.CreateOrderAsync(user.Id, pkg.Id, "unique-key");
        Assert.Equal(pkg.Price, order.Amount);
        Assert.Equal(order.OrderCode, (await service.CreateOrderAsync(user.Id, pkg.Id, "unique-key")).OrderCode);
        var other = await f.AddUserAsync("other");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOrderAsync(other.Id, order.OrderCode));
        Assert.Equal(0, await new EntitlementService(f.Db).CurrentTierAsync(user.Id));
        Assert.Empty(await f.Db.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task ValidDuplicateAndRenewalCreditExactlyOnce()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var (user, pkg) = await SeedAsync(f); var service = Service(f);
        var first = await service.CreateOrderAsync(user.Id, pkg.Id, "first-key");
        Assert.Equal(PaymentStates.Paid, await service.ProcessVerifiedSePayAsync(Receipt(first.OrderCode)));
        Assert.Equal("Duplicate", await service.ProcessVerifiedSePayAsync(Receipt(first.OrderCode)));
        var initial = await f.Db.UserSubscriptions.SingleAsync();
        var second = await service.CreateOrderAsync(user.Id, pkg.Id, "second-key");
        Assert.Equal(PaymentStates.Paid, await service.ProcessVerifiedSePayAsync(Receipt(second.OrderCode, "tx2")));
        var renewal = await f.Db.UserSubscriptions.OrderByDescending(s => s.Id).FirstAsync();
        Assert.Equal(initial.EndDate, renewal.StartDate);
        Assert.Equal(initial.EndDate.AddDays(30), renewal.EndDate);
        Assert.Equal(2, await f.Db.RevenueTransactions.CountAsync());
        Assert.Equal(2, await f.Db.PaymentBankTransactions.CountAsync());
        Assert.Equal(2, await f.Db.PaymentWebhookEvents.CountAsync());
        Assert.Equal(2, await new EntitlementService(f.Db).CurrentTierAsync(user.Id));
        Assert.Equal(renewal.EndDate, await new EntitlementService(f.Db).VipUntilAsync(user.Id));
    }

    [Theory]
    [InlineData(49999, "in", "123456789", false, false)]
    [InlineData(50000, "out", "123456789", false, false)]
    [InlineData(50000, "in", "000000000", false, false)]
    [InlineData(50000, "in", "123456789", true, false)]
    [InlineData(50000, "in", "123456789", false, true)]
    public async Task InvalidOrLateOrCancelledPaymentsNeverActivate(decimal amount, string direction, string account, bool late, bool cancelled)
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var (user, pkg) = await SeedAsync(f); var service = Service(f);
        var order = await service.CreateOrderAsync(user.Id, pkg.Id, "first-key");
        var row = await f.Db.PaymentOrders.SingleAsync();
        if (late) row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        if (cancelled) row.Status = PaymentStates.Cancelled;
        await f.Db.SaveChangesAsync();
        Assert.Equal(PaymentStates.RequiresReview, await service.ProcessVerifiedSePayAsync(Receipt(order.OrderCode, amount: amount, direction: direction, account: account)));
        Assert.Empty(await f.Db.UserSubscriptions.ToListAsync());
        Assert.Empty(await f.Db.RevenueTransactions.ToListAsync());
        Assert.Equal(PaymentStates.RequiresReview, (await f.Db.PaymentBankTransactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnknownCodeIsAuditedWithoutCrediting()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); await SeedAsync(f);
        Assert.Equal(PaymentStates.RequiresReview, await Service(f).ProcessVerifiedSePayAsync(Receipt("VV0123456789ABCDEF")));
        Assert.Null((await f.Db.PaymentBankTransactions.SingleAsync()).PaymentOrderId);
        Assert.Empty(await f.Db.RevenueTransactions.ToListAsync());
    }

    [Fact]
    public async Task SubscriptionFailureRollsBackPaymentRevenueAndEvent()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var (user, pkg) = await SeedAsync(f); var service = Service(f);
        var order = await service.CreateOrderAsync(user.Id, pkg.Id, "first-key");
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSubscription BEFORE INSERT ON UserSubscriptions BEGIN SELECT RAISE(ABORT, 'Injected subscription failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ProcessVerifiedSePayAsync(Receipt(order.OrderCode)));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(PaymentStates.Pending, (await f.Db.PaymentOrders.SingleAsync()).Status);
        Assert.Empty(await f.Db.UserSubscriptions.ToListAsync());
        Assert.Empty(await f.Db.RevenueTransactions.ToListAsync());
        Assert.Empty(await f.Db.PaymentWebhookEvents.ToListAsync());
        Assert.Empty(await f.Db.PaymentBankTransactions.ToListAsync());
    }
}
