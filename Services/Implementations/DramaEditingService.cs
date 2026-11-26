using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;

namespace VietVan.API.Services.Implementations;

public partial class DramaService
{
    public async Task<DramaAssetDto> ReplaceFileAsync(int dramaId, int assetId, IFormFile file, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, dramaId, ct);
        await PreserveSnapshotAsync(dramaId);
        await EnsureAssetMutableAsync(dramaId, assetId, false, ct);
        var asset = await db.DramaAssets.SingleOrDefaultAsync(a => a.Id == assetId && a.DramaProductionId == dramaId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy asset.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!MediaTypes.TryGetValue(extension, out var type)) throw new UploadException(415, "Định dạng file không hỗ trợ.");
        if (file.Length == 0) throw new ArgumentException("File rỗng.");
        try { ValidateUpload(file, asset.Kind, extension); }
        catch (ArgumentException) { throw new UploadException(file.Length > MaxSizeFor(type.Kind) ? 413 : 415, "Định dạng, MIME hoặc kích thước không hợp lệ."); }
        if (asset.Title == CoverAssetTitle && (extension == ".gif" || file.Length > WorkCoverStorage.MaxSize))
            throw new UploadException(extension == ".gif" ? 415 : 413, "Bìa dùng JPG/PNG/WebP, tối đa 5 MiB.");
        var drama = await FindDramaAsync(dramaId);
        var oldKey = asset.RelativePath;
        var saved = await Storage.SaveAsync(dramaId, asset.Kind, file, extension, MaxSizeFor(type.Kind), ct,
            VietVan.API.Services.Storage.R2FileStorageService.Prefix(asset.Kind, asset.Title == CoverAssetTitle, drama.FreeVideoAssetId == assetId));
        try
        {
            asset.RelativePath = saved.StorageKey; asset.OriginalFileName = saved.OriginalName;
            asset.ContentType = type.Mime; asset.SizeBytes = saved.SizeBytes;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch { await Storage.DeleteAsync(saved.StorageKey); throw; }
        // Chỉ xóa khóa cũ sau commit và khi không còn asset nào dùng chung khóa.
        if (!await db.DramaAssets.AnyAsync(a => a.RelativePath == oldKey, ct))
        {
            try { await Storage.DeleteAsync(oldKey); }
            catch (Exception ex) { logger?.LogWarning("Không dọn được file asset cũ. AssetId={AssetId} ErrorType={ErrorType}", assetId, ex.GetType().Name); }
        }
        return ToAsset(asset);
    }

    public async Task<object> ListAssetsAsync(int id, CancellationToken ct)
    {
        await FindDramaAsync(id);
        return await db.DramaAssets.AsNoTracking().Where(a => a.DramaProductionId == id).OrderBy(a => a.Id)
            .Select(a => new { a.Id, assetType = a.Kind, a.Title, a.OriginalFileName, a.ContentType, a.SizeBytes,
                a.NameVi, a.NameEn, a.MeaningVi, a.MeaningEn, a.DurationSeconds, a.CreatedAt,
                url = "/api/admin/dramas/" + id + "/assets/" + a.Id + "/media" }).ToListAsync(ct);
    }
    private async Task<IDbContextTransaction> BeginLegacyEditAsync(int id)
    {
        var tx = await db.Database.BeginTransactionAsync();
        try { await DatabaseLocks.DramaAsync(db, id); await PreserveSnapshotAsync(id); return tx; }
        catch { await tx.DisposeAsync(); throw; }
    }

    private async Task PreserveSnapshotAsync(int id)
    {
        var drama = await FindDramaAsync(id);
        if (!drama.IsPublished) return;
        var row = await db.DramaManifests.SingleOrDefaultAsync(x => x.DramaProductionId == id);
        if (row?.PublishedJson != null || row?.PublishedLegacyJson != null) return;
        if (row is null) { row = new DramaManifest { DramaProductionId = id }; db.Add(row); }
        row.PublishedLegacyJson = JsonSerializer.Serialize(await DetailAsync(id, true), ManifestValidator.JsonOptions);
        row.PublishedAt = drama.PublishedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task EnsureAssetMutableAsync(int dramaId, int assetId, bool deleting, CancellationToken ct)
    {
        var drama = await FindDramaAsync(dramaId);
        if (deleting && drama.FreeVideoAssetId == assetId) throw new InvalidOperationException("Video FREE đang được sử dụng.");
        if (deleting && drama.IsPublished && await db.DramaAssets.Where(a => a.DramaProductionId == dramaId && a.Title == CoverAssetTitle).OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct) == assetId)
            throw new InvalidOperationException("Ảnh bìa đang được sử dụng. Upload ảnh bìa thay thế trước khi xóa.");
        var manifest = await db.DramaManifests.AsNoTracking().SingleOrDefaultAsync(x => x.DramaProductionId == dramaId, ct);
        if (ManifestValidator.AssetIds(manifest?.PublishedJson).Contains(assetId) ||
            (deleting && ManifestValidator.AssetIds(manifest?.DraftJson).Contains(assetId)))
            throw new InvalidOperationException("Asset đang được manifest tham chiếu. Tạo asset mới để thay thế rồi xuất bản lại.");
        if (manifest?.PublishedLegacyJson is string old && JsonSerializer.Deserialize<DramaDto>(old, ManifestValidator.JsonOptions)!.Assets.Any(a => a.Id == assetId))
            throw new InvalidOperationException("Asset thuộc snapshot đã xuất bản. Xuất bản lại sau khi gỡ tham chiếu.");
    }

