using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietVan.API.Controllers;
using VietVan.API.Entities;
using VietVan.API.Hosting;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public sealed class DramaMediaInspectionTests
{
    private sealed class HeadOnlyObjects : IR2ObjectClient
    {
        public readonly Dictionary<string, RemoteObject> Files = [];
        public readonly List<string> Heads = [];
        public Task<RemoteObject?> HeadAsync(string key, CancellationToken ct)
        { Heads.Add(key); return Task.FromResult(Files.GetValueOrDefault(key)); }
        public Task<bool> PutFileAsync(string key, string path, string contentType, string sha256, CancellationToken ct) => throw new InvalidOperationException("Inspection must never upload");
        public Task DeleteAsync(string key, CancellationToken ct) => throw new InvalidOperationException("Inspection must never delete");
        public Task<string> PresignGetAsync(string key, DateTimeOffset expiresAt) => throw new InvalidOperationException("Inspection must never sign URLs");
        public Task<string> ReadHashAsync(string key, CancellationToken ct) => throw new InvalidOperationException("Inspection must only HEAD");
    }

    private sealed class CaptureLogger : ILogger<AdminDramaMediaController>
    {
        public readonly List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private const string Draft = "{\"scenes\":[{\"id\":1,\"backgroundAssetId\":114}]}";

    private static async Task Seed(PlatformTestDatabase f, string backgroundKey, string? coverKey = null)
    {
        f.Db.Add(new DramaProduction { Id = 25, Title = "Keep draft" }); await f.Db.SaveChangesAsync();
        f.Db.Add(new DramaAsset { Id = 114, DramaProductionId = 25, Kind = "Background", RelativePath = backgroundKey,
            ContentType = "image/png", SizeBytes = 12 });
        if (coverKey is not null) f.Db.Add(new DramaAsset { Id = 112, DramaProductionId = 25, Kind = "Background",
            Title = DramaService.CoverAssetTitle, RelativePath = coverKey, ContentType = "image/png", SizeBytes = 12 });
        f.Db.Add(new DramaManifest { DramaProductionId = 25, Revision = 5, DraftJson = Draft });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task InspectionReportsR2FilesWithoutChangingDraftRevisionReferencesOrAssetIds()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        const string background = "pro/backgrounds/25/background.png", cover = "free/thumbnails/25/cover.png";
        await Seed(f, background, cover);
        var remote = new HeadOnlyObjects(); remote.Files.Add(background, new(12, "image/png", null)); remote.Files.Add(cover, new(12, "image/png", null));
        var local = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions())); using var output = new StringWriter();
        Assert.Equal(0, await DramaMediaInspection.RunCoreAsync(f.Db, local, remote, "R2", 25, [112, 114], output, default));
        Assert.Contains("DraftRevision=5", output.ToString()); Assert.Contains("FILE_EXISTS_ON_R2", output.ToString());
        Assert.Equal(2, remote.Heads.Count);
        var manifest = await f.Db.DramaManifests.AsNoTracking().SingleAsync(); Assert.Equal(5, manifest.Revision); Assert.Equal(Draft, manifest.DraftJson);
        Assert.Equal(new[] { 112, 114 }, await f.Db.DramaAssets.OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync());
        Assert.Equal(background, await f.Db.DramaAssets.Where(a => a.Id == 114).Select(a => a.RelativePath).SingleAsync());
        using var localOutput = new StringWriter();
        Assert.Equal(1, await DramaMediaInspection.RunCoreAsync(f.Db, local, remote, "Local", 25, [114], localOutput, default));
        Assert.Contains("FILE_EXISTS_ON_R2", localOutput.ToString());
    }

    [Fact]
    public async Task InspectionFindsLocalCopyAndMigrationCandidateButNeverRepairsMetadataAutomatically()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        var local = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        var bytes = Convert.FromHexString("89504E470D0A1A0A00000000");
        var saved = await local.SaveAsync(25, "Background", R2StorageTests.Upload(bytes, "original.png", "image/png"), ".png", 100, default);
        await Seed(f, saved.StorageKey);
        var candidate = "pro/backgrounds/25/114/" + Path.GetFileName(saved.StorageKey);
        var remote = new HeadOnlyObjects(); remote.Files.Add(candidate, new(12, "image/png", null));
        using var output = new StringWriter();
        Assert.Equal(1, await DramaMediaInspection.RunCoreAsync(f.Db, local, remote, "R2", 25, [114], output, default));
        Assert.Contains("EXISTS_REQUIRES_VERIFICATION", output.ToString()); Assert.Contains("LOCAL_COPY_AVAILABLE", output.ToString());
        Assert.Equal(saved.StorageKey, await f.Db.DramaAssets.Where(a => a.Id == 114).Select(a => a.RelativePath).SingleAsync());
        Assert.NotNull(local.ExistingPath(saved.StorageKey)); Assert.Equal(5, (await f.Db.DramaManifests.AsNoTracking().SingleAsync()).Revision);
    }

    [Fact]
    public async Task MissingMetadataAndMismatchedRemoteAreReportedWithoutClaimingRestoreSuccess()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        const string key = "pro/backgrounds/25/background.png"; await Seed(f, key);
        var remote = new HeadOnlyObjects(); remote.Files.Add(key, new(99, "image/jpeg", null));
        var local = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions())); using var output = new StringWriter();
        Assert.Equal(1, await DramaMediaInspection.RunCoreAsync(f.Db, local, remote, "R2", 25, [112, 114], output, default));
        Assert.Contains("asset=112: METADATA_NOT_FOUND_FOR_DRAMA", output.ToString()); Assert.Contains("asset=114: R2=MISMATCH", output.ToString());
        Assert.Single(remote.Heads); Assert.Equal(5, (await f.Db.DramaManifests.AsNoTracking().SingleAsync()).Revision);
    }

    [Fact]
    public async Task AdminMedia404LogsTraceAndDistinguishesMetadataFromMissingFile()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); await Seed(f, "pro/backgrounds/25/background.png");
        var logger = new CaptureLogger(); var storage = new R2FileStorageService(new HeadOnlyObjects(), Options.Create(R2StorageTests.Configuration()));
        var controller = new AdminDramaMediaController(f.Db, storage, logger)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { TraceIdentifier = "media-test-trace" } } };
        Assert.IsType<NotFoundResult>(await controller.Get(25, 114, default));
        Assert.IsType<NotFoundResult>(await controller.Get(25, 112, default));
        Assert.Contains(logger.Messages, m => m.Contains("Reason=FILE_NOT_FOUND") && m.Contains("AssetId=114") && m.Contains("media-test-trace"));
        Assert.Contains(logger.Messages, m => m.Contains("Reason=METADATA_NOT_FOUND") && m.Contains("AssetId=112"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("background.png") || m.Contains("https://"));
    }

    [Fact]
    public async Task RestoringFilesThroughExistingReplacementKeepsAssetIdsDraft5AndSceneBackground114()
    {
        await using var f = await PlatformTestDatabase.CreateAsync(); using var media = new TestMediaDirectory();
        await Seed(f, "drama-assets/25/background/missing-background.png", "drama-assets/25/background/missing-cover.png");
        f.Db.DramaScenes.Add(new DramaScene { DramaProductionId = 25, SceneKey = "scene-1", Title = "Keep scene", BackgroundAssetId = 114 });
        await f.Db.SaveChangesAsync();
        var objects = new MemoryR2(); var storage = new R2FileStorageService(objects, Options.Create(R2StorageTests.Configuration()));
        var service = new DramaService(f.Db, media.Environment, storage);
        var png = Convert.FromHexString("89504E470D0A1A0A00000000");
        Assert.Equal(114, (await service.ReplaceFileAsync(25, 114, R2StorageTests.Upload(png, "original-background.png", "image/png"), default)).Id);
        Assert.Equal(112, (await service.ReplaceFileAsync(25, 112, R2StorageTests.Upload(png, "original-cover.png", "image/png"), default)).Id);
        var manifest = await f.Db.DramaManifests.AsNoTracking().SingleAsync(); Assert.Equal(5, manifest.Revision); Assert.Equal(Draft, manifest.DraftJson);
        Assert.Equal(114, (await f.Db.DramaScenes.AsNoTracking().SingleAsync()).BackgroundAssetId);
        Assert.Single(await f.Db.DramaProductions.AsNoTracking().ToListAsync());
        Assert.Equal(new[] { 112, 114 }, await f.Db.DramaAssets.OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync());
        var admin = new AdminDramaMediaController(f.Db, storage)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<RedirectResult>(await admin.Get(25, 114, default)); Assert.IsType<RedirectResult>(await admin.Get(25, 112, default));
    }

    [Theory]
    [InlineData("25")]
    [InlineData("25:112,-114")]
    [InlineData("0:112,114")]
    [InlineData("25:112,../114")]
    public void InvalidSelectionCannotReachDatabaseOrStorage(string value) =>
        Assert.False(DramaMediaInspection.TrySelection(value, out _, out _));
}
