using VietVan.API.Entities;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class MediaStorageAuditTests
{
    [Fact]
    public async Task AuditsAllLinkedAssetsAndProducesStableFingerprintWithoutWritingOrSigning()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var drama = new DramaProduction { Title = "Audit" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        var key = $"pro/backgrounds/{drama.Id}/original.gif";
        f.Db.Add(new DramaAsset { DramaProductionId = drama.Id, Kind = "Background", RelativePath = key,
            ContentType = "image/gif", SizeBytes = R2StorageTests.Gif.Length });
        var manifest = new DramaManifest { DramaProductionId = drama.Id, Revision = 5, DraftJson = "{\"scene\":1}" };
        f.Db.Add(manifest); await f.Db.SaveChangesAsync();
        var objects = new MemoryR2(); objects.Files.Add(key, (R2StorageTests.Gif, "image/gif"));
        using var first = new StringWriter(); using var second = new StringWriter();
        Assert.Equal(0, await MediaStorageAudit.RunCoreAsync(f.Db, objects, first, default));
        Assert.Equal(0, await MediaStorageAudit.RunCoreAsync(f.Db, objects, second, default));
        Assert.Equal(first.ToString(), second.ToString());
        Assert.Contains("matched=1; missing=0", first.ToString());
        Assert.Equal(0, objects.Puts); Assert.Equal(0, objects.Signs); Assert.Single(objects.Files);
        Assert.Equal(5, manifest.Revision); Assert.Equal("{\"scene\":1}", manifest.DraftJson);
        Assert.False(f.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task FindsMigrationCandidateButNeverAutomaticallyChangesKeyOrLeaksPersistedSignedUrl()
    {
        await using var f = await PlatformTestDatabase.CreateAsync();
        var drama = new DramaProduction { Title = "Legacy" }; f.Db.Add(drama); await f.Db.SaveChangesAsync();
        const string filename = "123456789012345678901234567890ab.gif";
        var legacy = new DramaAsset { DramaProductionId = drama.Id, Kind = "Background",
            RelativePath = $"drama-assets/{drama.Id}/background/{filename}", ContentType = "image/gif", SizeBytes = 10 };
        f.Db.Add(legacy);
        f.Db.Add(new DramaAsset { DramaProductionId = drama.Id, RelativePath = "https://example.invalid/file?X-Amz-Signature=secret-sentinel" });
        await f.Db.SaveChangesAsync();
        var objects = new MemoryR2(); objects.Files.Add($"pro/backgrounds/{drama.Id}/{legacy.Id}/{filename}", (new byte[10], "image/gif"));
        using var output = new StringWriter();
        Assert.Equal(1, await MediaStorageAudit.RunCoreAsync(f.Db, objects, output, default));
        Assert.Contains("migrationCandidates=1", output.ToString()); Assert.Contains("invalidOrUrlKey=1", output.ToString());
        Assert.DoesNotContain("secret-sentinel", output.ToString()); Assert.DoesNotContain("X-Amz-Signature", output.ToString());
        Assert.StartsWith("drama-assets/", legacy.RelativePath);
        Assert.Equal(0, objects.Puts); Assert.Equal(0, objects.Signs); Assert.Single(objects.Files);
        Assert.False(f.Db.ChangeTracker.HasChanges());
    }
}