    public async Task<DramaAssetDto> UploadVideoAsync(int id, IFormFile file, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct);
        var drama = await FindDramaAsync(id);
        DramaAssetDto? saved = null;
        try
        {
            saved = await UploadAsync(id, "Video", "FREE_VIDEO", file, ct);
            drama.FreeVideoAssetId = saved.Id; drama.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return saved;
        }
        catch
        {
            if (saved is not null)
            {
                var asset = db.ChangeTracker.Entries<DramaAsset>().FirstOrDefault(a => a.Entity.Id == saved.Id)?.Entity;
                if (asset is not null) await Storage.DeleteAsync(asset.RelativePath);
            }
            throw;
        }
    }

    public async Task<DramaAssetDto> UploadCoverAsync(int id, IFormFile file, CancellationToken ct)
    {
        if (file.Length > WorkCoverStorage.MaxSize) throw new UploadException(413, "Ảnh bìa tối đa 5 MiB.");
        return await UploadAsync(id, "Background", CoverAssetTitle, file, ct, true);
    }

    public async Task<IReadOnlyList<DramaAssetDto>> UploadBatchAsync(int id, AssetBatchRequest request, CancellationToken ct)
    {
        if (request.Files is null || request.Files.Count is < 1 or > 20) throw new ArgumentException("Chọn từ 1 đến 20 file.");
        var kind = NormalizeKind(request.AssetType);
        if (kind == "Video") throw new ArgumentException("Upload video FREE qua endpoint video.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct);
        var keys = new List<string>(); var result = new List<DramaAssetDto>();
        try
        {
            foreach (var file in request.Files)
            {
                var title = Path.GetFileNameWithoutExtension(file.FileName.Replace('\\', '/'));
                if (title.Length > 200) title = title[..200];
                var asset = await UploadAsync(id, kind, title, file, ct);
                result.Add(asset);
                keys.Add(db.ChangeTracker.Entries<DramaAsset>().Single(e => e.Entity.Id == asset.Id).Entity.RelativePath);
            }
            await tx.CommitAsync(ct); return result;
        }
        catch { foreach (var key in keys) await Storage.DeleteAsync(key); throw; }
    }

    public async Task<object> MetadataAsync(int id, int assetId, AssetMetadataRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct); await PreserveSnapshotAsync(id);
        await EnsureAssetMutableAsync(id, assetId, false, ct);
        var asset = await db.DramaAssets.SingleOrDefaultAsync(x => x.Id == assetId && x.DramaProductionId == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy asset.");
        asset.NameVi = request.NameVi?.Trim(); asset.NameEn = request.NameEn?.Trim();
        asset.MeaningVi = request.MeaningVi?.Trim(); asset.MeaningEn = request.MeaningEn?.Trim();
        asset.DurationSeconds = request.DurationSeconds;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { asset.Id, asset.NameVi, asset.NameEn, asset.MeaningVi, asset.MeaningEn, asset.DurationSeconds };
    }

    public async Task DeleteDraftAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.DramaAsync(db, id, ct);
        var drama = await FindDramaAsync(id);
        if (drama.Status != "DRAFT" || drama.PublishedAt != null || await db.Set<Review>().AnyAsync(x => x.DramaProductionId == id, ct) || await db.DramaViewings.AnyAsync(x => x.DramaProductionId == id, ct))
            throw new InvalidOperationException("Chỉ xóa bản nháp chưa có lịch sử nghiệp vụ. Hãy dùng DISABLED.");
        // Chỉ xóa bản nháp rỗng; không xóa ngầm tài nguyên hoặc quan hệ dữ liệu cũ.
        if (await db.DramaAssets.AnyAsync(x => x.DramaProductionId == id, ct) || await db.DramaScenes.AnyAsync(x => x.DramaProductionId == id, ct))
            throw new InvalidOperationException("Gỡ tài nguyên và cảnh của bản nháp trước khi xóa.");
        await db.DramaManifests.Where(x => x.DramaProductionId == id).ExecuteDeleteAsync(ct);
        db.Remove(drama); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
