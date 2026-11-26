using System.Security.Cryptography;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class DramaMediaRecoveryTests
{
    private const string Key = "drama-assets/25/background/bd87acf36ae74a90af602899c42a162f.gif";

    private static async Task<DramaMediaRecovery.Asset> OriginalAsync(string root, int id = 114)
    {
        var filename = $"original-{id}.gif";
        await File.WriteAllBytesAsync(Path.Combine(root, filename), R2StorageTests.Gif);
        return new(id, Key, filename, "image/gif", R2StorageTests.Gif.Length,
            Convert.ToHexString(SHA256.HashData(R2StorageTests.Gif)));
    }

    [Fact]
    public async Task DryRunWritesNothingAndApplyRestoresExactAnimatedBytesAtSameKeyIdempotently()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath;
        var asset = await OriginalAsync(root); var objects = new MemoryR2();
        var plan = new DramaMediaRecovery.Plan(25, [asset]);
        Assert.Equal(0, await DramaMediaRecovery.RunCoreAsync(plan, root, objects, false, output, default));
        Assert.Empty(objects.Files); Assert.Equal(0, objects.Puts);
        Assert.Equal(0, await DramaMediaRecovery.RunCoreAsync(plan, root, objects, true, output, default));
        Assert.Equal(0, await DramaMediaRecovery.RunCoreAsync(plan, root, objects, true, output, default));
        Assert.Equal(1, objects.Puts); Assert.Equal(0, objects.Signs);
        Assert.Equal(R2StorageTests.Gif, objects.Files[Key].Bytes);
        Assert.Equal("image/gif", objects.Files[Key].Mime);
        Assert.Equal(R2StorageTests.Gif, await File.ReadAllBytesAsync(Path.Combine(root, asset.SourceFile)));
    }

    [Fact]
    public async Task ValidatesEverySourceBeforeFirstPut()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var first = await OriginalAsync(root);
        var second = await OriginalAsync(root, 112);
        second = second with { Key = "drama-assets/25/background/1ef9d16139b44cd8a1dfeef1442b138a.gif", Sha256 = new string('0', 64) };
        var objects = new MemoryR2();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DramaMediaRecovery.RunCoreAsync(
            new(25, [first, second]), root, objects, true, output, default));
        Assert.Equal(0, objects.Puts); Assert.Empty(objects.Files);
    }

    [Fact]
    public async Task ExistingDifferentBytesAreNeverOverwrittenEvenWithSameSizeAndMime()
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var asset = await OriginalAsync(root);
        var objects = new MemoryR2(); var conflict = new byte[R2StorageTests.Gif.Length];
        objects.Files.Add(Key, (conflict, "image/gif"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DramaMediaRecovery.RunCoreAsync(
            new(25, [asset]), root, objects, true, output, default));
        Assert.Equal(0, objects.Puts); Assert.Equal(conflict, objects.Files[Key].Bytes);
    }

    [Theory]
    [InlineData("other-drama")]
    [InlineData("path-traversal")]
    [InlineData("mime")]
    [InlineData("signature")]
    public async Task RejectsWrongDramaUnsafeSourceMimeAndSignatureWithoutCloudWrites(string invalid)
    {
        using var media = new TestMediaDirectory(); using var output = new StringWriter();
        var root = media.Environment.ContentRootPath; var asset = await OriginalAsync(root);
        asset = invalid switch
        {
            "other-drama" => asset with { Key = Key.Replace("/25/", "/22/") },
            "path-traversal" => asset with { SourceFile = "../original.gif" },
            "mime" => asset with { ContentType = "image/png" },
            _ => asset
        };
        if (invalid == "signature")
        {
            var bytes = new byte[R2StorageTests.Gif.Length];
            await File.WriteAllBytesAsync(Path.Combine(root, asset.SourceFile), bytes);
            asset = asset with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
        var objects = new MemoryR2();
        await Assert.ThrowsAnyAsync<Exception>(() => DramaMediaRecovery.RunCoreAsync(
            new(25, [asset]), root, objects, true, output, default));
        Assert.Equal(0, objects.Puts); Assert.Empty(objects.Files);
    }
}
