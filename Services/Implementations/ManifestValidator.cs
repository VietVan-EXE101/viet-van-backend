using System.Text.Json;
using VietVan.API.DTOs.Drama;
using VietVan.API.Entities;

namespace VietVan.API.Services.Implementations;

public static class ManifestValidator
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public static HashSet<int> Validate(ManifestDocument document, int dramaId, IReadOnlyCollection<DramaAsset> assets)
    {
        static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
        static bool Key(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 80;
        static bool Position(ManifestPosition? p) => p is not null && double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1;
        Require(document is not null && document.SchemaVersion == 1, "Chỉ chấp nhận schemaVersion=1.");
        Require(document!.Scenes is { Count: > 0 and <= 500 }, "Manifest cần từ 1 đến 500 cảnh.");
        Require(document.Scenes.All(s => s is not null && Key(s.Id)), "ID cảnh không hợp lệ.");
        var sceneIds = document.Scenes.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        Require(sceneIds.Count == document.Scenes.Count && sceneIds.Contains(document.StartSceneId), "ID cảnh trùng hoặc startSceneId không tồn tại.");
        var lookup = assets.Where(a => a.DramaProductionId == dramaId && a.Title != DramaService.CoverAssetTitle).ToDictionary(a => a.Id);
        var referenced = new HashSet<int>();
        DramaAsset Asset(int id, params string[] kinds)
        {
            Require(lookup.TryGetValue(id, out var asset) && kinds.Contains(asset.Kind), $"Asset {id} không tồn tại, sai loại hoặc không thuộc vở kịch.");
            referenced.Add(id);
            return asset!;
        }
        foreach (var scene in document.Scenes)
        {
            Require(scene.DurationMs is > 0 and <= 86_400_000, "durationMs không hợp lệ.");
            Require(scene.NextSceneId is null || sceneIds.Contains(scene.NextSceneId), "nextSceneId không tồn tại.");
            if (scene.BackgroundAssetId is int background) Asset(background, "Background");
            Require(scene.Characters is { Count: <= 200 } && scene.Objects is { Count: <= 200 } && scene.AudioTracks is { Count: <= 200 } && scene.Hotspots is { Count: <= 200 }, "Danh sách thành phần không hợp lệ hoặc quá dài.");
            foreach (var (visuals, kind) in new[] { (scene.Characters!, "Character"), (scene.Objects!, "Object") })
                foreach (var visual in visuals)
                {
                    Require(visual is not null && Position(visual.Position) && double.IsFinite(visual.Scale) && visual.Scale is > 0 and <= 10, "Vị trí/scale không hợp lệ.");
                    Asset(visual!.AssetId, kind);
                }
            foreach (var track in scene.AudioTracks!)
            {
                Require(track is not null && track.StartMs >= 0 && track.EndMs > track.StartMs && track.EndMs <= scene.DurationMs, "Timestamp audio không hợp lệ.");
                var asset = Asset(track!.AssetId, "Audio", "Music", "Effect");
                Require(asset.ContentType.StartsWith("audio/", StringComparison.Ordinal), "Audio track phải là âm thanh.");
                if (asset.DurationSeconds is double seconds)
                    Require(track.EndMs - (long)track.StartMs <= seconds * 1000, "Audio track vượt thời lượng tài nguyên.");
            }
            var hotspotIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hotspot in scene.Hotspots!)
            {
                Require(hotspot is not null && Key(hotspot.Id) && hotspotIds.Add(hotspot.Id), "ID hotspot trùng hoặc không hợp lệ.");
                Require(hotspot!.Position is null || Position(hotspot.Position), "Tọa độ hotspot ngoài khung 0–1.");
                Require((hotspot.StartMs ?? 0) >= 0 && (hotspot.EndMs ?? scene.DurationMs) <= scene.DurationMs && (hotspot.StartMs ?? 0) < (hotspot.EndMs ?? scene.DurationMs), "Timestamp hotspot không hợp lệ.");
                Require(hotspot.ActionType is "SHOW_MEANING" or "GO_TO_SCENE" or "PLAY_AUDIO", "ActionType không được hỗ trợ.");
                if (hotspot.ActionType == "GO_TO_SCENE")
                    Require(hotspot.TargetSceneId is not null && sceneIds.Contains(hotspot.TargetSceneId), "targetSceneId không tồn tại.");
                else Require(hotspot.TargetSceneId is null, "Action này không nhận targetSceneId.");
                var asset = hotspot.ActionType == "PLAY_AUDIO" ? Asset(hotspot.AssetId, "Audio", "Music", "Effect") : Asset(hotspot.AssetId, "Character", "Object");
                if (hotspot.ActionType == "PLAY_AUDIO") Require(asset.ContentType.StartsWith("audio/", StringComparison.Ordinal), "PLAY_AUDIO yêu cầu âm thanh.");
                if (hotspot.ActionType == "SHOW_MEANING") Require(!string.IsNullOrWhiteSpace(asset.NameVi) && !string.IsNullOrWhiteSpace(asset.MeaningVi), "SHOW_MEANING cần NameVi và MeaningVi trong metadata asset.");
            }
        }
        Require(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions).Length <= 1_048_576, "Manifest vượt 1 MiB.");
        return referenced;
    }

    public static HashSet<int> AssetIds(string? json)
    {
        var ids = new HashSet<int>();
        if (json is null) return ids;
        using var doc = JsonDocument.Parse(json);
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var p in element.EnumerateObject())
                {
                    if ((p.Name.Equals("assetId", StringComparison.OrdinalIgnoreCase) || p.Name.EndsWith("AssetId", StringComparison.OrdinalIgnoreCase)) && p.Value.TryGetInt32Safe(out var id)) ids.Add(id);
                    Visit(p.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Visit(child);
        }
        Visit(doc.RootElement);
        return ids;
    }

    private static bool TryGetInt32Safe(this JsonElement element, out int id)
    { id = 0; return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out id); }
}
