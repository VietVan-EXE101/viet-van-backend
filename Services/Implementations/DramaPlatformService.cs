using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;

namespace VietVan.API.Services.Implementations;

public sealed class DramaPlatformService(VietVanDbContext db, DramaService legacy)
{
    private static readonly JsonSerializerOptions Json = ManifestValidator.JsonOptions;

    public async Task<DramaDto> LegacySnapshotAsync(int id, bool admin, CancellationToken ct)
    {
        await FindAsync(id, admin, ct);
        if (admin) return await legacy.DetailAsync(id, true);
        var row = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if (row?.PublishedJson != null) throw new KeyNotFoundException("Vở kịch dùng manifest schemaVersion=1.");
        return row?.PublishedLegacyJson is string json ? JsonSerializer.Deserialize<DramaDto>(json, Json)! : await legacy.DetailAsync(id, true);
    }

    public async Task<bool> IsPublishedAssetAsync(int id, int assetId, CancellationToken ct)
    {
        var row = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if (row?.PublishedJson is string json) return ManifestValidator.AssetIds(json).Contains(assetId);
        var snapshot = row?.PublishedLegacyJson is string old ? JsonSerializer.Deserialize<DramaDto>(old, Json)! : await legacy.DetailAsync(id, true);
        return snapshot.Assets.Any(a => a.Id == assetId);
    }

    public async Task<DramaProduction> FindAsync(int id, bool admin, CancellationToken ct) =>
        await db.DramaProductions.FirstOrDefaultAsync(d => d.Id == id && (admin || (d.IsPublished && d.Status == "PUBLISHED")), ct)
        ?? throw new KeyNotFoundException("Không tìm thấy vở kịch.");

    public async Task<object> DetailAsync(int id, int? userId, bool pro, bool admin, CancellationToken ct)
    {
        var drama = await FindAsync(id, admin, ct);
        var reviews = db.Set<Review>().AsNoTracking().Where(r => r.DramaProductionId == id && r.Status == "Published");
        var rating = await reviews.GroupBy(_ => 1).Select(g => new { Average = g.Average(r => (double)r.Rating), Total = g.Count() }).FirstOrDefaultAsync(ct);
        var mine = userId.HasValue ? await db.Set<Review>().AsNoTracking().Where(r => r.DramaProductionId == id && r.UserId == userId).Select(r => new { r.Id, r.Rating, r.Comment, r.Status }).SingleOrDefaultAsync(ct) : null;
        var manifest = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        var cover = await db.DramaAssets.AsNoTracking().Where(x => x.DramaProductionId == id && x.Title == DramaService.CoverAssetTitle).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var publishedLegacy = manifest?.PublishedLegacyJson is string published ? JsonSerializer.Deserialize<DramaDto>(published, Json) : null;
        var hasInteractive = manifest?.PublishedJson != null || (publishedLegacy is not null ? publishedLegacy.Scenes.Count > 0 :
            await db.DramaScenes.AnyAsync(s => s.DramaProductionId == id, ct));
        // Adapter chỉ trả snapshot cũ cho PRO. FREE không nhận scene/hotspot hay URL asset PRO.
        DramaDto? snapshot = null;
        if (pro || admin)
        {
            if (manifest?.PublishedLegacyJson is string saved && manifest.PublishedJson is null)
                snapshot = JsonSerializer.Deserialize<DramaDto>(saved, Json);
            else if (manifest?.PublishedJson is null && drama.IsPublished)
                snapshot = await legacy.DetailAsync(id, true);
        }
        if (snapshot?.Scenes.Count > 0) await RecordViewAsync(id, userId, "PRO_INTERACTIVE", ct);
        return new {
            drama.Id, drama.Title, drama.Description, drama.LiteraryWorkId, drama.IsPublished, drama.Status,
            drama.CreatedAt, drama.UpdatedAt, drama.PublishedAt, accessLevel = "Public",
            hasFreeVideo = drama.FreeVideoAssetId.HasValue,
            hasInteractive,
            coverImage = cover is null ? null : AssetDto(cover),
            assets = snapshot?.Assets ?? [], scenes = snapshot?.Scenes ?? [],
            averageRating = rating?.Average ?? 0, totalRatings = rating?.Total ?? 0, myRating = mine
        };
    }

    public async Task<object> InteractiveAsync(int id, int? userId, bool admin, CancellationToken ct, bool preview = false)
    {
        if (preview && !admin) throw new UnauthorizedAccessException();
        await FindAsync(id, admin, ct);
        var row = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if ((preview ? row?.DraftJson : row?.PublishedJson) is string json)
        {
            var ids = ManifestValidator.AssetIds(json);
            var assets = await db.DramaAssets.AsNoTracking().Where(a => a.DramaProductionId == id && ids.Contains(a.Id)).ToListAsync(ct);
            // Metadata snapshot được lưu cùng JSON; chỉnh sửa bản nháp không làm đổi giải nghĩa đã xuất bản.
            using var doc = JsonDocument.Parse(json);
            var result = new { manifest = doc.RootElement.Clone(), assets = assets.Select(AssetDto).ToList(), publishedAt = preview ? null : row!.PublishedAt, row!.Revision, isPreview = preview };
            if (!preview) await RecordViewAsync(id, userId, "PRO_INTERACTIVE", ct);
            return result;
        }
        var legacyDrama = !preview && row?.PublishedLegacyJson is string old ? JsonSerializer.Deserialize<DramaDto>(old, Json)! : await legacy.DetailAsync(id, true);
        if (legacyDrama.Scenes.Count == 0) throw new KeyNotFoundException("Chưa có phiên bản tương tác đã xuất bản.");
        if (!preview) await RecordViewAsync(id, userId, "PRO_INTERACTIVE", ct);
        return new { manifest = new { schemaVersion = 0, legacy = legacyDrama }, assets = legacyDrama.Assets, row?.PublishedAt };
    }

