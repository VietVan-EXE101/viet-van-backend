using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;

namespace VietVan.API.Services.Implementations;

/// <summary>
/// Dữ liệu kịch dùng các bảng riêng. Client chỉ đọc bản đã xuất bản;
/// Admin sửa cảnh, gắn file và hotspot mà không phải thay đổi code frontend.
/// </summary>
public partial class DramaService(VietVanDbContext db, IWebHostEnvironment environment, IFileStorageService? fileStorage = null)
{
    private IFileStorageService Storage => fileStorage ?? new LocalFileStorageService(environment,
        Microsoft.Extensions.Options.Options.Create(new StorageOptions()));
    public const string CoverAssetTitle = "__vietvan_drama_cover__";
    private static readonly Dictionary<string, (string Kind, string Mime)> MediaTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ("Image", "image/jpeg"), [".jpeg"] = ("Image", "image/jpeg"),
            [".png"] = ("Image", "image/png"), [".webp"] = ("Image", "image/webp"),
            [".gif"] = ("Image", "image/gif"),
            [".mp3"] = ("Audio", "audio/mpeg"), [".wav"] = ("Audio", "audio/wav"),
            [".m4a"] = ("Audio", "audio/mp4"), [".ogg"] = ("Audio", "audio/ogg"),
            [".mp4"] = ("Video", "video/mp4"), [".webm"] = ("Video", "video/webm")
        };
    internal static long MaxSizeFor(string kind) => kind switch { "Image" => 10L * 1024 * 1024, "Audio" => 30L * 1024 * 1024, "Video" => 200L * 1024 * 1024, _ => 0 };

    internal static void ValidateUpload(IFormFile file, string kind, string extension)
    {
        if (!MediaTypes.TryGetValue(extension, out var type)) throw new ArgumentException("Định dạng file không hỗ trợ.");
        var expectedKind = kind switch { "Background" or "Character" or "Object" => "Image", "Music" => "Audio", "Effect" => type.Kind is "Image" or "Audio" ? type.Kind : "Unsupported", _ => kind };
        if (type.Kind != expectedKind) throw new ArgumentException("Định dạng file không phù hợp với loại asset.");
        if (file.Length <= 0 || file.Length > MaxSizeFor(type.Kind)) throw new ArgumentException("File rỗng hoặc vượt giới hạn: ảnh 10 MiB, audio 30 MiB, video 200 MiB.");
        var mime = file.ContentType?.Split(';')[0].Trim();
        if (!string.Equals(mime, type.Mime, StringComparison.OrdinalIgnoreCase) &&
            !(extension == ".m4a" && string.Equals(mime, "audio/x-m4a", StringComparison.OrdinalIgnoreCase)) &&
            !(extension == ".wav" && string.Equals(mime, "audio/x-wav", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("MIME không phù hợp với định dạng file.");
    }

    public Task<List<DramaSummaryDto>> ListAsync(bool admin = false) =>
        db.DramaProductions.AsNoTracking()
            .Where(d => admin || (d.IsPublished && d.Status == "PUBLISHED"))
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DramaSummaryDto(d.Id, d.Title, d.Description, d.LiteraryWorkId, d.Scenes.Count, admin ? d.AccessLevel : "Public", d.Status, d.FreeVideoAssetId.HasValue))
            .ToListAsync();

    public async Task<DramaDto> DetailAsync(int dramaId, bool admin = false)
    {
        var drama = await db.DramaProductions.AsNoTracking().AsSplitQuery()
            .Include(x => x.Assets)
            .FirstOrDefaultAsync(x => x.Id == dramaId && (admin || x.IsPublished))
            ?? throw new KeyNotFoundException("Không tìm thấy kịch hoặc kịch chưa được xuất bản.");
        // New drafts, video-only and manifest-based productions may have no legacy scenes.
        // Query existence by the relationship key first: an empty table still fails SQL
        // compilation if an Include projects columns from a different historical schema.
        if (await db.DramaScenes.AnyAsync(x => x.DramaProductionId == dramaId))
            drama.Scenes = await db.DramaScenes.AsNoTracking().AsSplitQuery()
                .Where(x => x.DramaProductionId == dramaId)
                .Include(x => x.BackgroundAsset)
                .Include(x => x.AudioAsset)
                .Include(x => x.VideoAsset)
                .Include(x => x.Hotspots).ThenInclude(x => x.VisualAsset)
                .ToListAsync();
        return new DramaDto(drama.Id, drama.LiteraryWorkId, drama.Title,
            drama.Description, drama.IsPublished,
            drama.Assets.OrderBy(x => x.Id).Select(ToAsset).ToList(),
            drama.Scenes.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => ToScene(x, admin)).ToList(), drama.AccessLevel,
            drama.Assets.Where(x => x.Kind == "Background" && x.Title == CoverAssetTitle).OrderByDescending(x => x.Id).Select(ToAsset).FirstOrDefault(), drama.Status, drama.FreeVideoAssetId, drama.PublishedAt);
    }

    public async Task<DramaSceneDto> SceneAsync(int dramaId, int sceneId, bool admin = false)
    {
        var result = await DetailAsync(dramaId, admin);
        return result.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new KeyNotFoundException("Không tìm thấy cảnh.");
    }

    public async Task<DramaHotspotDto> HotspotAsync(int dramaId, int hotspotId, bool admin = false)
    {
        var hotspot = await db.DramaHotspots.AsNoTracking().Include(x => x.VisualAsset)
            .FirstOrDefaultAsync(x => x.Id == hotspotId && x.DramaScene.DramaProductionId == dramaId &&
                (admin || (x.IsEnabled && x.DramaScene.DramaProduction.IsPublished)));
        return hotspot is not null ? ToHotspot(hotspot) :
            throw new KeyNotFoundException("Không tìm thấy vùng tương tác.");
    }

    private async Task<DramaProduction> InsertDramaAsync(DramaRequest request, CancellationToken ct)
    {
        await ValidateWorkAsync(request.LiteraryWorkId);
        var drama = new DramaProduction
        {
            Title = NotEmpty(request.Title, "Tên kịch"),
            Description = request.Description?.Trim(),
            LiteraryWorkId = request.LiteraryWorkId,
            IsPublished = false,
            AccessLevel = NormalizeAccess(request.AccessLevel)
        };
        db.DramaProductions.Add(drama);
        await db.SaveChangesAsync(ct);
        return drama;
    }

    public Task<DramaDto> CreateAsync(DramaRequest request) => CreateCoreAsync(request, null, CancellationToken.None);

    public Task<DramaDto> CreateWithCoverAsync(DramaCreateFormRequest request, CancellationToken ct) =>
        CreateCoreAsync(request, request.CoverImage, ct);

    private async Task<DramaDto> CreateCoreAsync(DramaRequest request, IFormFile? cover, CancellationToken ct)
    {
        if (request.IsPublished == true) throw new ArgumentException("Tạo bản nháp, tải video/manifest rồi gọi API xuất bản.");
        if (cover is not null && cover.Length > WorkCoverStorage.MaxSize)
            throw new ArgumentException("Ảnh bìa phải có dung lượng tối đa 5 MiB.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var drama = await InsertDramaAsync(request, ct);
            var savedCover = cover is null ? null :
                await UploadAsync(drama.Id, "Background", CoverAssetTitle, cover, ct, isCover: true);
            // Creation has just written a new draft and, optionally, one cover. It has
            // not created scenes. Build the response from those confirmed writes rather
            // than hydrating the unrelated legacy scene/hotspot graph before committing.
            var result = new DramaDto(drama.Id, drama.LiteraryWorkId, drama.Title,
                drama.Description, drama.IsPublished, savedCover is null ? [] : [savedCover],
                [], drama.AccessLevel, savedCover, drama.Status, drama.FreeVideoAssetId, drama.PublishedAt);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            foreach (var asset in db.ChangeTracker.Entries<DramaAsset>().Where(x => x.Entity.Title == CoverAssetTitle))
            {
                Storage.Delete(asset.Entity.RelativePath);
            }
            throw;
        }
    }

    public async Task<DramaDto> UpdateAsync(int dramaId, DramaRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.DramaAsync(db, dramaId);
        await PreserveSnapshotAsync(dramaId);
        var drama = await FindDramaAsync(dramaId);
        if (request.IsPublished.HasValue && request.IsPublished.Value != drama.IsPublished) throw new ArgumentException("Đổi trạng thái qua API status/publish.");
        await ValidateWorkAsync(request.LiteraryWorkId);
        drama.Title = NotEmpty(request.Title, "Tên kịch");
        drama.Description = request.Description?.Trim();
        drama.LiteraryWorkId = request.LiteraryWorkId;
        drama.AccessLevel = NormalizeAccess(request.AccessLevel);
        drama.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return await DetailAsync(dramaId, true);
    }

    public async Task<DramaDto> PublishAsync(int dramaId, bool publish)
    {
        await new DramaPlatformService(db, this).StatusAsync(dramaId, publish ? "PUBLISHED" : "DISABLED", CancellationToken.None);
        return await DetailAsync(dramaId, true);
    }

    public async Task<DramaAssetDto> UploadAsync(int dramaId, string kind, string title, IFormFile file, CancellationToken ct, bool isCover = false)
    {
        await using var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await DatabaseLocks.DramaAsync(db, dramaId, ct);
        await PreserveSnapshotAsync(dramaId);
        DramaAssetDto? result = null;
        try
        {
            result = await UploadCoreAsync(dramaId, kind, title, file, ct, isCover);
            if (ownTransaction is not null) await ownTransaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (result is not null)
            {
                var asset = db.ChangeTracker.Entries<DramaAsset>().FirstOrDefault(x => x.Entity.Id == result.Id)?.Entity;
                if (asset is not null) Storage.Delete(asset.RelativePath);
            }
            throw;
        }
    }

    private async Task<DramaAssetDto> UploadCoreAsync(int dramaId, string kind, string title, IFormFile file, CancellationToken ct, bool isCover)
    {
        await FindDramaAsync(dramaId);
        if (file is null) throw new ArgumentException("Cần chọn file.");
        kind = NormalizeKind(kind);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Tên asset cần từ 1 đến 200 ký tự.");
        if (!isCover && title.Trim() == CoverAssetTitle) throw new ArgumentException("Tên asset được dành riêng cho ảnh bìa.");
        var ext = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        if (isCover && ext == ".gif") throw new UploadException(415, "Ảnh bìa chỉ hỗ trợ JPG/JPEG, PNG hoặc WebP.");
        try { ValidateUpload(file, kind, ext); }
        catch (ArgumentException ex)
        {
            if (MediaTypes.TryGetValue(ext, out var allowed) && file.Length > MaxSizeFor(allowed.Kind)) throw new UploadException(413, ex.Message);
            if (file.Length == 0) throw;
            throw new UploadException(415, ex.Message);
        }
        var fileType = MediaTypes[ext];

        var stored = await Storage.SaveAsync(dramaId, kind, file, ext, MaxSizeFor(fileType.Kind), ct);
        try
        {
            var asset = new DramaAsset
            {
                DramaProductionId = dramaId, Kind = kind, Title = title.Trim(),
                OriginalFileName = stored.OriginalName, RelativePath = stored.StorageKey,
                ContentType = fileType.Mime, SizeBytes = file.Length
            };
            db.DramaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return ToAsset(asset);
        }
        catch
        {
            Storage.Delete(stored.StorageKey);
            throw;
        }
    }

    public async Task DeleteAssetAsync(int dramaId, int assetId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await DatabaseLocks.DramaAsync(db, dramaId);
        await PreserveSnapshotAsync(dramaId);
        var asset = await db.DramaAssets.FirstOrDefaultAsync(x => x.Id == assetId && x.DramaProductionId == dramaId)
            ?? throw new KeyNotFoundException("Không tìm thấy asset.");
        await EnsureAssetMutableAsync(dramaId, assetId, true, CancellationToken.None);
        if (await db.DramaScenes.AnyAsync(x => x.DramaProductionId == dramaId &&
            (x.BackgroundAssetId == assetId || x.AudioAssetId == assetId || x.VideoAssetId == assetId || x.PreviewAudioAssetId == assetId || x.PreviewVideoAssetId == assetId)) ||
            await db.DramaHotspots.AnyAsync(x => x.DramaScene.DramaProductionId == dramaId && x.VisualAssetId == assetId))
            throw new InvalidOperationException("Asset đang được sử dụng trong cảnh/hotspot. Hãy gỡ tham chiếu trước.");
        db.DramaAssets.Remove(asset);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Storage.Delete(asset.RelativePath);
    }

    public async Task<DramaSceneDto> AddSceneAsync(int dramaId, DramaSceneRequest request)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        await FindDramaAsync(dramaId);
        await ValidateSceneAsync(dramaId, request);
        var scene = new DramaScene { DramaProductionId = dramaId };
        AssignScene(scene, request);
        db.DramaScenes.Add(scene);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
        return await SceneAsync(dramaId, scene.Id, true);
    }

    public async Task<DramaSceneDto> UpdateSceneAsync(int dramaId, int sceneId, DramaSceneRequest request)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        var scene = await FindSceneAsync(dramaId, sceneId);
        await ValidateSceneAsync(dramaId, request, sceneId);
        AssignScene(scene, request);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
        return await SceneAsync(dramaId, sceneId, true);
    }

    public async Task DeleteSceneAsync(int dramaId, int sceneId)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        var targets = await db.DramaHotspots.Where(h => h.DramaScene.DramaProductionId == dramaId && h.DramaSceneId != sceneId).Select(h => h.TargetSegmentIdsJson).ToListAsync();
        if (targets.Any(json => (JsonSerializer.Deserialize<List<int>>(json) ?? []).Contains(sceneId)))
            throw new InvalidOperationException("Cảnh đang là đích điều hướng của hotspot.");
        var scene = await FindSceneAsync(dramaId, sceneId);
        db.DramaScenes.Remove(scene);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
    }

    public async Task<DramaHotspotDto> AddHotspotAsync(int dramaId, int sceneId, DramaHotspotRequest request)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        var scene = await FindSceneAsync(dramaId, sceneId);
        await ValidateHotspotAsync(dramaId, scene, request);
        var hotspot = new DramaHotspot { DramaSceneId = sceneId };
        AssignHotspot(hotspot, request);
        db.DramaHotspots.Add(hotspot);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
        return await HotspotAsync(dramaId, hotspot.Id, true);
    }

    public async Task<DramaHotspotDto> UpdateHotspotAsync(int dramaId, int hotspotId, DramaHotspotRequest request)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        var hotspot = await db.DramaHotspots.Include(h => h.DramaScene)
            .FirstOrDefaultAsync(h => h.Id == hotspotId && h.DramaScene.DramaProductionId == dramaId)
            ?? throw new KeyNotFoundException("Không tìm thấy hotspot.");
        await ValidateHotspotAsync(dramaId, hotspot.DramaScene, request);
        AssignHotspot(hotspot, request);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
        return await HotspotAsync(dramaId, hotspotId, true);
    }

    public async Task DeleteHotspotAsync(int dramaId, int hotspotId)
    {
        await using var edit = await BeginLegacyEditAsync(dramaId);
        var hotspot = await db.DramaHotspots.FirstOrDefaultAsync(h =>
            h.Id == hotspotId && h.DramaScene.DramaProductionId == dramaId)
            ?? throw new KeyNotFoundException("Không tìm thấy hotspot.");
        db.DramaHotspots.Remove(hotspot);
        await db.SaveChangesAsync();
        await edit.CommitAsync();
    }

    private async Task<DramaProduction> FindDramaAsync(int id) =>
        await db.DramaProductions.FindAsync(id)
            ?? throw new KeyNotFoundException("Không tìm thấy kịch.");

    private async Task<DramaScene> FindSceneAsync(int dramaId, int sceneId) =>
        await db.DramaScenes.FirstOrDefaultAsync(s => s.Id == sceneId && s.DramaProductionId == dramaId)
            ?? throw new KeyNotFoundException("Không tìm thấy cảnh.");

    private async Task ValidateWorkAsync(int? workId)
    {
        if (workId.HasValue && !await db.LiteraryWorks.AnyAsync(w => w.Id == workId.Value))
            throw new ArgumentException("Tác phẩm liên kết không tồn tại.");
    }

    private async Task ValidateSceneAsync(int dramaId, DramaSceneRequest request, int? excludeId = null)
    {
        if (request.StartMs >= request.EndMs)
            throw new ArgumentException("EndMs phải lớn hơn StartMs.");
        var key = NotEmpty(request.SceneKey, "SceneKey");
        if (await db.DramaScenes.AnyAsync(x => x.DramaProductionId == dramaId &&
            x.SceneKey == key && (!excludeId.HasValue || x.Id != excludeId.Value)))
            throw new InvalidOperationException("SceneKey đã tồn tại trong vở kịch.");
        await ValidateAssetAsync(dramaId, request.BackgroundAssetId, "Background");
        await ValidateAssetAsync(dramaId, request.AudioAssetId, "Audio");
        await ValidateAssetAsync(dramaId, request.VideoAssetId, "Video");
    }

    private async Task ValidateAssetAsync(int dramaId, int? assetId, string kind)
    {
        if (assetId.HasValue && !await db.DramaAssets.AnyAsync(a =>
            a.Id == assetId.Value && a.DramaProductionId == dramaId && a.Kind == kind))
            throw new ArgumentException($"Không tìm thấy asset {kind} thuộc vở kịch này.");
    }

    private async Task ValidateHotspotAsync(int dramaId, DramaScene scene, DramaHotspotRequest request)
    {
        if (request.Kind is not ("Character" or "Object"))
            throw new ArgumentException("Hotspot Kind chỉ hỗ trợ Character hoặc Object.");
        NotEmpty(request.NameVi, "Tên tiếng Việt");
        NotEmpty(request.MeaningVi, "Giải nghĩa tiếng Việt");
        if (!double.IsFinite(request.X) || !double.IsFinite(request.Y) ||
            !double.IsFinite(request.Width) || !double.IsFinite(request.Height) ||
            request.X < 0 || request.Y < 0 || request.Width <= 0 || request.Height <= 0 ||
            request.X + request.Width > 100.0001 || request.Y + request.Height > 100.0001)
            throw new ArgumentException("Vùng hotspot phải nằm trong khung 0–100%.");
        if (request.StartMs < 0 || request.EndMs < 0 ||
            (request.StartMs ?? 0) >= (request.EndMs ?? scene.EndMs - scene.StartMs) ||
            request.EndMs > scene.EndMs - scene.StartMs)
            throw new ArgumentException("Thời gian hotspot phải hợp lệ và tương đối với đầu cảnh.");
        if (request.VisualAssetId.HasValue)
            await ValidateAssetAsync(dramaId, request.VisualAssetId, request.Kind);
        if (request.TargetSegmentIds is null || request.TargetSegmentIds.Count > 50 ||
            request.TargetSegmentIds.Any(x => x <= 0))
            throw new ArgumentException("TargetSegmentIds không hợp lệ.");
        // Các ID điều hướng là ID của DramaScene thuộc cùng kịch (có thể rỗng => nút khóa).
        var targets = request.TargetSegmentIds.Distinct().ToList();
        if (targets.Count > 0 && await db.DramaScenes.CountAsync(x =>
                x.DramaProductionId == dramaId && targets.Contains(x.Id)) != targets.Count)
            throw new ArgumentException("TargetSegmentIds phải trỏ tới cảnh trong cùng kịch.");
    }

    private static void AssignScene(DramaScene target, DramaSceneRequest request)
    {
        target.SceneKey = request.SceneKey.Trim();
        target.Title = NotEmpty(request.Title, "Tên cảnh");
        target.SortOrder = request.SortOrder;
        target.StartMs = request.StartMs;
        target.EndMs = request.EndMs;
        target.AudioOffsetMs = request.AudioOffsetMs;
        target.BackgroundAssetId = request.BackgroundAssetId;
        target.AudioAssetId = request.AudioAssetId;
        target.VideoAssetId = request.VideoAssetId;
    }

    private static void AssignHotspot(DramaHotspot target, DramaHotspotRequest request)
    {
        target.VisualAssetId = request.VisualAssetId;
        target.Kind = request.Kind;
        target.NameVi = request.NameVi.Trim();
        target.NameEn = request.NameEn?.Trim();
        target.MeaningVi = request.MeaningVi.Trim();
        target.MeaningEn = request.MeaningEn?.Trim();
        target.X = request.X;
        target.Y = request.Y;
        target.Width = request.Width;
        target.Height = request.Height;
        target.StartMs = request.StartMs;
        target.EndMs = request.EndMs;
        target.ZIndex = request.ZIndex;
        target.IsEnabled = request.IsEnabled;
        target.ActionKey = string.IsNullOrWhiteSpace(request.ActionKey) ? null : request.ActionKey.Trim();
        target.TargetSegmentIdsJson = JsonSerializer.Serialize(request.TargetSegmentIds.Distinct().ToList());
    }

    private static string NormalizeAccess(string? access) =>
        access is "Public" or "FreeAuthenticated" or "VIP" ? access :
            throw new ArgumentException("AccessLevel phải là Public, FreeAuthenticated hoặc VIP.");

    private static string NotEmpty(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} không được để trống.");

    private static string NormalizeKind(string? kind)
    {
        var kinds = new[] { "Background", "Audio", "Character", "Object", "Video", "Music", "Effect" };
        return kinds.FirstOrDefault(x => x.Equals(kind?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Kind phải là Background, Audio, Character, Object hoặc Video.");
    }

    internal static bool HasExpectedSignature(string path, string ext)
    {
        Span<byte> bytes = stackalloc byte[16];
        using var file = File.OpenRead(path);
        var count = file.Read(bytes);
        return ext switch
        {
            ".jpg" or ".jpeg" => Signature(bytes, count, 0, 0xFF, 0xD8, 0xFF),
            ".png" => Signature(bytes, count, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".gif" => count >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)),
            ".webp" => Signature(bytes, count, 0, 0x52, 0x49, 0x46, 0x46) && Signature(bytes, count, 8, 0x57, 0x45, 0x42, 0x50),
            ".mp3" => Signature(bytes, count, 0, 0x49, 0x44, 0x33) || (count > 1 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0),
            ".wav" => Signature(bytes, count, 0, 0x52, 0x49, 0x46, 0x46) && Signature(bytes, count, 8, 0x57, 0x41, 0x56, 0x45),
            ".m4a" or ".mp4" => Signature(bytes, count, 4, 0x66, 0x74, 0x79, 0x70),
            ".ogg" => Signature(bytes, count, 0, 0x4F, 0x67, 0x67, 0x53),
            ".webm" => Signature(bytes, count, 0, 0x1A, 0x45, 0xDF, 0xA3),
            _ => false
        };
    }

    private static bool Signature(ReadOnlySpan<byte> bytes, int count, int offset, params byte[] signature) =>
        count >= offset + signature.Length && bytes.Slice(offset, signature.Length).SequenceEqual(signature);

    private static DramaAssetDto ToAsset(DramaAsset a) =>
        new(a.Id, a.Kind, a.Title, $"/api/dramas/{a.DramaProductionId}/assets/{a.Id}/media",
            a.OriginalFileName, a.ContentType, a.SizeBytes, a.IsPreview);

    private static DramaHotspotDto ToHotspot(DramaHotspot h) =>
        new(h.Id, h.Kind, h.NameVi, h.NameEn, h.MeaningVi, h.MeaningEn,
            h.X, h.Y, h.Width, h.Height, h.StartMs, h.EndMs, h.ZIndex,
            h.IsEnabled, h.ActionKey,
            JsonSerializer.Deserialize<List<int>>(h.TargetSegmentIdsJson) ?? new List<int>(),
            h.VisualAsset is null ? null : ToAsset(h.VisualAsset));

    private static DramaSceneDto ToScene(DramaScene s, bool admin) =>
        new(s.Id, s.SceneKey, s.Title, s.SortOrder, s.StartMs, s.EndMs,
            s.AudioOffsetMs, s.BackgroundAsset is null ? null : ToAsset(s.BackgroundAsset),
            s.AudioAsset is null ? null : ToAsset(s.AudioAsset),
            s.VideoAsset is null ? null : ToAsset(s.VideoAsset),
            s.Hotspots.Where(x => admin || x.IsEnabled).OrderBy(x => x.ZIndex).ThenBy(x => x.Id)
                .Select(x => admin ? ToHotspot(x) : ToHotspot(x) with { MeaningVi = "", MeaningEn = null, TargetSegmentIds = Array.Empty<int>() }).ToList(), s.PreviewAudioAssetId, s.PreviewVideoAssetId);
}
