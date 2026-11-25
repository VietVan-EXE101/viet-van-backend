using System.ComponentModel.DataAnnotations;

namespace VietVan.API.DTOs.Drama;

public class DramaRequest
{
    public int? LiteraryWorkId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = "";
    [MaxLength(3000)] public string? Description { get; set; }
    public bool? IsPublished { get; set; }
    public string AccessLevel { get; set; } = "Public";
}

public class DramaCreateFormRequest : DramaRequest
{
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "coverImage")]
    public IFormFile? CoverImage { get; set; }
}

public class DramaSceneRequest
{
    [Required, MaxLength(80)] public string SceneKey { get; set; } = "";
    [Required, MaxLength(250)] public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    [Range(0, int.MaxValue)] public int StartMs { get; set; }
    [Range(1, int.MaxValue)] public int EndMs { get; set; }
    [Range(0, int.MaxValue)] public int AudioOffsetMs { get; set; }
    public int? BackgroundAssetId { get; set; }
    public int? AudioAssetId { get; set; }
    public int? VideoAssetId { get; set; }
}

public class DramaHotspotRequest
{
    public int? VisualAssetId { get; set; }
    [Required] public string Kind { get; set; } = "Object";
    [Required, MaxLength(200)] public string NameVi { get; set; } = "";
    [MaxLength(200)] public string? NameEn { get; set; }
    [Required] public string MeaningVi { get; set; } = "";
    public string? MeaningEn { get; set; }
    [Range(0, 100)] public double X { get; set; }
    [Range(0, 100)] public double Y { get; set; }
    [Range(0.01, 100)] public double Width { get; set; }
    [Range(0.01, 100)] public double Height { get; set; }
    public int? StartMs { get; set; }
    public int? EndMs { get; set; }
    public int ZIndex { get; set; }
    public bool IsEnabled { get; set; } = true;
    [MaxLength(120)] public string? ActionKey { get; set; }
    public List<int> TargetSegmentIds { get; set; } = new();
}

public record DramaAssetDto(int Id, string Kind, string Title, string Url,
    string OriginalFileName, string ContentType, long SizeBytes, bool IsPreview = false,
    string? NameVi = null, string? NameEn = null, string? MeaningVi = null, string? MeaningEn = null, double? DurationSeconds = null);

public record DramaHotspotDto(int Id, string Kind, string NameVi, string? NameEn,
    string MeaningVi, string? MeaningEn, double X, double Y, double Width, double Height,
    int? StartMs, int? EndMs, int ZIndex, bool IsEnabled,
    string? ActionKey, IReadOnlyList<int> TargetSegmentIds, DramaAssetDto? VisualAsset);

public record DramaSceneDto(int Id, string SceneKey, string Title, int SortOrder,
    int StartMs, int EndMs, int AudioOffsetMs, DramaAssetDto? Background,
    DramaAssetDto? Audio, DramaAssetDto? Video, IReadOnlyList<DramaHotspotDto> Hotspots,
    int? PreviewAudioAssetId = null, int? PreviewVideoAssetId = null);

public record DramaDto(int Id, int? LiteraryWorkId, string Title, string? Description,
    bool IsPublished, IReadOnlyList<DramaAssetDto> Assets, IReadOnlyList<DramaSceneDto> Scenes,
    string AccessLevel = "Public", DramaAssetDto? CoverImage = null,
    string Status = "DRAFT", int? FreeVideoAssetId = null, DateTime? PublishedAt = null);

public record DramaSummaryDto(int Id, string Title, string? Description,
    int? LiteraryWorkId, int SceneCount, string AccessLevel = "Public", string Status = "DRAFT", bool HasFreeVideo = false);
