using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using VietVan.API.Controllers;
using VietVan.API.Controllers.Admin;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

internal sealed class MemoryR2 : IR2ObjectClient
{
    public readonly Dictionary<string, (byte[] Bytes, string Mime)> Files = [];
    public int Puts { get; private set; }
    public int Signs { get; private set; }
    public bool CorruptReads { get; set; }
    public Task<RemoteObject?> HeadAsync(string key, CancellationToken ct) => Task.FromResult<RemoteObject?>(
        Files.TryGetValue(key, out var file) ? new(file.Bytes.Length, file.Mime, null) : null);
    public async Task<bool> PutFileAsync(string key, string path, string contentType, string sha256, CancellationToken ct)
    {
        if (Files.ContainsKey(key)) return false;
        Puts++; Files.Add(key, (await File.ReadAllBytesAsync(path, ct), contentType)); return true;
    }
    public Task DeleteAsync(string key, CancellationToken ct) { Files.Remove(key); return Task.CompletedTask; }
    public Task<string> PresignGetAsync(string key, DateTimeOffset expiresAt)
    { Signs++; return Task.FromResult("https://media.invalid/" + key + "?expires=" + expiresAt.ToUnixTimeSeconds()); }
    public Task<string> ReadHashAsync(string key, CancellationToken ct) => Task.FromResult(CorruptReads ? "wrong" :
        Convert.ToHexString(SHA256.HashData(Files[key].Bytes)).ToLowerInvariant());
}

