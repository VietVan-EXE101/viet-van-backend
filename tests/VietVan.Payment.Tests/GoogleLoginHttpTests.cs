using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Middlewares;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Settings;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class GoogleLoginHttpTests
{
    private const string FrontendOrigin = "http://localhost:3001";
    private const string ClientId = "google-http-tests.apps.googleusercontent.com";

    private sealed class FixedGoogleKeys(SecurityKey key) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var configuration = new OpenIdConnectConfiguration(); configuration.SigningKeys.Add(key);
            return Task.FromResult(configuration);
        }
        public void RequestRefresh() { }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public PlatformTestDatabase Database { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public CookieContainer Cookies { get; } = new();
        public HttpClient Client { get; private set; } = null!;
        public RSA GoogleRsa { get; } = RSA.Create(2048);
        private string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        public SymmetricSecurityKey JwtKey => new(Encoding.UTF8.GetBytes(JwtSecret));
        public const string JwtIssuer = "VietVan.GoogleHttpTests";
        public const string JwtAudience = "VietVan.TestFrontend";

        public static async Task<Fixture> CreateAsync(bool render = false)
        {
            var fixture = new Fixture { Database = await PlatformTestDatabase.CreateAsync() };
            fixture.Database.Db.SubscriptionPackages.Add(new SubscriptionPackage { Name = "Free", TierLevel = 0 });
            await fixture.Database.Db.SaveChangesAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = render ? "Production" : "Development" });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            // Load the real Development CORS settings shipped by the API project.
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"));
            builder.Services.AddControllers().AddApplicationPart(typeof(GoogleAuthController).Assembly);
            builder.Services.AddSingleton<VietVanDbContext>(fixture.Database.Db);
            builder.Services.AddSingleton<GoogleChallengeStore>();
            builder.Services.Configure<GoogleSignInOptions>(o => { o.ClientId = ClientId; o.ChallengeCookieSameSite = render ? SameSiteMode.None : SameSiteMode.Strict; });
            if (render) builder.Services.Configure<ForwardedHeadersOptions>(DeploymentConfiguration.ConfigureRenderProxy);
            builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(new FixedGoogleKeys(new RsaSecurityKey(fixture.GoogleRsa) { KeyId = "test-google-key" }));
            builder.Services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
            builder.Services.AddScoped<GoogleAccountService>(); builder.Services.AddScoped<AuthService>();
            builder.Services.AddScoped<AccountVerificationService>(); builder.Services.AddSingleton<IEmailSender>(new CaptureEmail());
            builder.Services.Configure<JwtSettings>(o => { o.Key = fixture.JwtSecret; o.Issuer = JwtIssuer; o.Audience = JwtAudience; o.ExpiryMinutes = 30; });
            builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
            {
                o.TokenValidationParameters = fixture.JwtValidation();
                o.Events = new JwtBearerEvents { OnTokenValidated = LiveAccountValidator.ValidateAsync };
            });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("auth-sensitive", limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); }));
            builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
            {
                var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
                if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            }));
            fixture.App = builder.Build();
            if (render) fixture.App.UseForwardedHeaders();
            fixture.App.UseMiddleware<ApiExceptionMiddleware>(); fixture.App.UseRouting(); fixture.App.UseCors();
            fixture.App.UseAuthentication(); fixture.App.UseAuthorization(); fixture.App.UseRateLimiter(); fixture.App.MapControllers();
            fixture.App.MapGet("/test/google-jwt", (HttpContext context) => Results.Ok(new { userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier), role = context.User.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();
            await fixture.App.StartAsync();
            var address = fixture.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var uri = new UriBuilder(address) { Host = "localhost" }.Uri;
            fixture.Client = new HttpClient(new HttpClientHandler { CookieContainer = fixture.Cookies }) { BaseAddress = uri };
            fixture.Client.DefaultRequestHeaders.Add("Origin", FrontendOrigin);
            return fixture;
        }

        public TokenValidationParameters JwtValidation() => new()
        {
            ValidateIssuer = true, ValidIssuer = JwtIssuer, ValidateAudience = true, ValidAudience = JwtAudience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = JwtKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        };

        public string GoogleToken(string nonce, string audience = ClientId) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "https://accounts.google.com", audience,
            [new("sub", "google-http-subject"), new("email", "google-http@example.test"), new("email_verified", "true"), new("nonce", nonce)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new RsaSecurityKey(GoogleRsa) { KeyId = "test-google-key" }, SecurityAlgorithms.RsaSha256)));

        public async Task<(string Nonce, string Cookie, string Header)> ChallengeAsync()
        {
            using var response = await Client.PostAsync("/api/auth/google/challenge", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(ClientId, json.RootElement.GetProperty("clientId").GetString());
            var cookie = Cookies.GetCookies(new Uri(Client.BaseAddress!, "/api/auth/google"))["vv_google_challenge"];
            Assert.NotNull(cookie);
            return (json.RootElement.GetProperty("nonce").GetString()!, cookie.Value, response.Headers.GetValues("Set-Cookie").Single());
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
            if (Database is not null) await Database.DisposeAsync();
            GoogleRsa.Dispose();
        }
    }

    [Fact]
    public async Task LocalhostCorsPreflightAllowsCredentialedFrontendAndDeniesOtherOrigins()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var endpoint in new[] { "/api/auth/google/challenge", "/api/auth/google" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, endpoint);
            request.Headers.Add("Access-Control-Request-Method", "POST"); request.Headers.Add("Access-Control-Request-Headers", "content-type");
            using var response = await f.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        }
        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/auth/google/challenge");
        denied.Headers.Add("Origin", "http://localhost:3002"); denied.Headers.Add("Access-Control-Request-Method", "POST");
        using var deniedResponse = await f.Client.SendAsync(denied);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RenderHttpsProxyAllowsSecureCrossSiteGoogleCookieAndStillRejectsPlainHttp()
    {
        await using var fixture = await Fixture.CreateAsync(render: true);
        using var plain = await fixture.Client.PostAsync("/api/auth/google/challenge", null);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.False(plain.Headers.Contains("Set-Cookie"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/challenge");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.12");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task ChallengeCookieAndVerifiedGoogleLoginProduceUsableVietVanJwtAndRejectReplay()
    {
        await using var f = await Fixture.CreateAsync(); var challenge = await f.ChallengeAsync();
        Assert.Contains("httponly", challenge.Header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", challenge.Header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth/google", challenge.Header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", challenge.Header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", challenge.Header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", challenge.Header, StringComparison.OrdinalIgnoreCase);
        var googleToken = f.GoogleToken(challenge.Nonce);
        using var response = await f.Client.PostAsJsonAsync("/api/auth/google", new { idToken = googleToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("token").GetString()!;
        Assert.Equal("Customer", body.RootElement.GetProperty("role").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("subscriptionTierLevel").GetInt32());
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, f.JwtValidation(), out var validated);
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JwtSecurityToken)validated).Header.Alg);
        Assert.Equal("0", principal.FindFirst("token_version")?.Value);
        Assert.Equal(body.RootElement.GetProperty("userId").GetInt32().ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True((await f.Database.Db.UserAuthStates.SingleAsync()).EmailVerified);
        using var probe = new HttpRequestMessage(HttpMethod.Get, "/test/google-jwt"); probe.Headers.Authorization = new("Bearer", token);
        using var probeResponse = await f.Client.SendAsync(probe); Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);

        f.Cookies.SetCookies(new Uri(f.Client.BaseAddress!, "/api/auth/google"), $"vv_google_challenge={challenge.Cookie}; Path=/api/auth/google");
        using var replay = await f.Client.PostAsJsonAsync("/api/auth/google", new { idToken = googleToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(1, await f.Database.Db.Users.CountAsync());
        Assert.Equal(1, await f.Database.Db.UserSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData("missing-cookie")]
    [InlineData("wrong-nonce")]
    [InlineData("wrong-audience")]
    [InlineData("client-id-as-token")]
    public async Task InvalidGoogleRequestsNeverIssueLocalJwtOrCreateAccounts(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        var nonce = failure == "missing-cookie" ? "nonce-without-cookie" : (await f.ChallengeAsync()).Nonce;
        var token = failure == "client-id-as-token" ? ClientId : f.GoogleToken(failure == "wrong-nonce" ? "other-nonce" : nonce, failure == "wrong-audience" ? "other-client" : ClientId);
        using var response = await f.Client.PostAsJsonAsync("/api/auth/google", new { idToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await f.Database.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task LockedGoogleAccountIsRejectedWith401()
    {
        await using var f = await Fixture.CreateAsync(); var first = await f.ChallengeAsync();
        using var created = await f.Client.PostAsJsonAsync("/api/auth/google", new { idToken = f.GoogleToken(first.Nonce) });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        (await f.Database.Db.UserAuthStates.SingleAsync()).IsLocked = true; await f.Database.Db.SaveChangesAsync();
        var second = await f.ChallengeAsync();
        using var rejected = await f.Client.PostAsJsonAsync("/api/auth/google", new { idToken = f.GoogleToken(second.Nonce) });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Theory]
    [InlineData("Development", "http", 200, false)]
    [InlineData("Production", "https", 200, true)]
    [InlineData("Production", "http", 400, false)]
    public void ChallengeCookieUsesSecureOnHttpsAndRejectsProductionHttp(string environment, string scheme, int status, bool secure)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = scheme;
        var controller = new GoogleAuthController(new GoogleChallengeStore(), null!,
            Options.Create(new GoogleSignInOptions { ClientId = ClientId }), null!, new PlatformEnvironment { EnvironmentName = environment })
        { ControllerContext = new ControllerContext { HttpContext = context } };
        var result = Assert.IsAssignableFrom<ObjectResult>(controller.CreateChallenge());
        Assert.Equal(status, result.StatusCode);
        var cookie = context.Response.Headers.SetCookie.ToString();
        if (status == 400) Assert.Empty(cookie);
        else
        {
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(secure, cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
        }
    }
}
