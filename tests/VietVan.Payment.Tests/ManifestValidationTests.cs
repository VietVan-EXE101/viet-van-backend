using System.Text.Json;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;
using Xunit;

namespace VietVan.Payment.Tests;

public class ManifestValidationTests
{
    internal static DramaAsset Asset(int id = 1, int drama = 7, string kind = "Background") => new() { Id = id, DramaProductionId = drama, Kind = kind, ContentType = kind is "Audio" or "Music" ? "audio/mpeg" : "image/png", NameVi = "Nhân vật", MeaningVi = "Giải nghĩa" };
    internal static ManifestDocument Document() => new() { SchemaVersion = 1, StartSceneId = "one", Scenes = [new() { Id = "one", DurationMs = 1000, BackgroundAssetId = 1 }] };

    [Fact]
    public void ValidReferencesAreCollected() => Assert.Equal(new[] { 1 }, ManifestValidator.Validate(Document(), 7, [Asset()]));

    [Theory]
    [InlineData(8, "Background")]
    [InlineData(7, "Character")]
    public void WrongOwnerOrKindIsRejected(int drama, string kind) => Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(Document(), 7, [Asset(drama: drama, kind: kind)]));

    [Fact]
    public void MissingDuplicateOrUnknownSceneRejected()
    {
        var doc = Document(); doc.StartSceneId = "missing";
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset()]));
        doc = Document(); doc.Scenes.Add(doc.Scenes[0]);
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset()]));
        doc = Document(); doc.Scenes[0].NextSceneId = "missing";
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset()]));
    }

    [Theory]
    [InlineData(-0.01, 0.5, 1)]
    [InlineData(1.01, 0.5, 1)]
    [InlineData(0.5, 0.5, 0)]
    [InlineData(double.NaN, 0.5, 1)]
    public void InvalidVisualCoordinatesRejected(double x, double y, double scale)
    {
        var doc = Document(); doc.Scenes[0].Characters.Add(new() { AssetId = 2, Position = new() { X = x, Y = y }, Scale = scale });
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset(), Asset(2, kind: "Character")]));
    }

    [Fact]
    public void TracksAndHotspotsCannotEscapeSceneOrReferenceUnknownActions()
    {
        var doc = Document(); doc.Scenes[0].AudioTracks.Add(new() { AssetId = 2, StartMs = 0, EndMs = 1001 });
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset(), Asset(2, kind: "Audio")]));
        doc = Document(); doc.Scenes[0].Hotspots.Add(new() { Id = "h", AssetId = 2, ActionType = "EXECUTE" });
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset(), Asset(2, kind: "Object")]));
        doc.Scenes[0].Hotspots[0].ActionType = "GO_TO_SCENE"; doc.Scenes[0].Hotspots[0].TargetSceneId = "missing";
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset(), Asset(2, kind: "Object")]));
    }

    [Fact]
    public void NullCollectionsAndUnrecognizedFieldsRejected()
    {
        var doc = Document(); doc.Scenes[0].Hotspots = null!;
        Assert.Throws<ArgumentException>(() => ManifestValidator.Validate(doc, 7, [Asset()]));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ManifestDocument>("{\"schemaVersion\":1,\"script\":\"x\"}", ManifestValidator.JsonOptions));
    }
}
