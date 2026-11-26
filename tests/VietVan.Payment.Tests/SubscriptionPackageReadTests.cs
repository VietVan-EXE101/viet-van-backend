using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.DTOs.Payments;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

public sealed class SubscriptionPackageReadTests
{
    // Chạy pipeline SQL Server/query thực, chặn mở mạng và thay reader bằng dữ liệu thử.
    private sealed class OfflineConnection : DbConnectionInterceptor
    {
        public Exception? Failure { get; init; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }

    private sealed class PackageReader : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql = command.CommandText;
            var table = new DataTable();
            foreach (var column in new (string Name, Type Type)[] {
                ("Id",typeof(int)),("Name",typeof(string)),("Description",typeof(string)),("Price",typeof(decimal)),
                ("DurationInDays",typeof(int)),("TierLevel",typeof(int)),("Benefits",typeof(string)),
                ("IsActive",typeof(bool)),("Code",typeof(string)),("BillingPeriod",typeof(string)) })
                table.Columns.Add(column.Name, column.Type);
            table.Rows.Add(11, "Imported PRO", "", 50000m, 30, 1, "", true, DBNull.Value, DBNull.Value);
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }

    private static VietVanDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<VietVanDbContext>()
        .UseSqlServer("Server=never-contacted.invalid;Database=OfflineOnly;User Id=offline;Password=not-used")
        .AddInterceptors(interceptors).Options);
    private static PaymentService Service(VietVanDbContext db) => new(db, new VietQrGateway(Options.Create(new SePayOptions())),
        Options.Create(new SePayOptions()), new EntitlementService(db));

    [Fact]
    public async Task PublicEndpointCompilesSqlAndReturnsImportedNullableMetadataWithoutPaymentConfiguration()
    {
        var reader = new PackageReader(); await using var db = Context(new OfflineConnection(), reader);
        var result = Assert.IsType<OkObjectResult>(await new SubscriptionPackagesController(Service(db)).List());
        var package = Assert.Single(Assert.IsType<List<PackageDto>>(result.Value));
        Assert.Equal(11, package.Id); Assert.Equal(50000m, package.Price);
        Assert.Null(package.Code); Assert.Null(package.BillingPeriod);
        Assert.Contains("[IsActive]", reader.Sql); Assert.Contains("WHERE", reader.Sql);
        Assert.Contains("ORDER BY", reader.Sql); Assert.Contains("[Price]", reader.Sql);
        Assert.DoesNotContain("UPDATE", reader.Sql); Assert.DoesNotContain("INSERT", reader.Sql);
    }

    [Fact]
    public async Task ReadFailureRetainsCauseForServerDiagnosticsWithoutConvertingItToBusinessConflict()
    {
        var cause = new InvalidOperationException("provider-sentinel");
        await using var db = Context(new OfflineConnection { Failure = cause });
        var error = await Assert.ThrowsAsync<SubscriptionPackagesReadException>(() => Service(db).PackagesAsync());
        Assert.Same(cause, error.InnerException);
    }
}
