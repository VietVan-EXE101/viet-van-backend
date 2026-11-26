using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class SqlConnectionOpeningRetryTests
{
    [Fact]
    public void SchemaDiagnosticIdentifiesMissingUserColumnWithoutReadingUserRows()
    {
        using var db = DiagnosticContext();
        var columns = new[] { "Id", "Username", "Email", "PasswordHash", "Role", "CreatedAt", "VipExpiresAt" };
        Assert.Equal(new[] { "FullName" }, DatabaseReadOnlyCheck.MissingColumns<User>(db, columns));
    }

    [Fact]
    public void SchemaDiagnosticReportsBenefitsMismatchWithoutTreatingBenefitAsEquivalent()
    {
        using var db = DiagnosticContext();
        var columns = new[] { "Id", "Name", "Code", "BillingPeriod", "Price", "DurationInDays", "TierLevel",
            "Description", "Benefit", "IsActive", "CreatedAt", "UpdatedAt" };
        Assert.Equal(new[] { "Benefits" }, DatabaseReadOnlyCheck.MissingColumns<SubscriptionPackage>(db, columns));
    }

    [Fact]
    public void SchemaDiagnosticAcceptsExtraColumnsAndCaseDifferencesLikeEfFromSql()
    {
        using var db = DiagnosticContext();
        var columns = new[] { "id", "username", "fullname", "email", "passwordhash", "role", "createdat", "vipexpiresat", "LegacyColumn" };
        Assert.Empty(DatabaseReadOnlyCheck.MissingColumns<User>(db, columns));
    }

    private static VietVanDbContext DiagnosticContext() => new(new DbContextOptionsBuilder<VietVanDbContext>()
        .UseSqlServer("Server=sample.mssql.somee.com;Database=Sample;User Id=sample;Password=sample-only").Options);

    [Fact]
    public void SchemaDiagnosticDetectsLegacySceneColumnsMissingPreviewReferences()
    {
        using var db = DiagnosticContext();
        var columns = new[] { "Id", "DramaProductionId", "SceneKey", "Title", "SortOrder", "StartMs", "EndMs",
            "AudioOffsetMs", "BackgroundAssetId", "AudioAssetId", "VideoAssetId" };
        Assert.Equal(new[] { "PreviewAudioAssetId", "PreviewVideoAssetId" },
            DatabaseReadOnlyCheck.MissingColumns<DramaScene>(db, columns));
    }

    [Fact]
    public void SchemaDiagnosticDetectsMissingPublishedLegacySnapshotColumn()
    {
        using var db = DiagnosticContext();
        var columns = new[] { "DramaProductionId", "DraftJson", "PublishedJson", "UpdatedAt", "PublishedAt", "Revision" };
        Assert.Equal(new[] { "PublishedLegacyJson" }, DatabaseReadOnlyCheck.MissingColumns<DramaManifest>(db, columns));
    }

    [Fact]
    public void SchemaDiagnosticDetectsMissingViewingVersionColumn()
    {
        using var db = DiagnosticContext();
        Assert.Equal(new[] { "VersionType" }, DatabaseReadOnlyCheck.MissingColumns<DramaViewing>(db,
            new[] { "UserId", "DramaProductionId", "AccessedAt" }));
    }

    [Fact]
    public void EfConnectionsRetryOpeningWithoutEnablingTransactionOrCommandReplay()
    {
        var options = new DbContextOptionsBuilder<VietVanDbContext>()
            .UseSqlServer("Server=sample.mssql.somee.com;Database=Sample;User Id=sample;Password=sample-only")
            .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance).Options;
        using var db = new VietVanDbContext(options);
        var connection = Assert.IsType<SqlConnection>(db.Database.GetDbConnection());
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(3, connection.RetryLogicProvider.RetryLogic.NumberOfTries);
        // Existing BeginTransaction-based business flows retain their original strategy.
        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
        using var command = connection.CreateCommand();
        Assert.NotSame(connection.RetryLogicProvider, command.RetryLogicProvider);
        Assert.True(command.RetryLogicProvider is null || command.RetryLogicProvider.RetryLogic.NumberOfTries == 1);
    }

    [Fact]
    public async Task UnfilledSomeeConfigurationNeverOpensConnectionAndDoesNotPrintSecret()
    {
        const string placeholder = "REPLACE_WITH_SOMEE_PASSWORD";
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Server=sample.mssql.somee.com;Database=Sample;User Id=sample;Password={placeholder}" }).Build();
        using var output = new StringWriter();
        var result = await VietVan.API.Hosting.DatabaseReadOnlyCheck.RunAsync(configuration, new PlatformEnvironment(), output);
        Assert.Equal(1, result);
        Assert.DoesNotContain(placeholder, output.ToString());
        Assert.DoesNotContain("Password=", output.ToString());
        Assert.DoesNotContain("Connected database:", output.ToString());
    }
}
