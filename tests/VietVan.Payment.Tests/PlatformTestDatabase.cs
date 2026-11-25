using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using VietVan.API.Data;
using VietVan.API.Entities;

namespace VietVan.Payment.Tests;

// SQLite kiểm tra transaction/FK/unique thực; không chứng minh sp_getapplock của SQL Server.
internal sealed class SqlServerLockAdapter : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)) command.CommandText = "SELECT 1";
        return ValueTask.FromResult(result);
    }
}

internal sealed class PlatformTestContext(DbContextOptions<VietVanDbContext> options) : VietVanDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<UserExternalLogin>().Property(x => x.Provider).UseCollation("BINARY");
        b.Entity<UserExternalLogin>().Property(x => x.ProviderSubject).UseCollation("BINARY");
        foreach (var property in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true) property.SetColumnType("TEXT");
    }
}

internal sealed class PlatformTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    public PlatformTestContext Db { get; private set; } = null!;
    public static async Task<PlatformTestDatabase> CreateAsync()
    {
        var fixture = new PlatformTestDatabase(); await fixture.connection.OpenAsync();
        fixture.Db = new PlatformTestContext(new DbContextOptionsBuilder<VietVanDbContext>().UseSqlite(fixture.connection).AddInterceptors(new SqlServerLockAdapter()).Options);
        await fixture.Db.Database.EnsureCreatedAsync();
        return fixture;
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }

    public async Task<User> AddUserAsync(string name = "customer", string role = "Customer", bool verified = true)
    {
        var user = new User { Username = name, Email = name + "@example.com", Role = role };
        user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(user, "ValidPass123!");
        Db.Add(user); await Db.SaveChangesAsync();
        Db.UserAuthStates.Add(new UserAuthState { UserId = user.Id, EmailVerified = verified }); await Db.SaveChangesAsync();
        return user;
    }
}

internal sealed class PlatformEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "VietVan.API";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "wwwroot");
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
