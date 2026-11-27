using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VietVan.API.DTOs.Auth;
using VietVan.API.Entities;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Settings;
using Xunit;

namespace VietVan.Payment.Tests;

// Real Gmail sender + auth services, with HTTP and SQLite isolated from every cloud service.
public class GmailAuthenticationFlowTests
{
    private sealed class Google(HttpStatusCode sendStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string To, string Html)> Emails { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token")
                return Response(HttpStatusCode.OK, "{\"access_token\":\"test-access-token\"}");
            Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", request.RequestUri.AbsoluteUri);
            Assert.Equal("Bearer test-access-token", request.Headers.Authorization?.ToString());
            if (sendStatus != HttpStatusCode.OK) return Response(sendStatus, "{\"error\":\"private-google-error\"}");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var raw = json.RootElement.GetProperty("raw").GetString()!.Replace('-', '+').Replace('_', '/');
            raw = raw.PadRight((raw.Length + 3) / 4 * 4, '=');
            var mime = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var parts = mime.Split("\r\n\r\n", 2);
            var to = Regex.Match(parts[0], @"(?m)^To: ([^\r\n]+)").Groups[1].Value;
            Emails.Add((to, Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))));
            return Response(HttpStatusCode.OK, "{\"id\":\"accepted-message\"}");
        }
        private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Email:Provider"] = "Gmail", ["Email:VerificationBaseUrl"] = "https://backend.example.test"
    }).Build();

    private static GmailEmailSender Sender(HttpClient client) => new(client, Options.Create(new GmailEmailOptions
    {
        ClientId = "test-client", ClientSecret = "test-secret", RefreshToken = "test-refresh", FromAddress = "sender@gmail.com"
    }));

    private static AccountVerificationService Verification(PlatformTestDatabase f, IEmailSender sender) =>
        new(f.Db, sender, Config, new PlatformEnvironment { EnvironmentName = "Production" }, NullLogger<AccountVerificationService>.Instance);

    private static AuthService Auth(PlatformTestDatabase f, AccountVerificationService verification) =>
        new(f.Db, Options.Create(new JwtSettings { Key = new string('t', 64) }), NullLogger<AuthService>.Instance, verification);

    [Fact]
    public async Task RegistrationAndForgotBothSendUsableTokensThroughGmailAndRequireEmailProof()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        f.Db.SubscriptionPackages.Add(new SubscriptionPackage { Name = "Free", TierLevel = 0 }); await f.Db.SaveChangesAsync();
        using var google = new Google(); using var http = new HttpClient(google);
        var sender = Sender(http); var verification = Verification(f, sender); var auth = Auth(f, verification);
        var registered = await auth.RegisterAsync(new RegisterRequest { Username = "gmail-user", Email = "reader@example.com", Password = "OriginalPass123!" });
        Assert.True(registered.EmailVerificationRequired); Assert.True(registered.VerificationEmailSent); Assert.Empty(registered.Token);
        var email = Assert.Single(google.Emails);
        Assert.Equal("reader@example.com", email.To);
        var verificationToken = Regex.Match(email.Html, @"verify-email\.html#([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(43, verificationToken.Length);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new() { UsernameOrEmail = email.To, Password = "OriginalPass123!" }));
        Assert.True(await verification.VerifyAsync(verificationToken));
        Assert.False(await verification.VerifyAsync(verificationToken));
        f.Db.ChangeTracker.Clear();
        Assert.NotEmpty((await auth.LoginAsync(new() { UsernameOrEmail = email.To, Password = "OriginalPass123!" })).Token);

        var before = (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash;
        var sessions = new AccountSessionService(f.Db, sender, Config, new PlatformEnvironment { EnvironmentName = "Production" }, NullLogger<AccountSessionService>.Instance);
        await sessions.ForgotAsync(email.To, default);
        Assert.Equal(2, google.Emails.Count);
        var resetEmail = google.Emails[1]; Assert.Equal(email.To, resetEmail.To);
        var resetToken = Regex.Match(resetEmail.Html, @"<code>([A-Za-z0-9_-]{43})</code>").Groups[1].Value;
        Assert.Equal(43, resetToken.Length);
        Assert.Equal(before, (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash);
        Assert.True(await sessions.ResetAsync(resetToken, "ChangedPass123!", default));
        Assert.False(await sessions.ResetAsync(resetToken, "ChangedPass456!", default));
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new() { UsernameOrEmail = email.To, Password = "OriginalPass123!" }));
        Assert.NotEmpty((await auth.LoginAsync(new() { UsernameOrEmail = email.To, Password = "ChangedPass123!" })).Token);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GmailRejectionCannotClaimRegistrationEmailSentOrAllowUnverifiedLogin(HttpStatusCode status)
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        f.Db.SubscriptionPackages.Add(new SubscriptionPackage { Name = "Free", TierLevel = 0 }); await f.Db.SaveChangesAsync();
        using var google = new Google(status); using var http = new HttpClient(google);
        var auth = Auth(f, Verification(f, Sender(http)));
        var response = await auth.RegisterAsync(new RegisterRequest { Username = "gmail-user", Email = "reader@example.com", Password = "OriginalPass123!" });
        Assert.True(response.EmailVerificationRequired); Assert.False(response.VerificationEmailSent); Assert.Empty(response.Token);
        Assert.Empty(google.Emails); Assert.False((await f.Db.UserAuthStates.AsNoTracking().SingleAsync()).EmailVerified);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new() { UsernameOrEmail = "reader@example.com", Password = "OriginalPass123!" }));
    }

    [Fact]
    public async Task FailedGmailResetDeliveryLeavesExistingPasswordAndVerificationUnchanged()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); var user = await f.AddUserAsync();
        using var google = new Google(HttpStatusCode.Forbidden); using var http = new HttpClient(google);
        var sender = Sender(http);
        var before = user.PasswordHash;
        var sessions = new AccountSessionService(f.Db, sender, Config, new PlatformEnvironment { EnvironmentName = "Production" }, NullLogger<AccountSessionService>.Instance);
        await sessions.ForgotAsync(user.Email, default);
        Assert.Empty(google.Emails);
        Assert.Equal(before, (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash);
        Assert.True((await f.Db.UserAuthStates.AsNoTracking().SingleAsync()).EmailVerified);
        Assert.NotEmpty((await Auth(f, Verification(f, sender)).LoginAsync(new() { UsernameOrEmail = user.Email, Password = "ValidPass123!" })).Token);
    }
}
