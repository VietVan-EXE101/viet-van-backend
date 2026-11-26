using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

public static class DramaMediaInspection
{
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment,
        string selection, TextWriter output)
    {
        if (!TrySelection(selection, out var dramaId, out var assetIds))
        {
            await output.WriteLineAsync("Dùng --inspect-drama-media=25:112,114 (ID dương, tối đa 20 asset).");
            return 1;
        }
        var stage = "configuration";
        try
        {
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var storage = MediaStorageConfiguration.ResolveOptions(effective);
            var local = new LocalFileStorageService(environment, Options.Create(storage));
            await using var db = new VietVanDbContext(new DbContextOptionsBuilder<VietVanDbContext>()
                .UseSqlServer(DeploymentConfiguration.SqlConnection(configuration, environment))
                .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance).EnableSensitiveDataLogging(false).Options);
            var options = effective.GetSection("R2").Get<R2Options>() ?? new();
            // Có thể kiểm tra R2 ngay cả khi runtime máy này đang chọn Local, để đối chiếu provider.
            using var sdk = options.InvalidKeys().Count == 0 ? R2ObjectClient.Create(options) : null;
            IR2ObjectClient? objects = sdk is null ? null : new R2ObjectClient(sdk, Options.Create(options));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            stage = "SELECT metadata / local files / R2 HEAD";
            return await RunCoreAsync(db, local, objects, storage.Provider, dramaId, assetIds, output, timeout.Token);
        }
        catch (Exception ex)
        {
            var detail = ex is Microsoft.Data.SqlClient.SqlException sql ? $" SqlNumber={sql.Number}." : "";
            await output.WriteLineAsync($"Inspection failed at {stage}. ErrorType={ex.GetType().Name}.{detail} Không in connection string/secret/signed URL; không sửa dữ liệu.");
            return 1;
        }
    }

    internal static bool TrySelection(string selection, out int dramaId, out int[] assetIds)
    {
        dramaId = 0; assetIds = [];
        var pieces = selection.Split(':');
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out dramaId) || dramaId <= 0) return false;
        var ids = pieces[1].Split(',');
        if (ids.Length is < 1 or > 20) return false;
        var parsed = new List<int>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, out var value) || value <= 0) return false;
            parsed.Add(value);
        }
        assetIds = parsed.Distinct().ToArray(); return true;
    }

    internal static async Task<int> RunCoreAsync(VietVanDbContext db, LocalFileStorageService local, IR2ObjectClient? objects,
        string provider, int dramaId, int[] assetIds, TextWriter output, CancellationToken ct)
    {
        await output.WriteLineAsync($"READ-ONLY inspection: drama={dramaId}; RuntimeProvider(this process)={provider}. Chỉ SELECT, kiểm tra local và HEAD; không PUT/DELETE/UPDATE.");
        await output.WriteLineAsync($"Database={db.Database.GetDbConnection().Database}");
        var manifest = await db.DramaManifests.AsNoTracking().Where(m => m.DramaProductionId == dramaId)
            .Select(m => new { m.Revision }).SingleOrDefaultAsync(ct);
        await output.WriteLineAsync($"DraftRevision={manifest?.Revision.ToString() ?? "no manifest row"}. Không sửa manifest hoặc scene.");
        var freeVideo = await db.DramaProductions.AsNoTracking().Where(d => d.Id == dramaId).Select(d => d.FreeVideoAssetId).SingleOrDefaultAsync(ct);
        var assets = await db.DramaAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.DramaProductionId, a.Kind, a.Title, a.RelativePath, a.ContentType, a.SizeBytes }).ToListAsync(ct);
        var failures = 0;
        foreach (var id in assetIds)
        {
            var asset = assets.SingleOrDefault(a => a.Id == id);
            if (asset is null || asset.DramaProductionId != dramaId)
            {
                failures++; await output.WriteLineAsync($"asset={id}: METADATA_NOT_FOUND_FOR_DRAMA"); continue;
            }
            string key;
            try
            {
                key = R2Options.Key(asset.RelativePath);
                if (key.Contains('?') || key.Contains('#')) throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                failures++; await output.WriteLineAsync($"asset={id}: INVALID_STORAGE_KEY (giá trị bị ẩn)"); continue;
            }
            var extension = Path.GetExtension(key).ToLowerInvariant();
            var mime = DramaService.MediaTypes.TryGetValue(extension, out var mediaType) ? mediaType.Mime : null;
            await output.WriteLineAsync($"asset={id}: key={key}; expectedMime={mime ?? "unsupported"}; bytes={asset.SizeBytes}");
            // Chỉ tìm đúng key hoặc basename gốc trong thư mục của chính kịch/kind này.
            var localPath = local.ExistingPath(key) ?? local.ExistingPath($"drama-assets/{dramaId}/{asset.Kind.ToLowerInvariant()}/{Path.GetFileName(key)}");
            var localMatches = localPath is not null && new FileInfo(localPath).Length == asset.SizeBytes &&
                mime is not null && string.Equals(mime, asset.ContentType, StringComparison.OrdinalIgnoreCase) &&
                DramaService.HasExpectedSignature(localPath, extension);
            await output.WriteLineAsync($"asset={id}: LOCAL={(localPath is null ? "MISSING" : localMatches ? "MATCH" : "MISMATCH")}");
            RemoteObject? remote = null; var remoteChecked = false;
            if (objects is not null)
            {
                try { remote = await objects.HeadAsync(key, ct); remoteChecked = true; }
                catch (MediaStorageUnavailableException ex)
                { await output.WriteLineAsync($"asset={id}: R2_CHECK_FAILED Code={ex.FailureCode}; HTTP={ex.HttpStatus?.ToString() ?? "none"}"); }
            }
            var remoteMatches = remote is not null && remote.Size == asset.SizeBytes &&
                string.Equals(remote.ContentType.Split(';')[0].Trim(), asset.ContentType, StringComparison.OrdinalIgnoreCase);
            await output.WriteLineAsync($"asset={id}: R2={(remote is not null ? remoteMatches ? "MATCH" : "MISMATCH" : remoteChecked ? "MISSING" : "NOT_CHECKED")}");
            if (remoteChecked && remote is null && key.StartsWith("drama-assets/", StringComparison.Ordinal))
            {
                var prefix = R2FileStorageService.Prefix(asset.Kind, asset.Title == DramaService.CoverAssetTitle, freeVideo == id);
                var candidate = $"{prefix}/{dramaId}/{id}/{Path.GetFileName(key)}";
                try
                {
                    var migrated = await objects!.HeadAsync(candidate, ct);
                    await output.WriteLineAsync($"asset={id}: MIGRATION_CANDIDATE={candidate}; R2={(migrated is null ? "MISSING" : "EXISTS_REQUIRES_VERIFICATION")}");
                }
                catch (MediaStorageUnavailableException ex)
                { await output.WriteLineAsync($"asset={id}: CANDIDATE_CHECK_FAILED Code={ex.FailureCode}; HTTP={ex.HttpStatus?.ToString() ?? "none"}"); }
            }
            var activeMatches = provider.Equals("Local", StringComparison.OrdinalIgnoreCase) ? localMatches :
                provider.Equals("R2", StringComparison.OrdinalIgnoreCase) && remoteMatches;
            if (!activeMatches) failures++;
            if (remoteMatches && !localMatches) await output.WriteLineAsync($"asset={id}: FILE_EXISTS_ON_R2; nếu Render dùng Local thì kiểm tra Storage__Provider trước khi khôi phục.");
            if (!remoteMatches && localMatches) await output.WriteLineAsync($"asset={id}: LOCAL_COPY_AVAILABLE; có thể khôi phục vào đúng key sau khi xác minh, giữ nguyên asset ID.");
        }
        await output.WriteLineAsync($"Inspection completed: requested={assetIds.Length}; unavailableOrMismatched={failures}. Không thay ID, key, revision, manifest hoặc file.");
        return failures == 0 ? 0 : 1;
    }
}
