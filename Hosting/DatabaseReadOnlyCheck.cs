using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VietVan.API.Data;
using VietVan.API.Entities;

namespace VietVan.API.Hosting;

public static class DatabaseReadOnlyCheck
{
    // No WebApplication/hosted service is built: no expiry worker, seed or migrations.
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment, TextWriter output,
        bool paymentsOnly = false)
    {
        var stage = "configuration";
        try
        {
            await output.WriteLineAsync(paymentsOnly
                ? "READ-ONLY Payment schema: SELECT TOP (0) only; no order creation, worker, migration or seed."
                : "READ-ONLY database schema: SELECT TOP (0) only; no worker, migration or seed.");
            var value = DeploymentConfiguration.SqlConnection(configuration, environment);
            await using var connection = new SqlConnection(value);
            SqlConnectionOpeningRetryInterceptor.Configure(connection);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            stage = "opening connection";
            await connection.OpenAsync(timeout.Token);
            await using var identity = connection.CreateCommand();
            identity.CommandTimeout = 10;
            stage = "database identity";
            identity.CommandText = "SELECT DB_NAME(), CONVERT(nvarchar(30), SERVERPROPERTY('ProductVersion')), compatibility_level FROM sys.databases WHERE name = DB_NAME()";
            await using (var reader = await identity.ExecuteReaderAsync(timeout.Token))
            {
                await reader.ReadAsync(timeout.Token);
                await output.WriteLineAsync($"Connected database: {reader.GetString(0)}; SQL version: {reader.GetString(1)}; compatibility level: {reader.GetValue(2)}");
            }
            // Inspect column names only, then compile EF projections without reading rows.
            var options = new DbContextOptionsBuilder<VietVanDbContext>().UseSqlServer(connection)
                .EnableSensitiveDataLogging(false).Options;
            await using var db = new VietVanDbContext(options);
            stage = "table schemas";
            var passed = true;
            // Keep inspecting later tables after a schema mismatch to report all blockers.
            if (!paymentsOnly) passed &= await CheckTableAsync<LiteraryWork>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<User>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<UserAuthState>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<UserSubscription>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<SubscriptionPackage>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<PaymentOrder>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<PaymentAuditLog>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<PaymentBankTransaction>(db, connection, output, timeout.Token);
            passed &= await CheckTableAsync<PaymentWebhookEvent>(db, connection, output, timeout.Token);
            if (!paymentsOnly)
            {
                passed &= await CheckTableAsync<EmailVerificationRecord>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<PasswordResetRecord>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaProduction>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaAsset>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaScene>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaHotspot>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaManifest>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<DramaViewing>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<CulturalInsightView>(db, connection, output, timeout.Token);
                passed &= await CheckTableAsync<Review>(db, connection, output, timeout.Token);
            }
            if (!passed)
            {
                await output.WriteLineAsync("Schema check failed. Review the listed tables/columns before planning an upgrade; no schema or data was changed.");
                return 1;
            }
            await output.WriteLineAsync("Read-only check completed. No migration, seed, UPDATE, INSERT or DELETE was run.");
            return 0;
        }
        catch (InvalidOperationException)
        {
            await output.WriteLineAsync($"Read-only check failed at {stage}. Check configuration/imported schema; no connection string or provider message is printed.");
        }
        catch (SqlException exception)
        {
            await output.WriteLineAsync($"SQL check failed at {stage}. SQL error number: {exception.Number}. No connection string or password is printed.");
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Read-only database check timed out or was cancelled.");
        }
        catch (Exception exception)
        {
            await output.WriteLineAsync($"Read-only database check failed. Error type: {exception.GetType().Name}. No provider message or connection string is printed.");
        }
        return 1;
    }

    internal static string[] MissingColumns<TEntity>(VietVanDbContext db, IEnumerable<string> actualColumns) where TEntity : class
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var actual = actualColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entity.GetProperties().Select(property => property.GetColumnName(table))
            .OfType<string>().Where(column => !actual.Contains(column))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(column => column, StringComparer.Ordinal).ToArray();
    }

    private static async Task<bool> CheckTableAsync<TEntity>(VietVanDbContext db, SqlConnection connection,
        TextWriter output, CancellationToken cancellationToken) where TEntity : class
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))!;
        var schema = entity.GetSchema() ?? "dbo";
        var table = entity.GetTableName()!;
        using var identifiers = new SqlCommandBuilder();
        var query = $"SELECT TOP (0) * FROM {identifiers.QuoteIdentifier(schema)}.{identifiers.QuoteIdentifier(table)}";
        try
        {
            string[] columns;
            await using (var command = connection.CreateCommand())
            {
                command.CommandTimeout = 10;
                command.CommandText = query;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            }
            var missing = MissingColumns<TEntity>(db, columns);
            if (missing.Length > 0)
            {
                // Names come from the EF model, never from exception text or user records.
                await output.WriteLineAsync($"FAIL {schema}.{table}: missing required columns: {string.Join(", ", missing)}");
                return false;
            }
            await db.Set<TEntity>().FromSqlRaw(query).AsNoTracking().ToListAsync(cancellationToken);
            await output.WriteLineAsync($"PASS {schema}.{table} projection (SELECT only)");
            return true;
        }
        catch (SqlException exception) when (exception.Number is 207 or 208 or 229)
        {
            var reason = exception.Number == 229 ? "SELECT permission denied" : "table/column missing or not visible";
            await output.WriteLineAsync($"FAIL {schema}.{table}: {reason}; SQL error number: {exception.Number}");
            return false;
        }
        catch (InvalidOperationException)
        {
            await output.WriteLineAsync($"FAIL {schema}.{table}: EF projection incompatible; no provider message is printed.");
            return false;
        }
    }
}
