using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using VietVan.API.Services.Interfaces;
namespace VietVan.API.Controllers.Admin; [ApiController,Route("api/admin/analytics"),Authorize(Roles="Admin")]
public class AnalyticsController(IAnalyticsService service):ControllerBase { [HttpGet("revenue")] public async Task<IActionResult> Revenue([FromQuery]DateTime? fromDate,[FromQuery]DateTime? toDate){try{return Ok(await service.RevenueAsync(fromDate,toDate));}catch(ArgumentException e){return BadRequest(new{message=e.Message});}} }
