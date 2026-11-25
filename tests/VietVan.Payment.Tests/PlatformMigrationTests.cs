using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietVan.API.Data.Migrations;
using Xunit;

namespace VietVan.Payment.Tests;

public class PlatformMigrationTests
{
    [Fact]
    public void MigrationUsesGuardedAdditiveScriptAndCannotDropHistory()
    {
        var migration = new DramaPlatformRefactor();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        Assert.Contains("CREATE TABLE dbo.DramaManifests", sql);
        Assert.Contains("CREATE TABLE dbo.PasswordResetTokens", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("ROLLBACK", sql);
        Assert.Contains("Missing prerequisite schema", sql);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.SubscriptionPackages", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }
}
