using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.DTOs.Auth;
using VietVan.API.Entities;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Settings;
using Xunit;

namespace VietVan.Payment.Tests;

internal sealed class CaptureEmail : IEmailSender
{
    public List<string> Messages { get; } = [];
    public Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default) { Messages.Add(html); return Task.CompletedTask; }
    public string LastToken => Regex.Match(Messages.Last(), @"(?:#|<code>)([A-Za-z0-9_-]{43})").Groups[1].Value;
}

public class AuthenticationFlowTests
{
    private sealed class SessionLogs : ILogger<AccountSessionService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
    private sealed class RejectSmtpEmail : IEmailSender
    {
        public Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default) =>
            throw new System.Net.Mail.SmtpException(System.Net.Mail.SmtpStatusCode.ClientNotPermitted, "private-provider-diagnostic");
    }

    [Theory]
    [InlineData("http://app.example.com/reset-password")]
    [InlineData("https://app.example.com/reset-password#existing-fragment")]
    public async Task ExplicitlyInvalidResetPageFailsEquallyForKnownAndUnknownEmails(string resetUrl)
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var mail = new CaptureEmail();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:PasswordResetUrl"] = resetUrl }).Build();
        var sessions = new AccountSessionService(f.Db, mail, configuration, new PlatformEnvironment(), NullLogger<AccountSessionService>.Instance);
        await Assert.ThrowsAsync<PasswordResetNotConfiguredException>(() => sessions.ForgotAsync(user.Email, default));
        await Assert.ThrowsAsync<PasswordResetNotConfiguredException>(() => sessions.ForgotAsync("unknown@example.com", default));
        Assert.Empty(mail.Messages); Assert.Empty(await f.Db.PasswordResetTokens.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ForgotWithoutResetPageEmailsUsableSingleUseToken(string? resetUrl)
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var mail = new CaptureEmail(); var logs = new SessionLogs();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:PasswordResetUrl"] = resetUrl }).Build();
        var sessions = new AccountSessionService(f.Db, mail, configuration, new PlatformEnvironment(), logs);
        await sessions.ForgotAsync(user.Email, default);
        var html = Assert.Single(mail.Messages);
        Assert.Contains("<code>", html); Assert.DoesNotContain("href=", html);
        var token = mail.LastToken; Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, string.Join("\n", logs.Messages));
        Assert.True(await sessions.ResetAsync(token, "ChangedPass123!", default));
        Assert.False(await sessions.ResetAsync(token, "ChangedPass456!", default));
    }

    [Fact]
    public async Task TokenEmailModeDoesNotSendToUnknownOrLockedAccounts()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var mail = new CaptureEmail();
        (await f.Db.UserAuthStates.SingleAsync()).IsLocked = true; await f.Db.SaveChangesAsync();
        var sessions = new AccountSessionService(f.Db, mail, new ConfigurationBuilder().Build(), new PlatformEnvironment(), NullLogger<AccountSessionService>.Instance);
        await sessions.ForgotAsync("unknown@example.com", default); await sessions.ForgotAsync(user.Email, default);
        Assert.Empty(mail.Messages); Assert.Empty(await f.Db.PasswordResetTokens.ToListAsync());
    }

    [Fact]
    public async Task ForgotRecordsProviderAcceptanceAndCooldownWithoutLoggingTheToken()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var mail = new CaptureEmail(); var logs = new SessionLogs();
        var sessions = new AccountSessionService(f.Db, mail, Config, new PlatformEnvironment(), logs);
        await sessions.ForgotAsync(user.Email, default); await sessions.ForgotAsync(user.Email, default);
        Assert.Single(mail.Messages);
        Assert.Contains(logs.Messages, m => m.Contains("PasswordResetEmailAcceptedByProvider"));
        Assert.Contains(logs.Messages, m => m.Contains("Reason=Cooldown"));
        Assert.DoesNotContain(mail.LastToken, string.Join("\n", logs.Messages));
        Assert.DoesNotContain(user.Email, string.Join("\n", logs.Messages));
    }

    [Fact]
    public async Task SmtpFailureKeepsGenericForgotFlowButLogsSafeStatusForDiagnosis()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var logs = new SessionLogs();
        var sessions = new AccountSessionService(f.Db, new RejectSmtpEmail(), Config, new PlatformEnvironment(), logs);
        await sessions.ForgotAsync(user.Email, default);
        var message = Assert.Single(logs.Messages, m => m.Contains("PasswordResetEmailFailed"));
        Assert.Contains("SmtpStatus=ClientNotPermitted", message);
        Assert.DoesNotContain("private-provider-diagnostic", message);
        Assert.DoesNotContain(user.Email, message);
    }

    [Fact]
    public async Task ExistingAdminWithValidSixCharacterPasswordStillLogsIn()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var user = await f.AddUserAsync("admin", "Admin");
        user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(user, "123456");
        await f.Db.SaveChangesAsync();
        var result = await Auth(f, new CaptureEmail()).LoginAsync(new() { UsernameOrEmail = "admin", Password = "123456" });
        Assert.Equal("Admin", result.Role); Assert.NotEmpty(result.Token);
    }

    [Theory]
    [InlineData("not-a-valid-password-hash")]
    [InlineData("123456")]
    [InlineData("")]
    public async Task InvalidLegacyPasswordHashReturnsUnauthorizedWithoutChangingAccount(string storedHash)
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var user = await f.AddUserAsync("admin", "Admin");
        user.PasswordHash = storedHash; await f.Db.SaveChangesAsync();
        var auth = Auth(f, new CaptureEmail());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new() { UsernameOrEmail = "admin", Password = "123456" }));
        Assert.Equal(storedHash, (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash);
    }

    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Email:VerificationBaseUrl"] = "http://localhost:5080", ["Email:PasswordResetUrl"] = "http://localhost:3000/reset-password" }).Build();
    private static AccountVerificationService Verification(PlatformTestDatabase f, CaptureEmail mail) => new(f.Db, mail, Config, new PlatformEnvironment(), NullLogger<AccountVerificationService>.Instance);
    private static AuthService Auth(PlatformTestDatabase f, CaptureEmail mail) => new(f.Db, Options.Create(new JwtSettings { Key = new string('t', 64) }), NullLogger<AuthService>.Instance, Verification(f, mail));

    [Fact]
    public async Task RegistrationDuplicateVerificationSingleUseAndLogin()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var mail = new CaptureEmail();
        f.Db.SubscriptionPackages.Add(new SubscriptionPackage { Name = "Free", TierLevel = 0 }); await f.Db.SaveChangesAsync();
        var auth = Auth(f, mail); var request = new RegisterRequest { FullName = "Người dùng mới", Email = "USER@example.com", Password = "ValidPass123!" };
        var result = await auth.RegisterAsync(request); Assert.Empty(result.Token); Assert.Equal(request.FullName, result.FullName);
        Assert.NotEqual(request.Password, (await f.Db.Users.SingleAsync()).PasswordHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.RegisterAsync(request));
        var login = new LoginRequest { UsernameOrEmail = "user@example.com", Password = request.Password };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(login));
        Assert.True(await Verification(f, mail).VerifyAsync(mail.LastToken));
        Assert.False(await Verification(f, mail).VerifyAsync(mail.LastToken));
        f.Db.ChangeTracker.Clear();
        Assert.NotEmpty((await auth.LoginAsync(login)).Token);
    }

    [Fact]
    public async Task ExpiredVerificationAndResendThrottle()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var mail = new CaptureEmail(); var user = await f.AddUserAsync(verified: false); var verify = Verification(f, mail);
        Assert.True(await verify.SendAsync(user.Id)); Assert.False(await verify.SendAsync(user.Id));
        Assert.Single(mail.Messages);
        (await f.Db.EmailVerificationTokens.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        Assert.False(await verify.VerifyAsync(mail.LastToken));
    }

    [Fact]
    public async Task PasswordResetExpiresIsSingleUseAndRevokesSessions()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var mail = new CaptureEmail(); var user = await f.AddUserAsync();
        var sessions = new AccountSessionService(f.Db, mail, Config, new PlatformEnvironment(), NullLogger<AccountSessionService>.Instance);
        await sessions.ForgotAsync(user.Email, default); var token = mail.LastToken;
        Assert.True(await sessions.ResetAsync(token, "ChangedPass123!", default));
        Assert.False(await sessions.ResetAsync(token, "ChangedPass456!", default));
        f.Db.ChangeTracker.Clear();
        var auth = Auth(f, mail);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new() { UsernameOrEmail = user.Email, Password = "ValidPass123!" }));
        Assert.NotEmpty((await auth.LoginAsync(new() { UsernameOrEmail = user.Email, Password = "ChangedPass123!" })).Token);
        Assert.Equal(1, (await f.Db.UserAuthStates.SingleAsync()).TokenVersion);
        await f.Db.PasswordResetTokens.ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
        await sessions.ForgotAsync(user.Email, default); token = mail.LastToken;
        await f.Db.PasswordResetTokens.Where(t => t.ConsumedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.False(await sessions.ResetAsync(token, "ChangedPass789!", default));
    }

    [Fact]
    public async Task GoogleCreationAndExplicitLinkCannotMergeAnotherUsersAccount()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var mail = new CaptureEmail();
        f.Db.Add(new SubscriptionPackage { Name = "Free", TierLevel = 0 }); await f.Db.SaveChangesAsync();
        var google = new GoogleAccountService(f.Db, Auth(f, mail));
        var result = await google.LoginAsync(new("google-subject", "google@example.com"), default);
        Assert.NotEmpty(result.Token); Assert.Equal("Customer", result.Role);
        Assert.True((await f.Db.UserAuthStates.SingleAsync()).EmailVerified);
        var existing = await f.AddUserAsync("existing");
        await Assert.ThrowsAsync<InvalidOperationException>(() => google.LoginAsync(new("second-subject", existing.Email), default));
        await google.LinkAsync(existing.Id, new("second-subject", existing.Email), default);
        Assert.Equal(existing.Id, (await google.LoginAsync(new("second-subject", existing.Email), default)).UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => google.LinkAsync(existing.Id, new("google-subject", "google@example.com"), default));
    }

    private static async Task<TokenValidatedContext> Validate(PlatformTestDatabase f, int id, int version)
    {
        var services = new ServiceCollection().AddSingleton<VietVanDbContext>(f.Db).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new TokenValidatedContext(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions()) {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "Admin"), new Claim("token_version", version.ToString()) }, "Bearer")) };
        await LiveAccountValidator.ValidateAsync(context); return context;
    }

    [Fact]
    public async Task AccountLockLogoutAndRoleChangesTakeEffectForOldJwt()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync(); var admin = await f.AddUserAsync("admin", "Admin");
        var context = await Validate(f, user.Id, 0); Assert.Null(context.Result); Assert.False(context.Principal!.IsInRole("Admin"));
        var management = new AdminPlatformService(f.Db);
        await management.LockAsync(admin.Id, user.Id, true, default);
        Assert.NotNull((await Validate(f, user.Id, 0)).Result?.Failure);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Auth(f, new()).LoginAsync(new() { UsernameOrEmail = user.Email, Password = "ValidPass123!" }));
        await management.LockAsync(admin.Id, user.Id, false, default);
        Assert.NotNull((await Validate(f, user.Id, 0)).Result?.Failure);
        Assert.Equal(2, await f.Db.AdminAuditLogs.CountAsync());
        Assert.Null((await Validate(f, user.Id, 2)).Result);
        var session = new AccountSessionService(f.Db, new CaptureEmail(), Config, new PlatformEnvironment(), NullLogger<AccountSessionService>.Instance);
        await session.LogoutAsync(user.Id, default);
        Assert.NotNull((await Validate(f, user.Id, 2)).Result?.Failure);
    }
}
