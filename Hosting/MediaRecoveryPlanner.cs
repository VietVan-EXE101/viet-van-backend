using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

// Operator command: build a recovery inventory without PUT/DELETE or database writes.
public static class MediaRecoveryPlanner
{
    internal sealed record Metadata(int AssetId, int DramaId, string Kind, string OriginalFileName,
        string Key, string ContentType, long SizeBytes);
    internal sealed record Candidate(int SourceAssetId, string SourceKey, string? LocalFile, string Evidence);
    internal sealed record MissingAsset(Metadata Asset, string Status, Candidate[] Candidates);
    internal sealed record Report(int Matched, MissingAsset[] Missing, string[] ReadyPlanFiles);

    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment, TextWriter output)
    {
        try
        {
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var options = effective.GetSection("R2").Get<R2Options>() ?? new();
            _ = options.Endpoint();
            await using var db = new VietVanDbContext(new DbContextOptionsBuilder<VietVanDbContext>()
                .UseSqlServer(DeploymentConfiguration.SqlConnection(configuration, environment))
                .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance).EnableSensitiveDataLogging(false).Options);
            using var sdk = R2ObjectClient.Create(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await output.WriteLineAsync("PLAN ONLY: LIST/HEAD R2 + SELECT metadata + read local originals; write local report/plans only. Không PUT/DELETE/UPDATE/seed.");
            var objects = new R2ObjectClient(sdk, Options.Create(options));
            var inventory = await objects.ListAsync(timeout.Token);
            await output.WriteLineAsync($"BucketName={options.BucketName}; R2 inventory objects={inventory.Count}");
            var assets = await db.DramaAssets.AsNoTracking().OrderBy(a => a.Id).Select(a => new Metadata(
                a.Id, a.DramaProductionId, a.Kind, a.OriginalFileName, a.RelativePath, a.ContentType, a.SizeBytes)).ToListAsync(timeout.Token);
            var videos = await db.DramaProductions.AsNoTracking().OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.FreeVideoAssetId }).ToListAsync(timeout.Token);
            foreach (var video in videos.Where(d => d.FreeVideoAssetId.HasValue))
                await output.WriteLineAsync($"drama={video.Id}: configuredFreeVideoAssetId={video.FreeVideoAssetId}");
            var folder = Path.Combine(environment.ContentRootPath, "artifacts", "media-recovery",
                "plan-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            var local = new LocalFileStorageService(environment, Options.Create(
                effective.GetSection("Storage").Get<StorageOptions>() ?? new()));
            var result = await RunCoreAsync(assets, local, objects,
                environment.ContentRootPath, folder, output, timeout.Token, inventory);
            await output.WriteLineAsync($"Local report={Path.GetRelativePath(environment.ContentRootPath, Path.Combine(folder, "report.json"))}");
            await output.WriteLineAsync($"PLAN completed: matched={result.Matched}; missing={result.Missing.Length}; readyBatches={result.ReadyPlanFiles.Length}. Chưa phục hồi object nào. Candidates chỉ gợi ý đối chiếu, không tự thay file.");
            return 0;
        }
        catch (Exception error)
        {
            var code = error is MediaStorageUnavailableException storage ? storage.FailureCode :
                error is Microsoft.Data.SqlClient.SqlException sql ? "SQL_" + sql.Number : error.GetType().Name;
            await output.WriteLineAsync($"Planner stopped Code={code}. Không in secret/connection string/signed URL; không sửa database/R2.");
            return 1;
        }
    }

    internal static async Task<Report> RunCoreAsync(IReadOnlyList<Metadata> assets, LocalFileStorageService local,
        IR2ObjectClient objects, string root, string folder, TextWriter output, CancellationToken ct,
        IReadOnlyList<RemoteObjectEntry>? inventory = null)
    {
        // Validate all keys before serializing metadata; never export a persisted signed URL.
        foreach (var asset in assets)
            if (R2Options.Key(asset.Key) != asset.Key || asset.Key.Contains('?') || asset.Key.Contains('#'))
                throw new ArgumentException("Invalid stored key; inspect with audit first.");
        var heads = new Dictionary<string, RemoteObject?>(StringComparer.Ordinal);
        foreach (var asset in assets)
            if (!heads.ContainsKey(asset.Key)) heads[asset.Key] = await objects.HeadAsync(asset.Key, ct);
        // Also inspect objects absent from DB, including copies saved under another allowed prefix.
        foreach (var entry in inventory ?? [])
        {
            if (heads.ContainsKey(entry.Key) || !assets.Any(a => heads[a.Key] is null && a.SizeBytes == entry.Size &&
                Path.GetExtension(a.Key).Equals(Path.GetExtension(entry.Key), StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                if (R2Options.Key(entry.Key) != entry.Key || entry.Key.Contains('?') || entry.Key.Contains('#')) continue;
            }
            catch (ArgumentException) { continue; }
            heads[entry.Key] = await objects.HeadAsync(entry.Key, ct);
        }
        static bool SameMime(string left, string right) => string.Equals(left.Split(';')[0].Trim(), right, StringComparison.OrdinalIgnoreCase);
        static string Name(string value) => Path.GetFileName(value.Replace('\\', '/')).Normalize();
        bool Matches(Metadata asset) => heads[asset.Key] is { } remote && remote.Size == asset.SizeBytes && SameMime(remote.ContentType, asset.ContentType);
        string? Existing(Metadata asset) => local.ExistingPath(asset.Key) ?? local.ExistingPath(
            $"drama-assets/{asset.DramaId}/{asset.Kind.ToLowerInvariant()}/{Path.GetFileName(asset.Key)}");
        bool ValidLocal(Metadata asset, string path)
        {
            var ext = Path.GetExtension(asset.Key).ToLowerInvariant();
            return DramaService.MediaTypes.TryGetValue(ext, out var type) && SameMime(type.Mime, asset.ContentType) &&
                new FileInfo(path).Length == asset.SizeBytes && asset.SizeBytes > 0 &&
                asset.SizeBytes <= DramaService.MaxSizeFor(type.Kind) && DramaService.HasExpectedSignature(path, ext);
        }
        var ready = new List<(int DramaId, DramaMediaRecovery.Asset Asset)>();
        var missing = new List<MissingAsset>();
        foreach (var asset in assets.Where(a => heads[a.Key] is null))
        {
            var exact = local.ExistingPath(asset.Key);
            var candidates = new List<Candidate>();
            var status = "NEEDS_ORIGINAL_OR_CONFIRMED_COPY";
            if (exact is not null && ValidLocal(asset, exact))
            {
                await using var input = File.OpenRead(exact);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                var relative = Path.GetRelativePath(root, exact);
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    ready.Add((asset.DramaId, new(asset.AssetId, asset.Key, relative, asset.ContentType, asset.SizeBytes, hash)));
                    status = "READY_LOCAL_EXACT_KEY";
                }
            }
            if (status != "READY_LOCAL_EXACT_KEY" && !string.IsNullOrWhiteSpace(asset.OriginalFileName))
            {
                foreach (var other in assets.Where(a => a.AssetId != asset.AssetId && a.SizeBytes == asset.SizeBytes &&
                    SameMime(a.ContentType, asset.ContentType) && Name(a.OriginalFileName).Equals(Name(asset.OriginalFileName), StringComparison.OrdinalIgnoreCase)))
                {
                    var path = Existing(other);
                    var validPath = path is not null && ValidLocal(other, path) ? Path.GetRelativePath(root, path) : null;
                    if (validPath is not null || Matches(other))
                        candidates.Add(new(other.AssetId, other.Key, validPath, "SAME_ORIGINAL_NAME_SIZE_MIME; content confirmation required; not a verified target hash"));
                }
            }
            if (status != "READY_LOCAL_EXACT_KEY")
            {
                foreach (var pair in heads.Where(p => p.Value is { } remote && remote.Size == asset.SizeBytes &&
                    SameMime(remote.ContentType, asset.ContentType) && !candidates.Any(c => c.SourceKey == p.Key)))
                {
                    // Size/MIME alone cannot establish identity; never turn this into an apply plan.
                    var source = assets.FirstOrDefault(a => a.Key == pair.Key);
                    candidates.Add(new(source?.AssetId ?? 0, pair.Key, null,
                        "SIZE_MIME_ONLY; content confirmation required; not a verified target hash"));
                }
            }
            missing.Add(new(asset, status, candidates.ToArray()));
            await output.WriteLineAsync($"asset={asset.AssetId}; drama={asset.DramaId}: {status}; candidates={candidates.Count}; bytes={asset.SizeBytes}; MIME={asset.ContentType}");
        }
        var planFiles = new List<string>();
        Directory.CreateDirectory(folder);
        var json = new JsonSerializerOptions { WriteIndented = true };
        foreach (var group in ready.GroupBy(a => a.DramaId))
        {
            var batch = 0;
            foreach (var chunk in group.Chunk(20))
            {
                var path = Path.Combine(folder, $"drama-{group.Key}-batch-{++batch:D2}.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new DramaMediaRecovery.Plan(group.Key, chunk.Select(a => a.Asset).ToArray()), json), ct);
                planFiles.Add(Path.GetRelativePath(root, path));
            }
        }
        var mismatches = assets.Count(a => heads[a.Key] is not null && !Matches(a));
        var report = new Report(assets.Count(Matches), missing.ToArray(), planFiles.ToArray());
        await File.WriteAllTextAsync(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(report, json), ct);
        await output.WriteLineAsync($"Existing object metadata mismatches={mismatches}; left untouched. Original local files retained.");
        return report;
    }
}
