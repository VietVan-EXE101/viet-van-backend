using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

public static class MediaMigration
{
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment, bool apply, TextWriter output)
    {
        try
        {
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var storage = effective.GetSection("Storage").Get<StorageOptions>() ?? new();
            var source = new LocalFileStorageService(environment, Options.Create(storage));
            var dbOptions = new DbContextOptionsBuilder<VietVanDbContext>()
                .UseSqlServer(DeploymentConfiguration.SqlConnection(configuration, environment))
                .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance).EnableSensitiveDataLogging(false).Options;
            await using var db = new VietVanDbContext(dbOptions);
            var r2 = effective.GetSection("R2").Get<R2Options>() ?? new();
            using var sdk = apply ? R2ObjectClient.Create(r2) : null;
            IR2ObjectClient? objects = sdk is null ? null : new R2ObjectClient(sdk, Options.Create(r2));
            return await RunCoreAsync(db, source, objects, environment, apply, output, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Migration stopped. ErrorType={ex.GetType().Name}. Không in connection string, secret hoặc signed URL.");
            return 1;
        }
    }

    internal static async Task<int> RunCoreAsync(VietVanDbContext db, LocalFileStorageService source, IR2ObjectClient? objects,
        IWebHostEnvironment environment, bool apply, TextWriter output, CancellationToken ct)
    {
        if (apply && objects is null) throw new InvalidOperationException("Apply cần R2 client.");
        var moved = 0; var skipped = 0; var failed = 0;
        await output.WriteLineAsync(apply ? "APPLY: PUT có điều kiện + xác minh SHA256 + cập nhật khóa DB. Không xóa local." :
            "DRY-RUN: chỉ SELECT và đọc file local; không gọi R2, không sửa DB hoặc file.");
        var assets = await db.DramaAssets.AsNoTracking().Select(a => new
        { a.Id, a.DramaProductionId, a.Kind, a.Title, a.RelativePath, a.ContentType, a.SizeBytes }).ToListAsync(ct);
        var freeVideos = (await db.DramaProductions.AsNoTracking().Where(d => d.FreeVideoAssetId != null)
            .Select(d => d.FreeVideoAssetId!.Value).ToListAsync(ct)).ToHashSet();
        foreach (var asset in assets)
        {
            try
            {
                if (asset.RelativePath.StartsWith("free/", StringComparison.Ordinal) || asset.RelativePath.StartsWith("pro/", StringComparison.Ordinal))
                {
                    if (apply)
                    {
                        var remote = await objects!.HeadAsync(asset.RelativePath, ct);
                        if (remote is null || remote.Size != asset.SizeBytes || !SameMime(remote.ContentType, asset.ContentType))
                            throw new InvalidOperationException("Object R2 không khớp metadata hiện có.");
                    }
                    skipped++; await output.WriteLineAsync($"SKIP asset={asset.Id}: đã có R2 key{(apply ? " (HEAD verified)" : " (chưa xác minh remote)")}.");
                    continue;
                }
                var path = source.ExistingPath(asset.RelativePath) ?? throw new FileNotFoundException();
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!DramaService.MediaTypes.TryGetValue(ext, out var type) || !SameMime(type.Mime, asset.ContentType) ||
                    new FileInfo(path).Length != asset.SizeBytes || !DramaService.HasExpectedSignature(path, ext))
                    throw new InvalidOperationException("File local không khớp metadata/signature.");
                var prefix = R2FileStorageService.Prefix(asset.Kind, asset.Title == DramaService.CoverAssetTitle, freeVideos.Contains(asset.Id));
                var target = R2Options.Key($"{prefix}/{asset.DramaProductionId}/{asset.Id}/{Path.GetFileName(path)}");
                await output.WriteLineAsync($"PLAN asset={asset.Id}: {asset.RelativePath} -> {target}");
                if (!apply) continue;
                await CopyVerifiedAsync(objects!, target, path, asset.ContentType, ct);
                // CAS: không đè sửa đổi của Admin đang diễn ra; chỉ cập nhật khóa, không đổi ID/manifest.
                var changed = await db.DramaAssets.Where(a => a.Id == asset.Id && a.RelativePath == asset.RelativePath &&
                    a.SizeBytes == asset.SizeBytes && a.ContentType == asset.ContentType)
                    .ExecuteUpdateAsync(set => set.SetProperty(a => a.RelativePath, target), ct);
                if (changed != 1 && !await db.DramaAssets.AsNoTracking().AnyAsync(a => a.Id == asset.Id && a.RelativePath == target, ct))
                    throw new InvalidOperationException("Metadata bị thay đổi đồng thời; object được giữ để đối chiếu.");
                moved++; await output.WriteLineAsync($"MOVED asset={asset.Id}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { failed++; await output.WriteLineAsync($"FAIL asset={asset.Id}: {ex.GetType().Name}. Không sửa bản ghi này nếu chưa xác minh copy."); }
        }

        var works = await db.LiteraryWorks.AsNoTracking().Where(w => w.CoverImageUrl != null)
            .Select(w => new { w.Id, w.CoverImageUrl }).ToListAsync(ct);
        foreach (var work in works)
        {
            if (!work.CoverImageUrl!.StartsWith("/uploads/work-covers/", StringComparison.Ordinal)) continue;
            try
            {
                var name = work.CoverImageUrl["/uploads/work-covers/".Length..];
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$"))
                    throw new ArgumentException("Tên bìa không hợp lệ.");
                var path = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "work-covers", name);
                var ext = Path.GetExtension(name);
                if (!File.Exists(path) || !DramaService.HasExpectedSignature(path, ext)) throw new FileNotFoundException();
                var key = "free/thumbnails/works/0/" + name;
                await output.WriteLineAsync($"PLAN work={work.Id}: {work.CoverImageUrl} -> {key}");
                if (!apply) continue;
                await CopyVerifiedAsync(objects!, key, path, DramaService.MediaTypes[ext].Mime, ct);
                var apiUrl = "/api/media/work-covers/" + name;
                var changed = await db.LiteraryWorks.Where(w => w.Id == work.Id && w.CoverImageUrl == work.CoverImageUrl)
                    .ExecuteUpdateAsync(set => set.SetProperty(w => w.CoverImageUrl, apiUrl), ct);
                if (changed != 1 && !await db.LiteraryWorks.AsNoTracking().AnyAsync(w => w.Id == work.Id && w.CoverImageUrl == apiUrl, ct))
                    throw new InvalidOperationException("Bìa đã đổi đồng thời.");
                moved++; await output.WriteLineAsync($"MOVED work={work.Id}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { failed++; await output.WriteLineAsync($"FAIL work={work.Id}: {ex.GetType().Name}."); }
        }
        await output.WriteLineAsync($"Completed: moved={moved}; skipped={skipped}; failed={failed}. Local files untouched; không sửa scene/hotspot/manifest.");
        return failed == 0 ? 0 : 1;
    }

    private static bool SameMime(string left, string right) =>
        string.Equals(left.Split(';')[0].Trim(), right.Split(';')[0].Trim(), StringComparison.OrdinalIgnoreCase);

    internal static async Task CopyVerifiedAsync(IR2ObjectClient objects, string target, string path, string contentType, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
        var remote = await objects.HeadAsync(target, ct);
        if (remote is null)
        {
            // If-None-Match:* bảo vệ trường hợp object được tạo giữa HEAD và PUT.
            await objects.PutFileAsync(target, path, contentType, hash, ct);
            remote = await objects.HeadAsync(target, ct);
        }
        if (remote is null || remote.Size != input.Length || !SameMime(remote.ContentType, contentType) ||
            !string.Equals(await objects.ReadHashAsync(target, ct), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object khác byte/size/MIME; không ghi đè, không cập nhật DB.");
    }
}
