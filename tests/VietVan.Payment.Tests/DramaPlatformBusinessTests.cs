using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Controllers;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;

internal sealed class TestMediaDirectory : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "vietvan-platform-tests", Guid.NewGuid().ToString("N"));
    public PlatformEnvironment Environment { get; }
    public TestMediaDirectory() { Directory.CreateDirectory(path); Environment = new() { ContentRootPath = path, WebRootPath = Path.Combine(path, "wwwroot") }; }
    public void Dispose() { if (Directory.Exists(path)) Directory.Delete(path, true); }
}

public class DramaPlatformBusinessTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, ManifestValidator.JsonOptions);
    private static IFormFile File(string filename = "movie.mp4", string mime = "video/mp4", string hex = "000000106674797069736F6D00000000") =>
        new FormFile(new MemoryStream(Convert.FromHexString(hex)), 0, hex.Length / 2, "file", filename) { Headers = new HeaderDictionary(), ContentType = mime };
    private static ClaimsPrincipal Principal(User user) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, user.Role)], "test"));
    private static async Task<DramaAsset> Background(PlatformTestDatabase f, int dramaId)
    {
        var asset = new DramaAsset { DramaProductionId = dramaId, Kind = "Background", Title = "Scene", OriginalFileName = "scene.png", RelativePath = "missing.png", ContentType = "image/png", SizeBytes = 8 };
        f.Db.Add(asset); await f.Db.SaveChangesAsync(); return asset;
    }
    private static ManifestDocument Document(int background) => new() { SchemaVersion = 1, StartSceneId = "one", Scenes = [new() { Id = "one", DurationMs = 1000, BackgroundAssetId = background }] };

    [Fact]
    public async Task CreateWithWebpCoverAllowsOmittedWorkAndAccessLevelAndReturnsDraft()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var service = new DramaService(f.Db, media.Environment);
        var result = await service.CreateWithCoverAsync(new()
        {
            Title = "Thầy Bói Xem Voi", Description = "Câu chuyện dân gian", IsPublished = false,
            CoverImage = File("bìa tbxv.webp", "image/webp", "524946460400000057454250")
        }, default);

        Assert.Equal("DRAFT", result.Status); Assert.False(result.IsPublished);
        Assert.Null(result.LiteraryWorkId); Assert.Equal("Public", result.AccessLevel);
        Assert.NotNull(result.CoverImage); Assert.Equal("bìa tbxv.webp", result.CoverImage.OriginalFileName);
        Assert.Equal("image/webp", result.CoverImage.ContentType);
        Assert.Empty(result.Scenes); Assert.Single(result.Assets);
        var asset = await f.Db.DramaAssets.AsNoTracking().SingleAsync();
        var storage = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        Assert.NotNull(storage.ExistingPath(asset.RelativePath));
        Assert.Single(await f.Db.DramaProductions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateWithCoverRollsBackDramaAndCleansFileWhenDatabaseRejectsAsset()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_cover BEFORE INSERT ON DramaAssets BEGIN SELECT RAISE(ABORT, 'test database failure'); END;");
        var service = new DramaService(f.Db, media.Environment);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateWithCoverAsync(new()
        {
            Title = "Thầy Bói Xem Voi", IsPublished = false,
            CoverImage = File("bìa tbxv.webp", "image/webp", "524946460400000057454250")
        }, default));

        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.DramaProductions.ToListAsync());
        Assert.Empty(await f.Db.DramaAssets.ToListAsync());
        Assert.Empty(Directory.GetFiles(media.Environment.ContentRootPath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewDramaCanBeCreatedOpenedAndPublishedWithoutLegacySceneProjection(bool withCover)
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var existing = new DramaProduction { Title = "Existing drama" };
        f.Db.Add(existing); await f.Db.SaveChangesAsync();
        f.Db.Add(new DramaScene { DramaProductionId = existing.Id, SceneKey = "existing", Title = "Existing scene", StartMs = 10, EndMs = 1010 });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();

        // Reproduce the missing DramaScenes columns named in the user's SQL 207 log.
        // Keep a row belonging to another drama to catch an incorrectly global Any().
        await f.Db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE DramaScenes RENAME COLUMN SceneKey TO Legacy_SceneKey;
            ALTER TABLE DramaScenes RENAME COLUMN Title TO Legacy_Title;
            ALTER TABLE DramaScenes RENAME COLUMN StartMs TO Legacy_StartMs;
            ALTER TABLE DramaScenes RENAME COLUMN EndMs TO Legacy_EndMs;
            ALTER TABLE DramaScenes RENAME COLUMN AudioOffsetMs TO Legacy_AudioOffsetMs;
            ALTER TABLE DramaScenes RENAME COLUMN BackgroundAssetId TO Legacy_BackgroundAssetId;
            ALTER TABLE DramaScenes RENAME COLUMN AudioAssetId TO Legacy_AudioAssetId;
            ALTER TABLE DramaScenes RENAME COLUMN VideoAssetId TO Legacy_VideoAssetId;
            """);

        var service = new DramaService(f.Db, media.Environment);
        var platform = new DramaPlatformService(f.Db, service);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            f.Db.DramaScenes.AsNoTracking().Where(x => x.DramaProductionId == int.MaxValue).ToListAsync());
        var result = withCover
            ? await service.CreateWithCoverAsync(new() { Title = "Thầy Bói Xem Voi", IsPublished = false,
                CoverImage = File("bìa tbxv.webp", "image/webp", "524946460400000057454250") }, default)
            : await service.CreateAsync(new() { Title = "Thầy Bói Xem Voi", IsPublished = false });
        Assert.Equal("DRAFT", result.Status); Assert.Empty(result.Scenes);
        Assert.Equal(withCover, result.CoverImage is not null);
        Assert.Equal(withCover ? 1 : 0, result.Assets.Count);
        Assert.Empty((await service.DetailAsync(result.Id, true)).Scenes);
        Assert.Equal(0, Json(await platform.DraftAsync(result.Id, default)).GetProperty("revision").GetInt32());

        if (result.CoverImage is not null)
        {
            var background = await service.UploadAsync(result.Id, "Background", "Scene", File("scene.png", "image/png", "89504E470D0A1A0A"), default);
            await platform.SaveDraftAsync(result.Id, new(Document(background.Id), 0), default);
        }
        await service.UploadVideoAsync(result.Id, File(), default);
        await platform.StatusAsync(result.Id, "PUBLISHED", default);
        Assert.True((await service.DetailAsync(result.Id, true)).IsPublished);
        var published = Json(await platform.DetailAsync(result.Id, null, false, false, default));
        Assert.True(published.GetProperty("hasFreeVideo").GetBoolean());
        Assert.Equal(withCover, published.GetProperty("hasInteractive").GetBoolean());
        if (withCover)
            Assert.Equal(1, Json(await platform.InteractiveAsync(result.Id, null, true, default)).GetProperty("manifest").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, await f.Db.DramaScenes.CountAsync(x => x.DramaProductionId == existing.Id));

        // Genuine schema incompatibility for a drama WITH legacy scenes is still surfaced.
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.DetailAsync(existing.Id, true));
    }

    [Fact]
    public async Task FreeVideoPublishesWithoutScenesAndUsesRangeProcessing()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var user = await f.AddUserAsync(); var drama = new DramaProduction { Title = "Video only" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        await legacy.UploadVideoAsync(drama.Id, File(), default); await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        Assert.Empty(await f.Db.DramaScenes.ToListAsync());
        var storage = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        var controller = new DramaMediaController(f.Db, new EntitlementService(f.Db), platform, storage) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = Principal(user) } } };
        var result = Assert.IsType<PhysicalFileResult>(await controller.Video(drama.Id, default)); Assert.True(result.EnableRangeProcessing);
        Assert.Single(await f.Db.DramaViewings.ToListAsync());
        var detail = Json(await platform.DetailAsync(drama.Id, user.Id, false, false, default));
        Assert.True(detail.GetProperty("hasFreeVideo").GetBoolean()); Assert.Empty(detail.GetProperty("assets").EnumerateArray()); Assert.Empty(detail.GetProperty("scenes").EnumerateArray());
        await platform.StatusAsync(drama.Id, "DISABLED", default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.Video(drama.Id, default));
    }

    [Fact]
    public async Task DraftAndPublishedManifestsAreIsolatedAndAssetsAreProtected()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var drama = new DramaProduction { Title = "Interactive" }; f.Db.Add(drama); await f.Db.SaveChangesAsync(); var background = await Background(f, drama.Id);
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        var doc = Document(background.Id);
        await platform.SaveDraftAsync(drama.Id, new(doc, 0), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.SaveDraftAsync(drama.Id, new(doc, 0), default));
        await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        var row = await f.Db.DramaManifests.SingleAsync(); var revision = row.Revision;
        doc.Scenes[0].DurationMs = 5000;
        await platform.SaveDraftAsync(drama.Id, new(doc, revision), default);
        var customer = Json(await platform.InteractiveAsync(drama.Id, null, false, default));
        Assert.Equal(1000, customer.GetProperty("manifest").GetProperty("scenes")[0].GetProperty("durationMs").GetInt32());
        var preview = Json(await platform.InteractiveAsync(drama.Id, null, true, default, preview: true));
        Assert.Equal(5000, preview.GetProperty("manifest").GetProperty("scenes")[0].GetProperty("durationMs").GetInt32());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => platform.InteractiveAsync(drama.Id, null, false, default, preview: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.DeleteAssetAsync(drama.Id, background.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.MetadataAsync(drama.Id, background.Id, new("Changed", null, null, null, null), default));
        await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        customer = Json(await platform.InteractiveAsync(drama.Id, null, false, default));
        Assert.Equal(5000, customer.GetProperty("manifest").GetProperty("scenes")[0].GetProperty("durationMs").GetInt32());
        await platform.StatusAsync(drama.Id, "DISABLED", default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => platform.InteractiveAsync(drama.Id, null, false, default));
        Assert.NotNull(await platform.FindAsync(drama.Id, true, default));
    }

    [Fact]
    public async Task LegacyEditingPreservesPublishedTimestampsAndActionMapping()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var drama = new DramaProduction { Title = "Existing" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        var first = await legacy.AddSceneAsync(drama.Id, new() { SceneKey = "first", Title = "First", StartMs = 500, EndMs = 1500 });
        var second = await legacy.AddSceneAsync(drama.Id, new() { SceneKey = "second", Title = "Second", StartMs = 1500, EndMs = 2500 });
        await legacy.AddHotspotAsync(drama.Id, first.Id, new() { NameVi = "Vật", MeaningVi = "Ý nghĩa", Kind = "Object", X = 10, Y = 10, Width = 20, Height = 20, ActionKey = "custom:action", TargetSegmentIds = [second.Id] });
        await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        await legacy.UpdateSceneAsync(drama.Id, first.Id, new() { SceneKey = "first", Title = "Draft first", StartMs = 0, EndMs = 3000 });
        var snapshot = await platform.LegacySnapshotAsync(drama.Id, false, default);
        Assert.Equal(500, snapshot.Scenes[0].StartMs); Assert.Equal(1500, snapshot.Scenes[0].EndMs);
        Assert.Equal("custom:action", snapshot.Scenes[0].Hotspots[0].ActionKey); Assert.Equal(second.Id, snapshot.Scenes[0].Hotspots[0].TargetSegmentIds[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.DeleteSceneAsync(drama.Id, second.Id));
    }

    [Fact]
    public async Task FreeAndExpiredUsersCannotDownloadProAssetsButAdminCanPreview()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory(); var user = await f.AddUserAsync();
        var drama = new DramaProduction { Title = "Pro only" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        var asset = await legacy.UploadAsync(drama.Id, "Background", "Scene", File("scene.png", "image/png", "89504E470D0A1A0A"), default);
        await platform.SaveDraftAsync(drama.Id, new(Document(asset.Id), 0), default); await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        var storage = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions())); var entitlement = new EntitlementService(f.Db);
        var controller = new DramaMediaController(f.Db, entitlement, platform, storage) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = Principal(user) } } };
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetMedia(drama.Id, asset.Id, default)).StatusCode);
        var pkg = new SubscriptionPackage { Name = "Existing", TierLevel = 1, Price = 10000, DurationInDays = 30 }; f.Db.Add(pkg); await f.Db.SaveChangesAsync();
        var sub = new UserSubscription { UserId = user.Id, PackageId = pkg.Id, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1), IsActive = true }; f.Db.Add(sub); await f.Db.SaveChangesAsync();
        Assert.IsType<PhysicalFileResult>(await controller.GetMedia(drama.Id, asset.Id, default));
        sub.EndDate = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetMedia(drama.Id, asset.Id, default)).StatusCode);
        var admin = await f.AddUserAsync("admin", "Admin"); controller.HttpContext.User = Principal(admin);
        await platform.StatusAsync(drama.Id, "DISABLED", default);
        Assert.IsType<PhysicalFileResult>(await controller.GetMedia(drama.Id, asset.Id, default));
    }

    [Fact]
    public async Task FeedbackNeedsServerViewIsUniqueAndCannotBeEditedByOtherUsers()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory(); var user = await f.AddUserAsync(); var other = await f.AddUserAsync("other");
        var drama = new DramaProduction { Title = "Feedback" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment); var platform = new DramaPlatformService(f.Db, legacy);
        await legacy.UploadVideoAsync(drama.Id, File(), default); await platform.StatusAsync(drama.Id, "PUBLISHED", default);
        var feedback = new FeedbackService(f.Db, new EntitlementService(f.Db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => feedback.SaveAsync(drama.Id, user.Id, 5, null, false, default));
        await platform.RecordViewAsync(drama.Id, user.Id, "FREE_VIDEO", default);
        await feedback.SaveAsync(drama.Id, user.Id, 5, null, false, default);
        await feedback.SaveAsync(drama.Id, user.Id, 4, "Updated", false, default);
        var row = await f.Db.Set<Review>().SingleAsync(); Assert.Equal(4, row.Rating);
        await Assert.ThrowsAsync<InvalidOperationException>(() => feedback.SaveAsync(drama.Id, user.Id, 5, null, true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => feedback.UpdateAsync(row.Id, other.Id, 1, "Hijack", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => feedback.DeleteAsync(null, row.Id, other.Id, default));
        await Assert.ThrowsAsync<ArgumentException>(() => feedback.SaveAsync(drama.Id, user.Id, 0, null, false, default));
        await Assert.ThrowsAsync<ArgumentException>(() => feedback.SaveAsync(drama.Id, user.Id, 6, null, false, default));
        await feedback.DeleteAsync(drama.Id, null, user.Id, default); Assert.Empty(await f.Db.Set<Review>().ToListAsync());
    }

    [Fact]
    public async Task BatchUploadFailureCleansEarlierFilesAndMetadata()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var drama = new DramaProduction { Title = "Batch" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var legacy = new DramaService(f.Db, media.Environment);
        await Assert.ThrowsAsync<UploadException>(() => legacy.UploadBatchAsync(drama.Id, new() { AssetType = "BACKGROUND", Files = [File("valid.png", "image/png", "89504E470D0A1A0A"), File("bad.exe", "application/octet-stream")] }, default));
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.DramaAssets.ToListAsync());
        Assert.Empty(Directory.GetFiles(media.Environment.ContentRootPath, "*", SearchOption.AllDirectories));
        var storage = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        Assert.Null(storage.ExistingPath("../outside.mp4")); Assert.Null(storage.ExistingPath("C:/outside.mp4"));
    }
}
