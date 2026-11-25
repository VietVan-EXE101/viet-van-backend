using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietVan.API.DTOs.Drama;
using VietVan.API.Services.Implementations;
using Microsoft.AspNetCore.RateLimiting;
using VietVan.API.Filters;
using VietVan.API.DTOs.Common;

namespace VietVan.API.Controllers.Admin;

/// <summary>API quản trị kịch: thư viện asset, cảnh, hotspot. Yêu cầu JWT role Admin.</summary>
[ApiController]
[Route("api/admin/dramas")]
[Authorize(Roles = "Admin")]
public class AdminDramasController(DramaService dramaService, DramaPlatformService platform) : ControllerBase
{
    [HttpPatch("{dramaId:int}/status")]
    public async Task<IActionResult> Status(int dramaId, DramaStatusRequest request, CancellationToken ct) => Ok(await platform.StatusAsync(dramaId, request.Status, ct));

    [HttpGet("{dramaId:int}/manifest")]
    public async Task<IActionResult> Manifest(int dramaId, CancellationToken ct) => Ok(await platform.DraftAsync(dramaId, ct));

    [HttpPut("{dramaId:int}/manifest"), RequestSizeLimit(1_100_000)]
    public async Task<IActionResult> SaveManifest(int dramaId, SaveManifestRequest request, CancellationToken ct) => Ok(await platform.SaveDraftAsync(dramaId, request, ct));

    [HttpGet("{dramaId:int}/assets")]
    public async Task<IActionResult> Assets(int dramaId, CancellationToken ct) => Ok(await dramaService.ListAssetsAsync(dramaId, ct));

    [HttpPut("{dramaId:int}/assets/{assetId:int}/metadata")]
    public async Task<IActionResult> Metadata(int dramaId, int assetId, AssetMetadataRequest request, CancellationToken ct) => Ok(await dramaService.MetadataAsync(dramaId, assetId, request, ct));

    [HttpPost("{dramaId:int}/video"), Consumes("multipart/form-data"), EnableRateLimiting("drama-upload")]
    [UploadRequest]
    [RequestSizeLimit(211_812_352), RequestFormLimits(MultipartBodyLengthLimit = 211_812_352)]
    public async Task<IActionResult> Video(int dramaId, IFormFile file, CancellationToken ct) => Ok(await dramaService.UploadVideoAsync(dramaId, file, ct));

    [HttpPost("{dramaId:int}/cover"), Consumes("multipart/form-data"), EnableRateLimiting("drama-upload")]
    [UploadRequest]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Cover(int dramaId, IFormFile file, CancellationToken ct) => Ok(await dramaService.UploadCoverAsync(dramaId, file, ct));

    [HttpDelete("{dramaId:int}")]
    public async Task<IActionResult> Delete(int dramaId, CancellationToken ct) { await dramaService.DeleteDraftAsync(dramaId, ct); return NoContent(); }
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await dramaService.ListAsync(true));

    [HttpGet("{dramaId:int}")]
    public Task<IActionResult> Detail(int dramaId) => Handle(() => dramaService.DetailAsync(dramaId, true));

    [HttpPost, Consumes("application/json", "text/json", "application/*+json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<IActionResult> Create([FromBody] DramaRequest request) => Handle(() => dramaService.CreateAsync(request));

    [HttpPost, Consumes("multipart/form-data")]
    [UploadRequest]
    [ProducesResponseType(typeof(DramaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> CreateFromForm([FromForm] DramaCreateFormRequest request, CancellationToken ct) =>
        Handle(() => dramaService.CreateWithCoverAsync(request, ct));

    [HttpPut("{dramaId:int}")]
    public Task<IActionResult> Update(int dramaId, [FromBody] DramaRequest request) =>
        Handle(() => dramaService.UpdateAsync(dramaId, request));

    [HttpPatch("{dramaId:int}/publish")]
    [Obsolete("Dùng PATCH status.")]
    public Task<IActionResult> Publish(int dramaId, [FromQuery] bool value = true) =>
        Handle(() => dramaService.PublishAsync(dramaId, value));

    // multipart/form-data: kind=Background/Audio/Character/Object/Video, title, file.
    [HttpPost("{dramaId:int}/assets")]
    [UploadRequest]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("drama-upload")]
    [RequestSizeLimit(211_812_352)]
    [RequestFormLimits(MultipartBodyLengthLimit = 211_812_352)]
    public async Task<IActionResult> Upload(int dramaId, [FromForm] AssetUploadRequest request, CancellationToken ct)
    {
        if (request.Files is not null) return Ok(await dramaService.UploadBatchAsync(dramaId, new AssetBatchRequest { AssetType = request.AssetType!, Files = request.Files }, ct));
        return Ok(await dramaService.UploadAsync(dramaId, request.Kind!, request.Title!, request.File!, ct));
    }

    [HttpDelete("{dramaId:int}/assets/{assetId:int}")]
    public Task<IActionResult> DeleteAsset(int dramaId, int assetId) =>
        HandleDelete(() => dramaService.DeleteAssetAsync(dramaId, assetId));

    [HttpPost("{dramaId:int}/scenes")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> CreateScene(int dramaId, [FromBody] DramaSceneRequest request) =>
        Handle(() => dramaService.AddSceneAsync(dramaId, request));

    [HttpPut("{dramaId:int}/scenes/{sceneId:int}")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> UpdateScene(int dramaId, int sceneId, [FromBody] DramaSceneRequest request) =>
        Handle(() => dramaService.UpdateSceneAsync(dramaId, sceneId, request));

    [HttpDelete("{dramaId:int}/scenes/{sceneId:int}")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> DeleteScene(int dramaId, int sceneId) =>
        HandleDelete(() => dramaService.DeleteSceneAsync(dramaId, sceneId));

    [HttpPost("{dramaId:int}/scenes/{sceneId:int}/hotspots")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> CreateHotspot(int dramaId, int sceneId, [FromBody] DramaHotspotRequest request) =>
        Handle(() => dramaService.AddHotspotAsync(dramaId, sceneId, request));

    [HttpPut("{dramaId:int}/hotspots/{hotspotId:int}")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> UpdateHotspot(int dramaId, int hotspotId, [FromBody] DramaHotspotRequest request) =>
        Handle(() => dramaService.UpdateHotspotAsync(dramaId, hotspotId, request));

    [HttpDelete("{dramaId:int}/hotspots/{hotspotId:int}")]
    [Obsolete("Lưu toàn bộ cấu hình qua PUT manifest.")]
    public Task<IActionResult> DeleteHotspot(int dramaId, int hotspotId) =>
        HandleDelete(() => dramaService.DeleteHotspotAsync(dramaId, hotspotId));

    private async Task<IActionResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    private async Task<IActionResult> HandleDelete(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