    public async Task<object> DraftAsync(int id, CancellationToken ct)
    {
        await FindAsync(id, true, ct);
        var row = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        return new { manifest = row?.DraftJson is string json ? JsonSerializer.Deserialize<JsonElement>(json) : (JsonElement?)null,
            revision = row?.Revision ?? 0, publishedAt = row?.PublishedAt, legacy = row?.DraftJson is null ? await legacy.DetailAsync(id, true) : null };
    }

    public async Task<object> SaveDraftAsync(int id, SaveManifestRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct);
        await FindAsync(id, true, ct);
        var assets = await db.DramaAssets.AsNoTracking().Where(a => a.DramaProductionId == id).ToListAsync(ct);
        ManifestValidator.Validate(request.Manifest, id, assets);
        var row = await db.DramaManifests.SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if ((row?.Revision ?? 0) != request.Revision) throw new InvalidOperationException("Bản nháp đã thay đổi. Tải lại revision trước khi lưu.");
        if (row is null) { row = new DramaManifest { DramaProductionId = id }; db.Add(row); }
        await PreserveLegacyAsync(id, row, ct);
        row.DraftJson = JsonSerializer.Serialize(request.Manifest, Json); row.Revision++; row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { revision = row.Revision, row.UpdatedAt };
    }

    // Phải gọi trong transaction đang giữ khóa drama trước mọi chỉnh sửa scene/hotspot cũ.
    public async Task PreserveLegacyAsync(int id, DramaManifest? row, CancellationToken ct)
    {
        var drama = await db.DramaProductions.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (!drama.IsPublished) return;
        row ??= await db.DramaManifests.SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if (row?.PublishedJson != null || row?.PublishedLegacyJson != null) return;
        if (row is null) { row = new DramaManifest { DramaProductionId = id }; db.Add(row); }
        row.PublishedLegacyJson = JsonSerializer.Serialize(await legacy.DetailAsync(id, true), Json);
        row.PublishedAt = drama.PublishedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> StatusAsync(int id, string status, CancellationToken ct)
    {
        if (status is not ("DRAFT" or "PUBLISHED" or "DISABLED")) throw new ArgumentException("Trạng thái không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct);
        var drama = await FindAsync(id, true, ct);
        var row = await db.DramaManifests.SingleOrDefaultAsync(x => x.DramaProductionId == id, ct);
        if (status == "PUBLISHED")
        {
            var scenes = await db.DramaScenes.AnyAsync(x => x.DramaProductionId == id, ct);
            if (drama.FreeVideoAssetId is null && row?.DraftJson is null && !scenes)
                throw new InvalidOperationException("Cần video FREE hoặc cấu hình tương tác trước khi xuất bản.");
            if (row is null) { row = new DramaManifest { DramaProductionId = id }; db.Add(row); }
            if (row.DraftJson is string draft)
            {
                ManifestValidator.Validate(JsonSerializer.Deserialize<ManifestDocument>(draft, Json)!, id,
                    await db.DramaAssets.AsNoTracking().Where(x => x.DramaProductionId == id).ToListAsync(ct));
                row.PublishedJson = draft; row.PublishedLegacyJson = null;
            }
            else if (scenes)
                row.PublishedLegacyJson = JsonSerializer.Serialize(await legacy.DetailAsync(id, true), Json);
            row.PublishedAt = DateTime.UtcNow; row.Revision++;
            drama.PublishedAt ??= row.PublishedAt;
        }
        drama.Status = status; drama.IsPublished = status == "PUBLISHED"; drama.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { drama.Id, drama.Status, drama.IsPublished, drama.PublishedAt, revision = row?.Revision ?? 0 };
    }

    public async Task RecordViewAsync(int id, int? userId, string version, CancellationToken ct)
    {
        if (userId is null) return;
        // Upsert dưới khóa để hai request phát media đồng thời không vi phạm khóa duy nhất.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.AcquireAsync(db, $"view:{userId}:{id}", ct);
        if (!await db.DramaViewings.AnyAsync(v => v.UserId == userId && v.DramaProductionId == id, ct))
            db.DramaViewings.Add(new DramaViewing { UserId = userId.Value, DramaProductionId = id, VersionType = version });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public static DramaAssetDto AssetDto(DramaAsset a) => new(a.Id, a.Kind, a.Title,
        $"/api/dramas/{a.DramaProductionId}/assets/{a.Id}/media", a.OriginalFileName, a.ContentType, a.SizeBytes, a.IsPreview,
        a.NameVi, a.NameEn, a.MeaningVi, a.MeaningEn, a.DurationSeconds);
}