public class R2StorageTests
{
    [Fact]
    public async Task ConfigurationDiagnosticsOnlyShowFieldNamesAndNeverValues()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["R2:ServiceUrl"] = "https://private-sentinel.invalid/secret" }).Build();
        Assert.Equal(1, await R2SmokeCheck.RunAsync(config, media.Environment, output));
        Assert.Contains("R2:AccessKeyId", output.ToString());
        Assert.Contains("R2:SecretAccessKey", output.ToString());
        Assert.Contains("R2:ServiceUrl", output.ToString());
        Assert.DoesNotContain("private-sentinel", output.ToString());
    }

    [Fact]
    public void StorageErrorDiagnosticsDiscardProviderMessagesAndSecrets()
    {
        var provider = new Amazon.S3.AmazonS3Exception("secret-sentinel and signed URL")
        { StatusCode = System.Net.HttpStatusCode.Forbidden, ErrorCode = "SignatureDoesNotMatch" };
        var safe = R2ObjectClient.Unavailable(provider);
        Assert.Equal(403, safe.HttpStatus); Assert.Equal("SIGNATURE_MISMATCH", safe.FailureCode);
        Assert.Null(safe.InnerException); Assert.DoesNotContain("secret-sentinel", safe.ToString());
        var tls = R2ObjectClient.Unavailable(new HttpRequestException("secret-sentinel", new System.Security.Authentication.AuthenticationException("secret-sentinel")));
        Assert.Equal("TLS_ERROR", tls.FailureCode); Assert.Null(tls.HttpStatus);
        Assert.DoesNotContain("secret-sentinel", tls.ToString());
    }

    internal static readonly byte[] Gif = Convert.FromHexString(
        "47494638396101000100800000FF00000000FF21FF0B4E45545343415045322E300301000000" +
        "21F904000A0000002C000000000100010000020244010021F904000A0000002C00000000010001000002024C01003B");
    internal static IFormFile Upload(byte[] bytes, string name = "animated.gif", string mime = "image/gif") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = mime };
    internal static R2Options Configuration() => new()
    {
        AccountId = new string('a', 32), AccessKeyId = new string('b', 32), SecretAccessKey = new string('c', 64),
        BucketName = "test-private", PublicUrlMinutes = 60, ProtectedUrlMinutes = 5
    };

    [Fact]
    public async Task CloudUploadPreservesAnimatedGifBytesAndCleansStage()
    {
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(Configuration()));
        var stored = await storage.SaveAsync(22, "Character", Upload(Gif), ".gif", 10 * 1024 * 1024, default);
        Assert.StartsWith("pro/characters/22/", stored.StorageKey);
        Assert.Equal(Gif, objects.Files[stored.StorageKey].Bytes); Assert.Equal("image/gif", objects.Files[stored.StorageKey].Mime);
        Assert.Equal(Gif.Length, stored.SizeBytes);
        var until = DateTimeOffset.UtcNow.AddSeconds(30);
        var access = await storage.GetAccessAsync(stored.StorageKey, default, until, protectedAccess: true);
        Assert.NotNull(access); Assert.Null(access!.LocalPath); Assert.True(access.ExpiresAt <= until);
        await storage.DeleteAsync(stored.StorageKey); Assert.Empty(objects.Files);
    }

    [Fact]
    public async Task RejectedMimeSignatureAndSizeNeverReachCloud()
    {
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(Configuration()));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(22, "Character", Upload(Gif, mime: "image/png"), ".gif", 1000, default));
        await Assert.ThrowsAsync<UploadException>(() => storage.SaveAsync(22, "Character", Upload(new byte[20]), ".gif", 1000, default));
        await Assert.ThrowsAsync<UploadException>(() => storage.SaveAsync(22, "Character", Upload(Gif), ".gif", 5, default));
        Assert.Empty(objects.Files);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("pro/assets/../secret")]
    [InlineData("https://example.com/file")]
    public void KeysCannotEscapeStorage(string key) => Assert.Throws<ArgumentException>(() => R2Options.Key(key));

    [Fact]
    public async Task SdkPresignsPrivateObjectWithPathStyleAutoRegionAndExpiryWithoutNetwork()
    {
        var options = Configuration(); using var sdk = R2ObjectClient.Create(options);
        var config = Assert.IsType<Amazon.S3.AmazonS3Config>(sdk.Config);
        Assert.True(config.ForcePathStyle); Assert.Equal("auto", config.AuthenticationRegion);
        var client = new R2ObjectClient(sdk, Options.Create(options));
        var url = new Uri(await client.PresignGetAsync("pro/characters/22/test.gif", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal(options.AccountId + ".r2.cloudflarestorage.com", url.Host);
        Assert.Equal("/test-private/pro/characters/22/test.gif", url.AbsolutePath);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", url.Query);
        Assert.Contains("X-Amz-Signature=", url.Query);
        Assert.DoesNotContain(options.SecretAccessKey, url.AbsoluteUri);
    }

    [Fact]
    public void EndpointCannotSendCredentialsToDifferentHost()
    {
        var options = Configuration(); options.ServiceUrl = "https://example.com";
        Assert.Throws<InvalidOperationException>(() => options.Endpoint());
    }

    [Fact]
    public async Task MigrationDryRunDoesNotWriteAndApplyPreservesIdsAndLocalFilesOnRerun()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var drama = new DramaProduction { Title = "Migration" }; db.Db.Add(drama); await db.Db.SaveChangesAsync();
        var local = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        var stored = await local.SaveAsync(drama.Id, "Character", Upload(Gif), ".gif", 1000, default);
        var asset = new DramaAsset { DramaProductionId = drama.Id, Kind = "Character", Title = "Actor", RelativePath = stored.StorageKey,
            OriginalFileName = stored.OriginalName, ContentType = "image/gif", SizeBytes = Gif.Length };
        db.Db.Add(asset); await db.Db.SaveChangesAsync(); var id = asset.Id; var oldKey = asset.RelativePath;
        var objects = new MemoryR2(); using var output = new StringWriter();
        Assert.Equal(0, await MediaMigration.RunCoreAsync(db.Db, local, null, media.Environment, false, output, default));
        Assert.Empty(objects.Files); Assert.Equal(oldKey, await db.Db.DramaAssets.AsNoTracking().Where(a => a.Id == id).Select(a => a.RelativePath).SingleAsync());
        Assert.Equal(0, await MediaMigration.RunCoreAsync(db.Db, local, objects, media.Environment, true, output, default));
        var row = await db.Db.DramaAssets.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.StartsWith("pro/characters/", row.RelativePath); Assert.Equal(id, row.Id);
        Assert.Equal(Gif, objects.Files[row.RelativePath].Bytes); Assert.NotNull(local.ExistingPath(oldKey));
        Assert.Equal(0, await MediaMigration.RunCoreAsync(db.Db, local, objects, media.Environment, true, output, default));
        Assert.Equal(1, objects.Puts); Assert.Single(await db.Db.DramaAssets.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MigrationRefusesConflictingRemoteBytesWithoutOverwriting()
    {
        using var media = new TestMediaDirectory(); var path = Path.Combine(media.Environment.ContentRootPath, "test.gif");
        await File.WriteAllBytesAsync(path, Gif);
        var objects = new MemoryR2(); objects.Files.Add("pro/assets/test.gif", (new byte[Gif.Length], "image/gif"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaMigration.CopyVerifiedAsync(objects, "pro/assets/test.gif", path, "image/gif", default));
        Assert.Equal(0, objects.Puts); Assert.Equal(new byte[Gif.Length], objects.Files["pro/assets/test.gif"].Bytes);
    }

    [Fact]
    public async Task DraftReplacementPreservesAssetIdAndPublishedSnapshotRefusesReplacement()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(Configuration()));
        var service = new DramaService(f.Db, media.Environment, storage);
        var drama = new DramaProduction { Title = "Replace", Status = "DRAFT" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var original = await service.UploadAsync(drama.Id, "Character", "Actor", Upload(Gif), default);
        var oldKey = await f.Db.DramaAssets.Where(a => a.Id == original.Id).Select(a => a.RelativePath).SingleAsync();
        var replaced = await service.ReplaceFileAsync(drama.Id, original.Id, Upload(Gif, "new.gif"), default);
        Assert.Equal(original.Id, replaced.Id); Assert.False(objects.Files.ContainsKey(oldKey)); Assert.Single(objects.Files);
        Assert.Equal("new.gif", (await f.Db.DramaAssets.SingleAsync()).OriginalFileName);
        drama.IsPublished = true; drama.Status = "PUBLISHED"; await f.Db.SaveChangesAsync();
        var puts = objects.Puts;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplaceFileAsync(drama.Id, original.Id, Upload(Gif), default));
        Assert.Equal(puts, objects.Puts); Assert.Single(objects.Files);
    }

    [Fact]
    public async Task WorkCoverReplacementAndDeletionCleanOnlyItsObjects()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(Configuration()));
        var covers = new WorkCoverStorage(media.Environment, storage, Options.Create(new StorageOptions { Provider = "R2" }));
        var work = new LiteraryWork { Title = "Cover" }; f.Db.Add(work); await f.Db.SaveChangesAsync();
        var controller = new AdminWorkMediaController(f.Db, covers, NullLogger<AdminWorkMediaController>.Instance);
        var png = Convert.FromHexString("89504E470D0A1A0A00000000");
        Assert.IsType<OkObjectResult>(await controller.ReplaceCover(work.Id, Upload(png, "cover.png", "image/png"), default));
        var oldKey = Assert.Single(objects.Files).Key;
        Assert.IsType<OkObjectResult>(await controller.ReplaceCover(work.Id, Upload(png, "next.png", "image/png"), default));
        Assert.False(objects.Files.ContainsKey(oldKey)); Assert.Single(objects.Files);
        var url = await f.Db.LiteraryWorks.AsNoTracking().Select(w => w.CoverImageUrl).SingleAsync();
        Assert.StartsWith("/api/media/work-covers/", url);
        Assert.IsType<NoContentResult>(await controller.DeleteCover(work.Id, default));
        Assert.Empty(objects.Files); Assert.Null(await f.Db.LiteraryWorks.AsNoTracking().Select(w => w.CoverImageUrl).SingleAsync());
    }

    [Fact]
    public async Task AvatarIsOwnedByJwtUserAndReplacingItCleansOldObject()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var owner = await f.AddUserAsync(); var other = await f.AddUserAsync("other");
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(Configuration()));
        var controller = new MediaImagesController(f.Db, storage, NullLogger<MediaImagesController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        void Login(int id) => controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));
        Login(owner.Id);
        var png = Convert.FromHexString("89504E470D0A1A0A00000000");
        Assert.IsType<OkObjectResult>(await controller.SaveAvatar(Upload(png, "avatar.png", "image/png"), default));
        var oldKey = Assert.Single(objects.Files).Key; Assert.StartsWith("users/avatars/", oldKey);
        Login(other.Id);
        Assert.IsType<NotFoundResult>(await controller.Avatar(default));
        Assert.IsType<NoContentResult>(await controller.DeleteAvatar(default)); Assert.Single(objects.Files);
        Login(owner.Id);
        Assert.IsType<OkObjectResult>(await controller.SaveAvatar(Upload(png, "next.png", "image/png"), default));
        Assert.False(objects.Files.ContainsKey(oldKey)); Assert.Single(objects.Files);
        Assert.IsType<OkObjectResult>(await controller.Avatar(default));
        Assert.IsType<NoContentResult>(await controller.DeleteAvatar(default)); Assert.Empty(objects.Files);
        Assert.Empty(await f.Db.Set<UserAvatar>().ToListAsync());
    }
}
