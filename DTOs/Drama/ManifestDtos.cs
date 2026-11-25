using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace VietVan.API.DTOs.Drama;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestDocument
{
    public int SchemaVersion { get; set; }
    public string StartSceneId { get; set; } = "";
    public List<ManifestScene> Scenes { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestScene
{
    public string Id { get; set; } = "";
    public int? BackgroundAssetId { get; set; }
    public int DurationMs { get; set; }
    public string? NextSceneId { get; set; }
    public List<ManifestVisual> Characters { get; set; } = [];
    public List<ManifestVisual> Objects { get; set; } = [];
    public List<ManifestTrack> AudioTracks { get; set; } = [];
    public List<ManifestHotspot> Hotspots { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestVisual
{
    public int AssetId { get; set; }
    public ManifestPosition Position { get; set; } = new();
    public double Scale { get; set; } = 1;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestTrack
{
    public int AssetId { get; set; }
    public int StartMs { get; set; }
    public int EndMs { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManifestHotspot
{
    public string Id { get; set; } = "";
    public int AssetId { get; set; }
    public string ActionType { get; set; } = "SHOW_MEANING";
    public string? TargetSceneId { get; set; }
    public ManifestPosition? Position { get; set; }
    public int? StartMs { get; set; }
    public int? EndMs { get; set; }
}

public sealed record SaveManifestRequest([Required] ManifestDocument Manifest, [Range(0, int.MaxValue)] int Revision);
public sealed record DramaStatusRequest([Required, RegularExpression("^(DRAFT|PUBLISHED|DISABLED)$")] string Status);
public sealed record AssetMetadataRequest(
    [MaxLength(200)] string? NameVi, [MaxLength(200)] string? NameEn,
    [MaxLength(8000)] string? MeaningVi, [MaxLength(8000)] string? MeaningEn,
    [Range(0.001, 86400)] double? DurationSeconds);

public sealed class AssetBatchRequest
{
    [Required] public string AssetType { get; set; } = "";
    [Required, MinLength(1), MaxLength(20)] public List<IFormFile> Files { get; set; } = [];
}

public sealed class AssetUploadRequest : IValidatableObject
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public IFormFile? File { get; set; }
    public string? AssetType { get; set; }
    public List<IFormFile>? Files { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if ((File is null) == (Files is null)) yield return new ValidationResult("Chọn file hoặc files.");
        if (File is not null && (string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(Title))) yield return new ValidationResult("Cần kind và title cho upload một file.");
        if (Files is not null && (Files.Count is < 1 or > 20 || string.IsNullOrWhiteSpace(AssetType))) yield return new ValidationResult("Cần assetType và từ 1 đến 20 files.");
    }
}
