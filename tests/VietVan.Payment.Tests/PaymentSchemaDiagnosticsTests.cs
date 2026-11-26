using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class PaymentSchemaDiagnosticsTests
{
    private static string UpgradeSql()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VietVan.API.csproj")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "scripts", "Upgrade_Somee_PaymentColumns.sql"));
    }

    [Fact]
    public async Task FourteenUpgradeDefinitionsMatchCurrentEfTypesAndNullability()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync();
        var entries = Regex.Matches(UpgradeSql(), @"\(N'(Payment\w+)',N'([^']+)',N'([^']+)',N'([^']+)',(\d+),(\d+),(\d+),([01])\)");
        Assert.Equal(14, entries.Count);
        foreach (Match entry in entries)
        {
            var entity = fixture.Db.Model.GetEntityTypes().Single(e => e.GetTableName() == entry.Groups[1].Value);
            var property = entity.FindProperty(entry.Groups[2].Value)!;
            Assert.NotNull(property);
            var type = property.ClrType == typeof(string) ? $"nvarchar({property.GetMaxLength()})" :
                property.ClrType == typeof(decimal) ? "decimal(18,2)" : "int";
            Assert.Equal(type + (property.IsNullable ? " NULL" : " NOT NULL"), entry.Groups[3].Value);
            Assert.Equal(property.IsNullable ? "1" : "0", entry.Groups[8].Value);
            Assert.Equal(property.ClrType == typeof(string) ? property.GetMaxLength() * 2 :
                property.ClrType == typeof(decimal) ? 9 : 4, int.Parse(entry.Groups[5].Value));
        }
    }

    [Fact]
    public async Task LegacyInsertBlockerDetectionKnowsEveryMappedPaymentColumn()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync();
        var sql = UpgradeSql();
        var mapping = sql[sql.IndexOf("INSERT INTO @Mapped VALUES", StringComparison.Ordinal)..sql.IndexOf("BEGIN TRY", StringComparison.Ordinal)];
        var entries = Regex.Matches(mapping, @"\(N'(Payment\w+)',N'([^']+)'\)")
            .Select(match => match.Groups[1].Value + "." + match.Groups[2].Value).OrderBy(x => x).ToArray();
        var tables = new[] { "PaymentOrders", "PaymentAuditLogs", "PaymentBankTransactions", "PaymentWebhookEvents" };
        var expected = fixture.Db.Model.GetEntityTypes().Where(e => tables.Contains(e.GetTableName())).SelectMany(entity =>
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return entity.GetProperties().Select(p => entity.GetTableName() + "." + p.GetColumnName(table));
        }).OrderBy(x => x).ToArray();
        Assert.Equal(expected, entries);
    }

    [Fact]
    public void UpgradeDefaultsToPreviewAndStopsBeforeDdlForUnmappedHistoryOrRequiredLegacyFields()
    {
        var sql = UpgradeSql();
        Assert.Contains("DECLARE @ApplyChanges bit = 0", sql);
        Assert.Contains("DB_NAME() <> N'VietVan_New'", sql);
        Assert.Contains("TABLOCKX,HOLDLOCK", sql); Assert.Contains("ROLLBACK TRANSACTION", sql);
        var ddl = sql.IndexOf("SET @Sql=N'ALTER TABLE", StringComparison.Ordinal);
        Assert.True(sql.IndexOf("IF @ApplyChanges=0", StringComparison.Ordinal) < ddl);
        Assert.True(sql.IndexOf("THROW 51004", StringComparison.Ordinal) < ddl);
        Assert.True(sql.IndexOf("THROW 51005", StringComparison.Ordinal) < ddl);
        Assert.True(sql.IndexOf("THROW 51008", StringComparison.Ordinal) < ddl);
        Assert.False(Regex.IsMatch(sql, @"\b(DROP\s+(TABLE|DATABASE|COLUMN)|DELETE\s+FROM\s+dbo\.|UPDATE\s+dbo\.|INSERT\s+(INTO\s+)?dbo\.)", RegexOptions.IgnoreCase));
        Assert.Contains("RowsPreserved", sql);
    }

    [Fact]
    public async Task OnlyFourConfirmedObsoleteColumnsCanBecomeNullableAndNoneAreEfProperties()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync();
        var sql = UpgradeSql();
        var start = sql.IndexOf("INSERT INTO @LegacyChanges", StringComparison.Ordinal);
        var end = sql.IndexOf("SELECT DB_NAME() AS DatabaseName", start, StringComparison.Ordinal);
        var plan = sql[start..end];
        var names = Regex.Matches(plan, @"c\.name(?: IN \(([^)]+)\)|=N'([^']+)')")
            .SelectMany(match => match.Groups[2].Success ? new[] { match.Groups[2].Value } :
                Regex.Matches(match.Groups[1].Value, @"N'([^']+)'").Select(value => value.Groups[1].Value))
            .OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "DurationInDays", "PackageName", "Price", "TierLevel" }, names);
        var order = fixture.Db.Model.FindEntityType(typeof(PaymentOrder))!;
        foreach (var name in names) Assert.Null(order.FindProperty(name));
        Assert.Contains("b.TableName=N'PaymentOrders'", plan);
        Assert.Contains("ty.is_user_defined=0", plan);
        Assert.Contains("c.max_length", plan); Assert.Contains("c.precision", plan);
        Assert.Contains("c.scale", plan); Assert.Contains("+ c.collation_name", plan);
        Assert.Contains("END + N' NULL'", plan);
        Assert.Contains("THROW 51009", sql);
    }

    [Fact]
    public void CollationIsUnquotedAndValidatedBeforeBuildingDynamicAlterColumn()
    {
        var sql = UpgradeSql();
        Assert.Contains("+ N') COLLATE ' + c.collation_name", sql);
        Assert.DoesNotContain("QUOTENAME(c.collation_name)", sql);
        Assert.Contains("NOT LIKE N'%[^A-Za-z0-9_]%'", sql);
        Assert.Contains("sys.fn_helpcollations() hc WHERE hc.name=c.collation_name", sql);
        // Table/column identifiers still need quoting. COLLATE has different grammar.
        Assert.Contains("ALTER COLUMN '+QUOTENAME(@LegacyColumn)", sql);
    }

    [Fact]
    public void LegacyAllowanceStillBlocksHistoryAndUnrecognizedRequiredColumnsBeforeAnyAlter()
    {
        var sql = UpgradeSql();
        var ddl = sql.IndexOf("SET @Sql=N'ALTER TABLE", StringComparison.Ordinal);
        var guard = sql[sql.IndexOf("IF EXISTS(SELECT 1 FROM @LegacyChanges)", StringComparison.Ordinal)..ddl];
        Assert.Contains("RowsBefore FROM @Tables WHERE TableName=N'PaymentOrders')<>0", guard);
        Assert.Contains("THROW 51008", guard);
        Assert.Contains("FROM @InsertBlockers b WHERE NOT (b.TableName=N'PaymentOrders'", guard);
        Assert.Contains("l.ColumnName=b.ColumnName", guard);
        Assert.Contains("THROW 51005", guard);
        Assert.Contains("LegacyColumnsMadeNullable", sql);
        Assert.DoesNotContain("sp_rename", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ADD CONSTRAINT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectsMissingNullableBillingPeriodSnapshotWithoutCreatingOrder()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync();
        var entity = fixture.Db.Model.FindEntityType(typeof(PaymentOrder))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var legacyColumns = entity.GetProperties().Select(p => p.GetColumnName(table))
            .OfType<string>().Where(name => name != "BillingPeriodSnapshot");
        Assert.Equal(["BillingPeriodSnapshot"], DatabaseReadOnlyCheck.MissingColumns<PaymentOrder>(fixture.Db, legacyColumns));
        Assert.Equal(0, await fixture.Db.PaymentOrders.CountAsync());
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ChecksAllPaymentTableColumnsWithoutReadingOrWritingTransactionData()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync();
        void Check<TEntity>() where TEntity : class
        {
            var entity = fixture.Db.Model.FindEntityType(typeof(TEntity))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var columns = entity.GetProperties().Select(p => p.GetColumnName(table)).OfType<string>().ToArray();
            Assert.Empty(DatabaseReadOnlyCheck.MissingColumns<TEntity>(fixture.Db, columns));
            Assert.Equal(columns.OrderBy(name => name, StringComparer.Ordinal), DatabaseReadOnlyCheck.MissingColumns<TEntity>(fixture.Db, []));
        }
        Check<PaymentOrder>(); Check<PaymentAuditLog>(); Check<PaymentBankTransaction>(); Check<PaymentWebhookEvent>();
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }
}
