using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

// Operator-only recovery of confirmed originals at existing legacy keys. No database dependency.
public static class DramaMediaRecovery
{
    internal sealed record Asset(int AssetId, string Key, string SourceFile, string ContentType, long SizeBytes, string Sha256);
    internal sealed record Plan(int DramaId, Asset[] Assets);

    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment,
        string planFile, bool apply, TextWriter output)
    {
        try
        {
            var path = InsideRoot(environment.ContentRootPath, planFile);
            if (new FileInfo(path).Length > 65536) throw new ArgumentException();
            var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path),
                new JsonSerializerOptions { MaxDepth = 8 }) ?? throw new ArgumentException();
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var options = effective.GetSection("R2").Get<R2Options>() ?? new();
            var invalid = options.InvalidKeys();
            if (invalid.Count > 0)
            {
                await output.WriteLineAsync("Thiếu/sai cấu hình: " + string.Join(", ", invalid));
                return 1;
            }
            using var sdk = R2ObjectClient.Create(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            return await RunCoreAsync(plan, environment.ContentRootPath,
                new R2ObjectClient(sdk, Options.Create(options)), apply, output, timeout.Token);
        }
        catch (Exception error)
        {
            var storage = error as MediaStorageUnavailableException;
            await output.WriteLineAsync($"Recovery stopped: {error.GetType().Name}" +
                (storage is null ? "." : $"; code={storage.FailureCode}; HTTP={storage.HttpStatus}.") +
                " Không in secret/signed URL. Không sửa database; file đã phục hồi được giữ lại để chạy tiếp an toàn.");
            return 1;
        }
    }

    internal static async Task<int> RunCoreAsync(Plan plan, string root, IR2ObjectClient objects,
        bool apply, TextWriter output, CancellationToken ct)
    {
        await output.WriteLineAsync(apply
            ? "APPLY: chỉ tạo object thiếu tại key đã xác nhận; không ghi đè hoặc xóa."
            : "DRY-RUN: kiểm tra file gốc và HEAD/hash object có sẵn; không PUT/DELETE.");
        await output.WriteLineAsync("Không truy cập database, không sửa ID, key metadata, scene, manifest hoặc revision.");
        if (plan.DramaId <= 0 || plan.Assets is null || plan.Assets.Length is < 1 or > 20 ||
            plan.Assets.Any(a => a is null) ||
            plan.Assets.Select(a => a.AssetId).Distinct().Count() != plan.Assets.Length ||
            plan.Assets.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count() != plan.Assets.Length)
            throw new ArgumentException("Invalid recovery plan.");

        var locked = new List<(Asset Asset, string Path, FileStream Stream)>();
        try
        {
            // Validate and lock every source before any remote write. Keep originals byte-for-byte.
            foreach (var asset in plan.Assets)
            {
                if (asset.AssetId <= 0 || string.IsNullOrEmpty(asset.Key) ||
                    !Regex.IsMatch(asset.Key, $@"^drama-assets/{plan.DramaId}/[a-z]+/[a-f0-9]{{32}}\.(png|gif|jpg|jpeg|webp)$") ||
                    R2Options.Key(asset.Key) != asset.Key || asset.SizeBytes is <= 0 or > 10 * 1024 * 1024 ||
                    asset.Sha256 is null || !Regex.IsMatch(asset.Sha256, "^[a-fA-F0-9]{64}$"))
                    throw new ArgumentException("Invalid asset identity/key/size/hash.");
                var path = InsideRoot(root, asset.SourceFile);
                var ext = Path.GetExtension(asset.Key);
                if (!Path.GetExtension(path).Equals(ext, StringComparison.OrdinalIgnoreCase) ||
                    !DramaService.MediaTypes.TryGetValue(ext, out var type) || type.Mime != asset.ContentType)
                    throw new ArgumentException("MIME/extension mismatch.");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                locked.Add((asset, path, stream));
                if (stream.Length != asset.SizeBytes || !DramaService.HasExpectedSignature(path, ext) ||
                    !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), asset.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Original file does not match confirmed bytes.");
            }

            // Check all existing objects before creating any. A concurrent create is protected by If-None-Match:*.
            foreach (var (asset, _, _) in locked)
            {
                var remote = await objects.HeadAsync(asset.Key, ct);
                if (remote is not null && (remote.Size != asset.SizeBytes ||
                    !remote.ContentType.Split(';')[0].Trim().Equals(asset.ContentType, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(await objects.ReadHashAsync(asset.Key, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Existing object conflicts; no overwrite allowed.");
                await output.WriteLineAsync($"asset={asset.AssetId}: SOURCE_SHA256=MATCH; R2={(remote is null ? "MISSING" : "SHA256_MATCH")}; key={asset.Key}");
            }
            if (!apply) return 0;
            foreach (var (asset, path, _) in locked)
            {
                await MediaMigration.CopyVerifiedAsync(objects, asset.Key, path, asset.ContentType, ct);
                await output.WriteLineAsync($"PASS asset={asset.AssetId}: R2 size/MIME/SHA256 verified; ID/key unchanged.");
            }
            await output.WriteLineAsync($"Recovery completed: verified={locked.Count}. Local originals retained; no database changes.");
            return 0;
        }
        finally
        {
            foreach (var (_, _, stream) in locked) await stream.DisposeAsync();
        }
    }

    private static string InsideRoot(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new ArgumentException();
        var boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(boundary, relative));
        if (!path.StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException();
        // Do not follow junctions/symlinks outside the checked workspace.
        for (var current = path; current.Length >= boundary.Length; current = Path.GetDirectoryName(current)!)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException();
        return path;
    }
}
