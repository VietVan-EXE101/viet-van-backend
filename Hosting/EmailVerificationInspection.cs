using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;

namespace VietVan.API.Hosting;

// Explicit operator command. Only SELECTs account flags and token timestamps, never token/hash/body.
public static class EmailVerificationInspection
{
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment,
        string email, TextWriter output)
    {
        await output.WriteLineAsync("READ-ONLY verification inspection: SELECT only; no email, worker, migration, seed, UPDATE, INSERT, DELETE or R2 access.");
        email = email.Trim().ToLowerInvariant();
        if (email.Length > 150 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
        { await output.WriteLineAsync("FAIL invalid email argument (value hidden)."); return 1; }
        try
        {
            var connection = new SqlConnectionStringBuilder(DeploymentConfiguration.SqlConnection(configuration, environment))
                { ConnectTimeout = 10, ConnectRetryCount = 0 };
            await using var db = new VietVanDbContext(new DbContextOptionsBuilder<VietVanDbContext>()
                .UseSqlServer(connection.ConnectionString).EnableSensitiveDataLogging(false).Options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await output.WriteLineAsync("Database=" + connection.InitialCatalog + "; configuration belongs to this process, not a remote Render environment.");
            return await RunCoreAsync(db, email, output, timeout.Token);
        }
        catch (Exception error)
        {
            var code = error is SqlException sql ? "SQL_" + sql.Number : error.GetType().Name;
            await output.WriteLineAsync("Inspection stopped Code=" + code + "; no connection string, secret or provider message printed.");
            return 1;
        }
    }

    internal static async Task<int> RunCoreAsync(VietVanDbContext db, string email, TextWriter output, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.Id).Take(2).ToListAsync(ct);
        if (users.Count != 1)
        {
            await output.WriteLineAsync(users.Count == 0 ? "Account=NOT_FOUND; resend does not call the email provider." : "Account=DUPLICATE_EMAIL; review account data without resetting it.");
            return users.Count == 0 ? 0 : 1;
        }
        var id = users[0];
        var state = await db.UserAuthStates.AsNoTracking().Where(s => s.UserId == id)
            .Select(s => new { s.EmailVerified, s.IsLocked }).SingleOrDefaultAsync(ct);
        var reason = state is null ? "AUTH_STATE_MISSING" : state.EmailVerified ? "ALREADY_VERIFIED" : state.IsLocked ? "ACCOUNT_LOCKED" : "PENDING_VERIFICATION";
        await output.WriteLineAsync($"UserId={id}; CurrentState={reason}");
        var now = DateTime.UtcNow;
        var tokens = await db.EmailVerificationTokens.AsNoTracking().Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt).Take(3)
            .Select(t => new { t.CreatedAt, t.ExpiresAt, t.ConsumedAt }).ToListAsync(ct);
        await output.WriteLineAsync($"CooldownNow={tokens.Any(t => t.CreatedAt > now.AddMinutes(-1))}; latestRecords={tokens.Count}");
        foreach (var token in tokens)
            await output.WriteLineAsync($"TokenMetadata CreatedAtUTC={token.CreatedAt:yyyy-MM-dd HH:mm:ss}; ExpiresAtUTC={token.ExpiresAt:yyyy-MM-dd HH:mm:ss}; Consumed={token.ConsumedAt.HasValue}; ExpiredNow={token.ExpiresAt <= now}");
        await output.WriteLineAsync("Timestamps show token creation, not provider invocation/acceptance. Use Render/Resend logs for the historical request; account state above is current.");
        return 0;
    }
}
