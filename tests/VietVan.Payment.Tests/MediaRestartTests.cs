using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public class MediaRestartTests
{
    [Fact]
    public async Task HttpCoverFreeVideoAndProMediaSurviveFreshHostsWithEmptyLocalDisks()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var customer = await f.AddUserAsync(); var admin = await f.AddUserAsync("restart-admin", "Admin");
        var drama = new DramaProduction { Title = "Restart", IsPublished = true, Status = "PUBLISHED" };
        f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var assets = new[]
        {
            new DramaAsset { Kind = "Background", Title = DramaService.CoverAssetTitle, RelativePath = "free/thumbnails/25/cover.png", ContentType = "image/png" },
            new DramaAsset { Kind = "Video", Title = "FREE_VIDEO", RelativePath = "free/videos/25/video.mp4", ContentType = "video/mp4" },
            new DramaAsset { Kind = "Background", RelativePath = "pro/backgrounds/25/scene.gif", ContentType = "image/gif" },
            new DramaAsset { Kind = "Character", RelativePath = "pro/characters/25/actor.gif", ContentType = "image/gif" },
            new DramaAsset { Kind = "Object", RelativePath = "pro/assets/25/object.png", ContentType = "image/png" },
            new DramaAsset { Kind = "Audio", RelativePath = "pro/audio/25/voice.mp3", ContentType = "audio/mpeg" }
        };
        foreach (var asset in assets) { asset.DramaProductionId = drama.Id; asset.SizeBytes = 32; }
        f.Db.AddRange(assets); await f.Db.SaveChangesAsync();
        drama.FreeVideoAssetId = assets[1].Id;
        var snapshot = new VietVan.API.DTOs.Drama.DramaDto(drama.Id, null, drama.Title, null, true,
            assets.Select(DramaPlatformService.AssetDto).ToList(), []);
        var savedJson = JsonSerializer.Serialize(snapshot, ManifestValidator.JsonOptions);
        var manifest = new DramaManifest { DramaProductionId = drama.Id, Revision = 5, PublishedLegacyJson = savedJson };
        f.Db.Add(manifest);
        var package = new SubscriptionPackage { Name = "PRO", TierLevel = 1, DurationInDays = 1, Price = 1 };
        f.Db.Add(package); await f.Db.SaveChangesAsync();
        f.Db.Add(new UserSubscription { UserId = customer.Id, PackageId = package.Id, IsActive = true,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddHours(1) });
        await f.Db.SaveChangesAsync();
        var objects = new MemoryR2();
        foreach (var asset in assets) objects.Files.Add(asset.RelativePath, (new byte[32], asset.ContentType));
        var keysBefore = assets.Select(a => a.RelativePath).ToArray();
        var jwtKey = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        string Token(User user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("restart", "restart",
            [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, user.Role), new("token_version", "0")],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new(jwtKey, SecurityAlgorithms.HmacSha256)));

        for (var restart = 0; restart < 2; restart++)
        {
            using var emptyDisk = new TestMediaDirectory();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                { EnvironmentName = "Production", ContentRootPath = emptyDisk.Environment.ContentRootPath });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RENDER"] = "true", ["R2:AccountId"] = new string('a', 32), ["R2:AccessKeyId"] = new string('b', 32),
                ["R2:SecretAccessKey"] = new string('c', 64), ["R2:BucketName"] = "vietvan-media",
                ["R2:ServiceUrl"] = $"https://{new string('a', 32)}.r2.cloudflarestorage.com"
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(DramaMediaController).Assembly);
            builder.Services.AddSingleton<VietVanDbContext>(f.Db);
            builder.Services.AddScoped<DramaService>(); builder.Services.AddScoped<DramaPlatformService>(); builder.Services.AddScoped<EntitlementService>();
            builder.Services.AddMediaStorage(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton<IR2ObjectClient>(objects); // Simulated persistent bucket shared across fresh hosts.
            builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new() { ValidIssuer = "restart", ValidAudience = "restart",
                    IssuerSigningKey = jwtKey, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.Zero };
                options.Events = new JwtBearerEvents { OnTokenValidated = LiveAccountValidator.ValidateAsync };
            });
            builder.Services.AddAuthorization();
            await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            await app.StartAsync();
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(address) };
                foreach (var asset in assets)
                {
                    client.DefaultRequestHeaders.Authorization = null;
                    var url = $"/api/dramas/{drama.Id}/assets/{asset.Id}/access";
                    using var anonymous = await client.GetAsync(url);
                    Assert.Equal(asset == assets[0] || asset == assets[1] ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, anonymous.StatusCode);
                    client.DefaultRequestHeaders.Authorization = new("Bearer", Token(customer));
                    var signsBefore = objects.Signs;
                    using var granted = await client.GetAsync(url);
                    Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
                    Assert.Equal(signsBefore + 1, objects.Signs); // Always sign on request, never reuse a database URL.
                    using var json = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
                    Assert.True(json.RootElement.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
                    Assert.True(granted.Headers.CacheControl!.NoStore);
                    client.DefaultRequestHeaders.Authorization = new("Bearer", Token(admin));
                    using var adminResult = await client.GetAsync($"/api/admin/dramas/{drama.Id}/assets/{asset.Id}/media");
                    Assert.Equal(HttpStatusCode.Found, adminResult.StatusCode);
                }
                client.DefaultRequestHeaders.Authorization = null;
                Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/dramas/{drama.Id}/video")).StatusCode);
                Assert.False(Directory.Exists(Path.Combine(emptyDisk.Environment.ContentRootPath, "PrivateMedia")));
            }
            finally { await app.StopAsync(); }
        }
        Assert.Equal(keysBefore, assets.Select(a => a.RelativePath));
        Assert.Equal(5, manifest.Revision); Assert.Equal(savedJson, manifest.PublishedLegacyJson);
        Assert.Equal(0, objects.Puts); Assert.Equal(6, objects.Files.Count);
    }
}
