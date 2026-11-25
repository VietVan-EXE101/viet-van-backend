using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Controllers.Admin;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/users")]
public sealed class AdminUsersController(AdminPlatformService service) : ControllerBase
{
    public sealed record LockRequest(bool IsLocked);
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? accessLevel,
        [FromQuery] bool? locked, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await service.UsersAsync(search, accessLevel, locked, page, pageSize, null, ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct) => Ok(await service.UsersAsync(null, null, null, 1, 1, id, ct));
    [HttpPatch("{id:int}/lock")]
    public async Task<IActionResult> Lock(int id, LockRequest request, CancellationToken ct) =>
        Ok(await service.LockAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), id, request.IsLocked, ct));
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/dashboard")]
public sealed class AdminDashboardController(AdminPlatformService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] string groupBy = "day", CancellationToken ct = default) => Ok(await service.DashboardAsync(fromUtc, toUtc, groupBy, ct));
}
