using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Controllers.Admin;
using VietVan.API.Data;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;
using VietVan.API.Middlewares;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

public class DramaGifAssetTests
{
    // Two 1x1 frames (red/blue), 100 ms per frame, infinite loop.
    private static readonly byte[] AnimatedGif = Convert.FromHexString(
        "47494638396101000100800000FF00000000FF" +
        "21FF0B4E45545343415045322E300301000000" +
        "21F904000A0000002C0000000001000100000202440100" +
        "21F904000A0000002C00000000010001000002024C01003B");

    private static MultipartFormDataContent Upload(byte[] bytes, string filename, string mime, string kind, bool batch = false)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), batch ? "assetType" : "kind");
        if (!batch) form.Add(new StringContent("Animated character"), "title");
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, batch ? "files" : "file", filename);
        return form;
    }

    [Fact]
    public async Task GifUploadAndMediaPreserveAnimationAndEnforceFormatAndSize()
    {
        await using var database = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var admin = await database.AddUserAsync("admin", "Admin");
        var drama = new DramaProduction { Title = "Animated assets" }; database.Db.Add(drama); await database.Db.SaveChangesAsync();
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("test", "test",
            [new(ClaimTypes.NameIdentifier, admin.Id.ToString()), new(ClaimTypes.Role, "Admin"), new("token_version", "0")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256)));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminDramasController).Assembly);
        builder.Services.AddSingleton<VietVanDbContext>(database.Db);
        builder.Services.AddScoped<DramaService>(); builder.Services.AddScoped<DramaPlatformService>(); builder.Services.AddScoped<EntitlementService>();
        builder.Services.Configure<StorageOptions>(o => o.RootPath = Path.Combine(media.Environment.ContentRootPath, "PrivateMedia"));
        builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new() { ValidIssuer = "test", ValidAudience = "test", IssuerSigningKey = key, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.Zero };
            o.Events = new() { OnTokenValidated = LiveAccountValidator.ValidateAsync };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("drama-upload", limiter => { limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); }));
        await using var app = builder.Build(); app.UseMiddleware<ApiExceptionMiddleware>(); app.UseRouting();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var endpoint = $"/api/admin/dramas/{drama.Id}/assets";
            foreach (var (kind, batch) in new[] { ("Character", false), ("Object", true) })
            {
                using var form = Upload(AnimatedGif, batch ? "object.gif" : "character.GIF", "image/gif", kind, batch);
                using var response = await client.PostAsync(endpoint, form); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var asset = batch ? Assert.Single((await response.Content.ReadFromJsonAsync<List<DramaAssetDto>>())!)
                    : (await response.Content.ReadFromJsonAsync<DramaAssetDto>())!;
                Assert.Equal(kind, asset.Kind); Assert.Equal("image/gif", asset.ContentType); Assert.Equal(AnimatedGif.Length, asset.SizeBytes);
                foreach (var url in new[] { asset.Url, $"/api/admin/dramas/{drama.Id}/assets/{asset.Id}/media" })
                {
                    using var downloaded = await client.GetAsync(url);
                    Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
                    Assert.Equal("image/gif", downloaded.Content.Headers.ContentType?.MediaType);
                    // Exact bytes include both frames, timing and loop extension, with no re-encoding.
                    Assert.Equal(AnimatedGif, await downloaded.Content.ReadAsByteArrayAsync());
                }
                var saved = await database.Db.DramaAssets.FindAsync(asset.Id);
                Assert.EndsWith(".gif", saved!.RelativePath);
            }
            foreach (var (bytes, name, mime, expected) in new[]
            {
                (AnimatedGif, "wrong-mime.gif", "image/png", HttpStatusCode.UnsupportedMediaType),
                (AnimatedGif, "wrong-extension.png", "image/gif", HttpStatusCode.UnsupportedMediaType),
                (new byte[16], "fake.gif", "image/gif", HttpStatusCode.UnsupportedMediaType),
                (new byte[10 * 1024 * 1024 + 1], "too-large.gif", "image/gif", HttpStatusCode.RequestEntityTooLarge)
            })
            {
                using var form = Upload(bytes, name, mime, "Character");
                using var response = await client.PostAsync(endpoint, form); Assert.Equal(expected, response.StatusCode);
            }
            using (var coverForm = Upload(AnimatedGif, "cover.gif", "image/gif", "Background"))
            using (var coverResponse = await client.PostAsync($"/api/admin/dramas/{drama.Id}/cover", coverForm))
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, coverResponse.StatusCode);
            Assert.Equal(2, await database.Db.DramaAssets.CountAsync());
            Assert.Equal(2, Directory.GetFiles(Path.Combine(media.Environment.ContentRootPath, "PrivateMedia"), "*", SearchOption.AllDirectories).Length);
        }
        finally { await app.StopAsync(); }
    }
}
