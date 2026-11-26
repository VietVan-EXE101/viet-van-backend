using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Authorization;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Middlewares;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public class R2MediaAuthorizationTests
{
    [Fact]
    public async Task HttpChecksPublicationRoleAndCurrentSubscriptionBeforeIssuingUrl()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var user = await f.AddUserAsync(); var admin = await f.AddUserAsync("cloud-admin", "Admin");
        var drama = new DramaProduction { Title = "Cloud", IsPublished = true, Status = "PUBLISHED" };
        f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var character = new DramaAsset { DramaProductionId = drama.Id, Kind = "Character", Title = "Actor",
            RelativePath = "pro/characters/actor.gif", ContentType = "image/gif", SizeBytes = R2StorageTests.Gif.Length };
        var video = new DramaAsset { DramaProductionId = drama.Id, Kind = "Video", Title = "FREE_VIDEO",
            RelativePath = "free/videos/play.mp4", ContentType = "video/mp4", SizeBytes = 16 };
        f.Db.AddRange(character, video); await f.Db.SaveChangesAsync();
        drama.FreeVideoAssetId = video.Id; await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment);
        f.Db.DramaManifests.Add(new DramaManifest { DramaProductionId = drama.Id,
            PublishedLegacyJson = JsonSerializer.Serialize(await legacy.DetailAsync(drama.Id, true), ManifestValidator.JsonOptions) });
        await f.Db.SaveChangesAsync();
        var objects = new MemoryR2(); objects.Files.Add(character.RelativePath, (R2StorageTests.Gif, "image/gif"));
        objects.Files.Add(video.RelativePath, (new byte[16], "video/mp4"));
        var key = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        string Token(User account) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("test", "test",
            [new(ClaimTypes.NameIdentifier, account.Id.ToString()), new(ClaimTypes.Role, account.Role), new("token_version", "0")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256)));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(DramaMediaController).Assembly);
        builder.Services.AddSingleton<VietVanDbContext>(f.Db);
        builder.Services.AddScoped<DramaService>(); builder.Services.AddScoped<DramaPlatformService>(); builder.Services.AddScoped<EntitlementService>();
        builder.Services.AddSingleton<IR2ObjectClient>(objects);
        builder.Services.AddSingleton<IOptions<R2Options>>(Options.Create(R2StorageTests.Configuration()));
        builder.Services.AddScoped<IFileStorageService, R2FileStorageService>();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new() { ValidIssuer = "test", ValidAudience = "test", IssuerSigningKey = key, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.Zero };
            o.Events = new JwtBearerEvents { OnTokenValidated = LiveAccountValidator.ValidateAsync };
        });
        builder.Services.AddAuthorization(o => o.AddPolicy("Pro", p => p.RequireAuthenticatedUser().AddRequirements(new ProRequirement())));
        builder.Services.AddScoped<IAuthorizationHandler, ProAuthorizationHandler>();
        await using var app = builder.Build(); app.UseMiddleware<ApiExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            var accessUrl = $"/api/dramas/{drama.Id}/assets/{character.Id}/access";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(accessUrl)).StatusCode);
            Assert.Equal(0, objects.Signs);
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/dramas/{drama.Id}/video")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(user));
            var signsBefore = objects.Signs;
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(accessUrl)).StatusCode);
            Assert.Equal(signsBefore, objects.Signs);
            var package = new SubscriptionPackage { Name = "PRO", TierLevel = 1, Price = 10000, DurationInDays = 1 };
            f.Db.Add(package); await f.Db.SaveChangesAsync();
            var until = DateTime.UtcNow.AddSeconds(90);
            var subscription = new UserSubscription { UserId = user.Id, PackageId = package.Id, IsActive = true,
                StartDate = DateTime.UtcNow.AddMinutes(-1), EndDate = until };
            f.Db.Add(subscription); await f.Db.SaveChangesAsync();
            using var granted = await client.GetAsync(accessUrl); Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            using var body = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().UtcDateTime <= until);
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/dramas/{drama.Id}/assets/{character.Id}/media")).StatusCode);
            subscription.EndDate = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
            signsBefore = objects.Signs;
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(accessUrl)).StatusCode); Assert.Equal(signsBefore, objects.Signs);
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(admin));
            drama.Status = "DISABLED"; drama.IsPublished = false; await f.Db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/admin/dramas/{drama.Id}/assets/{character.Id}/media")).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/dramas/{drama.Id}/video")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
