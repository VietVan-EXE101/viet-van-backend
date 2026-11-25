using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Authorization;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Middlewares;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

public class HttpMediaAccessTests
{
    [Fact]
    public async Task RealHttpRangeAndProRevocationAreEnforced()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var user = await f.AddUserAsync(); var admin = await f.AddUserAsync("admin", "Admin");
        var drama = new DramaProduction { Title = "HTTP" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        static IFormFile File(string name, string type, string hex) => new FormFile(new MemoryStream(Convert.FromHexString(hex)), 0, hex.Length / 2, "file", name) { Headers = new HeaderDictionary(), ContentType = type };
        await legacy.UploadVideoAsync(drama.Id, File("video.mp4", "video/mp4", "000000106674797069736F6D00000000"), default);
        var asset = await legacy.UploadAsync(drama.Id, "Background", "Scene", File("image.png", "image/png", "89504E470D0A1A0A"), default);
        await platform.SaveDraftAsync(drama.Id, new(new() { SchemaVersion = 1, StartSceneId = "one", Scenes = [new() { Id = "one", BackgroundAssetId = asset.Id, DurationMs = 1000 }] }, 0), default);
        await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('s', 64)));
        string Token(User u) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("test", "test",
            [new(ClaimTypes.NameIdentifier, u.Id.ToString()), new(ClaimTypes.Role, u.Role), new("token_version", "0")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256)));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(DramaController).Assembly);
        builder.Services.AddSingleton<VietVanDbContext>(f.Db);
        builder.Services.AddScoped<DramaService>(); builder.Services.AddScoped<DramaPlatformService>(); builder.Services.AddScoped<EntitlementService>();
        builder.Services.Configure<StorageOptions>(o => o.RootPath = Path.Combine(media.Environment.ContentRootPath, "PrivateMedia"));
        builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(o => {
            o.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = "test", ValidateAudience = true, ValidAudience = "test", ValidateIssuerSigningKey = true, IssuerSigningKey = key, ClockSkew = TimeSpan.Zero };
            o.Events = new JwtBearerEvents { OnTokenValidated = LiveAccountValidator.ValidateAsync };
        });
        builder.Services.AddAuthorization(o => o.AddPolicy("Pro", p => p.RequireAuthenticatedUser().AddRequirements(new ProRequirement())));
        builder.Services.AddScoped<IAuthorizationHandler, ProAuthorizationHandler>();
        await using var app = builder.Build(); app.UseMiddleware<ApiExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(user));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/dramas/{drama.Id}/video"); request.Headers.Range = new RangeHeaderValue(0, 3);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(4, (await response.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal(0, response.Content.Headers.ContentRange!.From); Assert.Equal(3, response.Content.Headers.ContentRange.To);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(asset.Url)).StatusCode);
            var package = new SubscriptionPackage { Name = "Paid", TierLevel = 2, DurationInDays = 30, Price = 50000 }; f.Db.Add(package); await f.Db.SaveChangesAsync();
            var sub = new UserSubscription { UserId = user.Id, PackageId = package.Id, IsActive = true, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) }; f.Db.Add(sub); await f.Db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive?preview=true")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(asset.Url)).StatusCode);
            sub.EndDate = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(asset.Url)).StatusCode);
            await new AdminPlatformService(f.Db).LockAsync(admin.Id, user.Id, true, default);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(admin));
            await platform.StatusAsync(drama.Id, "DISABLED", default);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(asset.Url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/dramas/{drama.Id}/interactive?preview=true")).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/dramas/{drama.Id}/video")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
