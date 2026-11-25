using Microsoft.AspNetCore.Mvc;
using VietVan.API.Services.Interfaces;

namespace VietVan.API.Controllers;

// LiteraryWorks tiếp tục phục vụ tên tác giả và liên kết kịch; nội dung đọc đã ngừng hoạt động.
[ApiController, Route("api/works")]
public sealed class WorksController(IWorkService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null) => Ok(await service.GetPublicAsync(page, pageSize, search));
    [HttpGet("{id:int}/content"), Obsolete("Nội dung đọc đã ngừng sử dụng.")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Content(int id) => StatusCode(410, new { message = "Nội dung đọc đã ngừng sử dụng. Dùng /api/dramas." });
}
