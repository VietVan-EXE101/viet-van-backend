namespace VietVan.API.Entities;

// Một vở kịch có thể liên kết với tác phẩm văn học, không phụ thuộc các module khác.
public class DramaProduction
{
    public int Id { get; set; }
    public int? LiteraryWorkId { get; set; }
    public LiteraryWork? LiteraryWork { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublished { get; set; }
    public string Status { get; set; } = "DRAFT";
    public DateTime? PublishedAt { get; set; }
    public int? FreeVideoAssetId { get; set; }
    public int PreviewDurationMs { get; set; } = 120000;
    // Public / FreeAuthenticated / VIP. Mặc định Public để không khóa dữ liệu cũ.
    public string AccessLevel { get; set; } = "Public";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<DramaScene> Scenes { get; set; } = new List<DramaScene>();
    public ICollection<DramaAsset> Assets { get; set; } = new List<DramaAsset>();
}

// Thư viện tài nguyên được lưu riêng theo vở kịch; URL là đường dẫn tới file thực.
public class DramaAsset
{
    public int Id { get; set; }
    public int DramaProductionId { get; set; }
    public DramaProduction DramaProduction { get; set; } = null!;
    public string Kind { get; set; } = "Background"; // Background/Audio/Character/Object/Video
    public string Title { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    // Chỉ đánh dấu file đã cắt riêng, không chứa dữ liệu sau preview.
    public bool IsPreview { get; set; }
    public string? NameVi { get; set; }
    public string? NameEn { get; set; }
    public string? MeaningVi { get; set; }
    public string? MeaningEn { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Hai bản JSON độc lập: sửa bản nháp không thay đổi nội dung khách hàng đang xem.
public sealed class DramaManifest
{
    public int DramaProductionId { get; set; }
    public string? DraftJson { get; set; }
    public string? PublishedJson { get; set; }
    public string? PublishedLegacyJson { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int Revision { get; set; }
}

public sealed class DramaViewing
{
    public int UserId { get; set; }
    public int DramaProductionId { get; set; }
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
    public string VersionType { get; set; } = "FREE_VIDEO";
}

public class CulturalInsightView
{
    public int UserId { get; set; }
    public int DramaProductionId { get; set; }
    public int DramaHotspotId { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}

// Một cảnh gắn background + audio/video, thời gian theo timeline của vở kịch (mili giây).
public class DramaScene
{
    public int Id { get; set; }
    public int DramaProductionId { get; set; }
    public DramaProduction DramaProduction { get; set; } = null!;
    public string SceneKey { get; set; } = "";
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public int AudioOffsetMs { get; set; } // Vị trí seek trong file audio, hỗ trợ dùng chung 1 track.
    public int? BackgroundAssetId { get; set; }
    public DramaAsset? BackgroundAsset { get; set; }
    public int? AudioAssetId { get; set; }
    public DramaAsset? AudioAsset { get; set; }
    public int? VideoAssetId { get; set; }
    public DramaAsset? VideoAsset { get; set; }
    public int? PreviewAudioAssetId { get; set; }
    public int? PreviewVideoAssetId { get; set; }
    public ICollection<DramaHotspot> Hotspots { get; set; } = new List<DramaHotspot>();
}

// Vùng nhấn được định vị % trên khung kịch; hiển thị popup tiếng Việt/Anh.
public class DramaHotspot
{
    public int Id { get; set; }
    public int DramaSceneId { get; set; }
    public DramaScene DramaScene { get; set; } = null!;
    public int? VisualAssetId { get; set; }
    public DramaAsset? VisualAsset { get; set; }
    public string Kind { get; set; } = "Object"; // Character/Object
    public string NameVi { get; set; } = "";
    public string? NameEn { get; set; }
    public string MeaningVi { get; set; } = "";
    public string? MeaningEn { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int? StartMs { get; set; } // Tương đối với đầu cảnh.
    public int? EndMs { get; set; }
    public int ZIndex { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? ActionKey { get; set; }
    public string TargetSegmentIdsJson { get; set; } = "[]";
}
