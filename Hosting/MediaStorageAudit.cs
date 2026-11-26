using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

// Explicit operator command. SELECT/HEAD only; never reconnects keys by guessing or writes on startup.
public static class MediaStorageAudit
{
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment, TextWriter output)
    {
        try
        {
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var storage = MediaStorageConfiguration.ResolveOptions(effective);
            var options = effective.GetSection("R2").Get<R2Options>() ?? new();
            _ = options.Endpoint();
            await using var db = new VietVanDbContext(new DbContextOptionsBuilder<VietVanDbContext>()
                .UseSqlServer(DeploymentConfiguration.SqlConnection(configuration, environment))
                .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance).EnableSensitiveDataLogging(false).Options);
            using var sdk = R2ObjectClient.Create(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await output.WriteLineAsync($"READ-ONLY Provider(this process)={storage.Provider}; BucketName={options.BucketName}; Database={db.Database.GetDbConnection().Database}");
            return await RunCoreAsync(db, new R2ObjectClient(sdk, Options.Create(options)), output, timeout.Token);
        }
        catch (Exception error)
        {
            var code = error is MediaStorageUnavailableException storage ? storage.FailureCode :
                error is Microsoft.Data.SqlClient.SqlException sql ? "SQL_" + sql.Number : error.GetType().Name;
            await output.WriteLineAsync($"Audit stopped Code={code}. Không in provider message/connection string/secret/signed URL; không sửa dữ liệu.");
            return 1;
        }
    }

    internal static async Task<int> RunCoreAsync(VietVanDbContext db, IR2ObjectClient objects, TextWriter output, CancellationToken ct)
    {
        var assets = await db.DramaAssets.AsNoTracking().OrderBy(a => a.Id).Select(a => new
        { a.Id, a.DramaProductionId, a.Kind, a.Title, a.RelativePath, a.ContentType, a.SizeBytes }).ToListAsync(ct);
        var productions = await db.DramaProductions.AsNoTracking().OrderBy(d => d.Id)
            .Select(d => new { d.Id, d.FreeVideoAssetId, d.IsPublished, d.Status }).ToListAsync(ct);
        var works = await db.LiteraryWorks.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Id, w.CoverImageUrl }).ToListAsync(ct);
        var manifests = await db.DramaManifests.AsNoTracking().OrderBy(m => m.DramaProductionId)
            .Select(m => new { m.DramaProductionId, m.Revision, m.DraftJson, m.PublishedJson, m.PublishedLegacyJson }).ToListAsync(ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { assets, productions, works, manifests })))).ToLowerInvariant();
        var connection = db.Database.GetDbConnection();
        var target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection.DataSource + "|" + connection.Database)))[..16];
        await output.WriteLineAsync($"DatabaseTargetRef={target}; MetadataFingerprint={fingerprint}");
        await output.WriteLineAsync("Fingerprint gồm liên kết media, revision/JSON bản nháp/xuất bản; so sánh trước/sau redeploy, không phải checksum toàn database.");
        var checkedKeys = new Dictionary<string, RemoteObject?>(StringComparer.Ordinal);
        async Task<RemoteObject?> Head(string key)
        {
            if (!checkedKeys.TryGetValue(key, out var found)) checkedKeys[key] = found = await objects.HeadAsync(key, ct);
            return found;
        }
        var matched = 0; var missing = 0; var mismatched = 0; var invalid = 0; var candidates = 0;
        foreach (var asset in assets)
        {
            string key;
            try
            {
                key = R2Options.Key(asset.RelativePath);
                if (key.Contains('?') || key.Contains('#')) throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                invalid++; await output.WriteLineAsync($"asset={asset.Id}; drama={asset.DramaProductionId}: INVALID_OR_URL_KEY (value hidden)"); continue;
            }
            var remote = await Head(key);
            var matches = remote is not null && remote.Size == asset.SizeBytes &&
                string.Equals(remote.ContentType.Split(';')[0].Trim(), asset.ContentType, StringComparison.OrdinalIgnoreCase);
            if (remote is null) missing++; else if (matches) matched++; else mismatched++;
            await output.WriteLineAsync($"asset={asset.Id}; drama={asset.DramaProductionId}: {(remote is null ? "MISSING" : matches ? "HEAD_MATCH" : "SIZE_MIME_MISMATCH")}; key={key}");
            if (remote is null && key.StartsWith("drama-assets/", StringComparison.Ordinal))
            {
                var freeVideo = productions.Any(d => d.Id == asset.DramaProductionId && d.FreeVideoAssetId == asset.Id);
                var prefix = R2FileStorageService.Prefix(asset.Kind, asset.Title == DramaService.CoverAssetTitle, freeVideo);
                var candidate = $"{prefix}/{asset.DramaProductionId}/{asset.Id}/{Path.GetFileName(key)}";
                if (await Head(candidate) is not null)
                {
                    candidates++;
                    await output.WriteLineAsync($"asset={asset.Id}: MIGRATION_CANDIDATE={candidate}; verify original hash before any metadata repair (not applied).");
                }
            }
        }
        var workFailures = 0;
        foreach (var work in works.Where(w => !string.IsNullOrEmpty(w.CoverImageUrl)))
        {
            var value = work.CoverImageUrl!;
            var api = value.StartsWith("/api/media/work-covers/", StringComparison.Ordinal);
            var legacy = value.StartsWith("/uploads/work-covers/", StringComparison.Ordinal);
            if (!(api || legacy))
            {
                await output.WriteLineAsync($"work={work.Id}: OTHER_COVER_URL; review separately (value hidden)"); continue;
            }
            var name = value[(api ? "/api/media/work-covers/".Length : "/uploads/work-covers/".Length)..];
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$"))
            { workFailures++; await output.WriteLineAsync($"work={work.Id}: INVALID_COVER_PATH"); continue; }
            var found = await Head("free/thumbnails/works/0/" + name) is not null;
            if (!api || !found) workFailures++;
            await output.WriteLineAsync($"work={work.Id}: {(legacy ? "LEGACY_LOCAL_COVER_URL" : "API_COVER_URL")}; R2={(found ? "EXISTS" : "MISSING")}; no metadata changes");
        }
        var suspiciousSnapshots = manifests.Count(m => Suspicious(m.DraftJson) || Suspicious(m.PublishedJson) || Suspicious(m.PublishedLegacyJson));
        await output.WriteLineAsync($"Audit completed: assets={assets.Count}; matched={matched}; missing={missing}; mismatched={mismatched}; invalidOrUrlKey={invalid}; migrationCandidates={candidates}; workCoverIssues={workFailures}; snapshotsWithSignedOrLocalhostUrl={suspiciousSnapshots}; uniqueKeysChecked={checkedKeys.Count}.");
        await output.WriteLineAsync("HEAD_MATCH chỉ kiểm tra size/MIME, không hash byte. Không LIST toàn bucket, không PUT/DELETE/UPDATE/seed/migration; không sửa liên kết theo phỏng đoán.");
        return missing + mismatched + invalid + workFailures + suspiciousSnapshots == 0 ? 0 : 1;
    }

    private static bool Suspicious(string? value) => value is not null &&
        (value.Contains("X-Amz-", StringComparison.OrdinalIgnoreCase) || value.Contains("localhost:", StringComparison.OrdinalIgnoreCase));
}
