using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VietVan.API.Hosting;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public class MediaRecoveryPlannerTests
{
    [Theory]
    [InlineData("mp4", "video/mp4", "Video", "000000106674797069736F6D00000000")]
    [InlineData("wav", "audio/wav", "Audio", "524946460400000057415645")]
    public async Task ExactLocalOriginalCanBePlannedAndRestoredAtSameVideoOrAudioKey(string ext, string mime, string kind, string hex)
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath;
        var bytes = Convert.FromHexString(hex);
        var key = $"drama-assets/25/{kind.ToLowerInvariant()}/{new string('a', 32)}.{ext}";
        var path = Path.Combine(root, "PrivateMedia", key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes);
        var asset = new MediaRecoveryPlanner.Metadata(113, 25, kind, "original." + ext, key, mime, bytes.Length);
        var objects = new MemoryR2(); var folder = Path.Combine(root, "plans");
        var result = await MediaRecoveryPlanner.RunCoreAsync([asset], new(media.Environment, Options.Create(new StorageOptions())),
            objects, root, folder, output, default);
        Assert.Equal(0, objects.Puts); Assert.Empty(objects.Files);
        Assert.Equal("READY_LOCAL_EXACT_KEY", Assert.Single(result.Missing).Status);
        var planPath = Path.Combine(root, Assert.Single(result.ReadyPlanFiles));
        var plan = JsonSerializer.Deserialize<DramaMediaRecovery.Plan>(await File.ReadAllTextAsync(planPath))!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), plan.Assets[0].Sha256, ignoreCase: true);
        Assert.Equal(0, await DramaMediaRecovery.RunCoreAsync(plan, root, objects, true, output, default));
        Assert.Equal(bytes, objects.Files[key].Bytes); Assert.Equal(mime, objects.Files[key].Mime);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExistingR2CopyWithSameOriginalNameIsReportedButNotSilentlyApplied()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var objects = new MemoryR2();
        var source = new MediaRecoveryPlanner.Metadata(77, 22, "Audio", "voice.wav", "pro/audio/22/77/old.wav", "audio/wav", 12);
        var target = new MediaRecoveryPlanner.Metadata(170, 25, "Audio", "voice.wav", "drama-assets/25/audio/" + new string('a', 32) + ".wav", "audio/wav", 12);
        objects.Files.Add(source.Key, (new byte[12], source.ContentType));
        var result = await MediaRecoveryPlanner.RunCoreAsync([source, target], new(media.Environment, Options.Create(new StorageOptions())),
            objects, root, Path.Combine(root, "plans"), output, default);
        var missing = Assert.Single(result.Missing);
        Assert.Equal(170, missing.Asset.AssetId); Assert.Equal(1, result.Matched);
        Assert.Equal(77, Assert.Single(missing.Candidates).SourceAssetId);
        Assert.Contains("SAME_ORIGINAL_NAME_SIZE_MIME", missing.Candidates[0].Evidence);
        Assert.Empty(result.ReadyPlanFiles); Assert.Equal(0, objects.Puts); Assert.Equal(0, objects.Signs);
        Assert.Single(objects.Files); Assert.Equal(target.Key, missing.Asset.Key);
    }

    [Fact]
    public async Task InventoryFindsObjectsOutsideDbAndKeepsAmbiguousMatchesForContentReview()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var objects = new MemoryR2();
        var target = new MediaRecoveryPlanner.Metadata(115, 25, "Background", "scene.png", "drama-assets/25/background/" + new string('a', 32) + ".png", "image/png", 8);
        objects.Files.Add("pro/backgrounds/25/other-prefix.png", (new byte[8], "image/png"));
        objects.Files.Add("pro/backgrounds/22/another.png", (new byte[8], "image/png"));
        objects.Files.Add("pro/assets/wrong.png", (new byte[8], "image/gif"));
        var inventory = objects.Files.Select(p => new RemoteObjectEntry(p.Key, p.Value.Bytes.Length)).ToList();
        var result = await MediaRecoveryPlanner.RunCoreAsync([target], new(media.Environment, Options.Create(new StorageOptions())),
            objects, root, Path.Combine(root, "plans"), output, default, inventory);
        Assert.Equal(2, Assert.Single(result.Missing).Candidates.Length);
        Assert.All(result.Missing[0].Candidates, c => { Assert.Equal(0, c.SourceAssetId); Assert.Contains("SIZE_MIME_ONLY", c.Evidence); });
        Assert.Empty(result.ReadyPlanFiles); Assert.Equal(0, objects.Puts); Assert.Equal(0, objects.Signs); Assert.Equal(3, objects.Files.Count);
    }

    [Fact]
    public async Task DoesNotExportInvalidSignedKeyOrCreateApplyPlansForConflictingObjects()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var folder = Path.Combine(root, "plans");
        var objects = new MemoryR2(); var local = new LocalFileStorageService(media.Environment, Options.Create(new StorageOptions()));
        var bad = new MediaRecoveryPlanner.Metadata(115, 25, "Background", "scene.png", "https://example.invalid/file?X-Amz-Signature=secret", "image/png", 8);
        await Assert.ThrowsAsync<ArgumentException>(() => MediaRecoveryPlanner.RunCoreAsync([bad], local, objects, root, folder, output, default));
        Assert.False(Directory.Exists(folder));
        var valid = bad with { Key = "pro/assets/existing.png" };
        objects.Files.Add(valid.Key, (new byte[9], "image/png"));
        var result = await MediaRecoveryPlanner.RunCoreAsync([valid], local, objects, root, folder, output, default);
        Assert.Empty(result.ReadyPlanFiles); Assert.Empty(result.Missing);
        Assert.Contains("mismatches=1", output.ToString()); Assert.Equal(0, objects.Puts); Assert.Single(objects.Files);
    }
}
