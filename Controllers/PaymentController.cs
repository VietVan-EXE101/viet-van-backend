using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VietVan.API.DTOs.Payments;
using VietVan.API.DTOs.Common;
using VietVan.API.Services.Payments;
using Microsoft.AspNetCore.RateLimiting;

namespace VietVan.API.Controllers;

[ApiController]
[Route("api/subscription-packages")]
public sealed class SubscriptionPackagesController(PaymentService service) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    [ProducesResponseType(typeof(List<PackageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List() => Ok(await service.PackagesAsync());
}

[ApiController, Authorize]
[Route("api/subscriptions")]
public sealed class SubscriptionsController(PaymentService service, EntitlementService entitlements) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(await service.MySubscriptionAsync(CurrentUser()));
    /// <summary>Quyền hiện hành được tính lại từ subscription còn hiệu lực.</summary>
    [HttpGet("me/entitlements")]
    public async Task<IActionResult> Entitlements() => Ok(await entitlements.GetAsync(CurrentUser()));
    private int CurrentUser() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController, Authorize, EnableRateLimiting("payment-sensitive")]
[Route("api/payments")]
public sealed class PaymentsController(PaymentService service) : ControllerBase
{
    private int CurrentUser() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("orders")]
    public async Task<IActionResult> Create([FromBody] CreatePaymentOrderRequest r,
        [FromHeader(Name="Idempotency-Key")] string? key)
    {
        try {return Ok(await service.CreateOrderAsync(CurrentUser(),r.PackageId,key ?? ""));}
        catch(ArgumentException e){return BadRequest(new {message=e.Message});}
        catch(InvalidOperationException e){return Conflict(new {message=e.Message});}
    }

    [HttpGet("orders/{orderCode}")]
    public async Task<IActionResult> Get(string orderCode)
    {
        try{return Ok(await service.GetOrderAsync(CurrentUser(),orderCode));}
        catch(KeyNotFoundException){return NotFound(new {message="Không tìm thấy đơn hàng."});}
    }

    [HttpPost("orders/{orderCode}/cancel")]
    public async Task<IActionResult> Cancel(string orderCode)
    {
        try{return Ok(await service.CancelAsync(CurrentUser(),orderCode));}
        catch(KeyNotFoundException){return NotFound();}
        catch(InvalidOperationException e){return Conflict(new {message=e.Message});}
    }

    [HttpGet("history")]
    public async Task<IActionResult> History() => Ok(await service.HistoryAsync(CurrentUser()));
}

// Webhook dùng API Key do chính SePay gửi, KHÔNG xác thực bằng JWT người mua.
[ApiController]
[Route("api/payments/webhooks/sepay")]
[AllowAnonymous]
public sealed class SePayWebhookController(PaymentService service, IOptions<SePayOptions> settings,
    ILogger<SePayWebhookController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> Receive()
    {
        var expected=settings.Value.WebhookApiKey;
        if (string.IsNullOrWhiteSpace(expected) || expected.Length<24)
            return StatusCode(503,new {success=false,message="Webhook chưa được cấu hình."});
        var auth=Request.Headers.Authorization.ToString();
        var prefix="Apikey ";
        if (!auth.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) ||
            !SafeEqual(auth[prefix.Length..].Trim(),expected))
            return Unauthorized(new {success=false});
        string payload;
        using(var reader=new StreamReader(Request.Body,Encoding.UTF8))
            payload=await reader.ReadToEndAsync();
        SePayReceipt receipt;
        try { receipt=SePayWebhookParser.Parse(payload); }
        catch(Exception e) when (e is FormatException or System.Text.Json.JsonException)
        {return BadRequest(new {success=false,message="Dữ liệu giao dịch không hợp lệ."});}
        try
        {
            var outcome=await service.ProcessVerifiedSePayAsync(receipt);
            // Tài liệu SePay: 201 với {success:true} (hỗ trợ webhook thế hệ cũ).
            return StatusCode(201,new {success=true,outcome});
        }
        catch(Exception e)
        {
            logger.LogError(e,"Webhook SePay gặp lỗi khi xử lý transactionId={TxId}",receipt.TransactionId);
            return StatusCode(503,new {success=false,message="Không thể lưu giao dịch; cần gửi lại."});
        }
    }

    private static bool SafeEqual(string a,string b)
    {
        var left=SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var right=SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(left,right);
    }
}

[ApiController, Authorize(Roles="Admin")]
[Route("api/admin/subscription-packages")]
public sealed class AdminSubscriptionPackagesController(PaymentService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get() => Ok(await service.PackagesAsync(true));
    [HttpPost] public Task<IActionResult> Create(EditPackageRequest r) => Save(null,r);
    [HttpPut("{id:int}")] public Task<IActionResult> Update(int id,EditPackageRequest r)=>Save(id,r);
    private async Task<IActionResult> Save(int? id,EditPackageRequest r)
    {
        try{return Ok(await service.SavePackageAsync(id,r));}
        catch(ArgumentException e){return BadRequest(new {message=e.Message});}
        catch(KeyNotFoundException){return NotFound();}
    }
}

[ApiController,Authorize(Roles="Admin")]
[Route("api/admin/payments")]
public sealed class AdminPaymentsController(PaymentService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]string? status=null,
        [FromQuery]string? orderCode=null,[FromQuery]int? userId=null,
        [FromQuery]DateTime? fromUtc=null,[FromQuery]DateTime? toUtc=null) =>
        Ok(await service.AdminOrdersAsync(status,orderCode,userId,fromUtc,toUtc));
    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation() => Ok(await service.AdminReconciliationAsync());
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id)
    {
        try{return Ok(await service.AdminDetailAsync(id));}
        catch(KeyNotFoundException){return NotFound();}
    }
}
