using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.DTOs.Auth;
using VietVan.API.Services.Authentication;
using VietVan.API.Services.Interfaces;
using VietVan.API.Entities;
using Microsoft.AspNetCore.Authorization;
using VietVan.API.Services.Payments;
using VietVan.API.DTOs.Common;

namespace VietVan.API.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(IAuthService service, AccountVerificationService verification, VietVanDbContext db, ILogger<AuthController> logger,
    AccountSessionService sessions, EntitlementService entitlements) : ControllerBase
{
    public sealed record ForgotRequest([Required, EmailAddress, MaxLength(150)] string Email);
    public sealed record ResetRequest([Required, StringLength(43, MinimumLength = 43)] string Token, [Required, StringLength(128, MinimumLength = 8)] string Password);

    [HttpPost("forgot-password"), EnableRateLimiting("auth-sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Forgot(ForgotRequest request, CancellationToken ct)
    { await sessions.ForgotAsync(request.Email, ct); return Ok(new { message = "Nếu tài khoản hợp lệ, email hướng dẫn sẽ được gửi." }); }

    [HttpPost("reset-password"), EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> Reset(ResetRequest request, CancellationToken ct) => await sessions.ResetAsync(request.Token, request.Password, ct)
        ? Ok(new { message = "Mật khẩu đã được cập nhật. Vui lòng đăng nhập lại." }) : BadRequest(new { message = "Liên kết không hợp lệ hoặc đã hết hạn." });

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    { await sessions.LogoutAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct); return NoContent(); }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Username, x.FullName, x.Email, x.Role, x.CreatedAt }).SingleAsync(ct);
        return Ok(new { user.Id, user.Username, user.FullName, user.Email, user.Role, user.CreatedAt, accessLevel = await entitlements.CurrentTierAsync(id, ct) > 0 ? "PRO" : "FREE", proUntil = await entitlements.VipUntilAsync(id, ct) });
    }
    public sealed record VerifyEmailRequest([Required, StringLength(43, MinimumLength = 43)] string Token);
    public sealed record ResendVerificationRequest([Required, EmailAddress, MaxLength(150)] string Email);

    [HttpPost("register"), EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        try { return Ok(await service.RegisterAsync(request)); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [HttpPost("login"), EnableRateLimiting("auth-sensitive")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        try { return Ok(await service.LoginAsync(request)); }
        catch (UnauthorizedAccessException e) { return Unauthorized(new { message = e.Message }); }
    }

    /// <summary>Xác minh email bằng token có thời hạn và chỉ dùng một lần.</summary>
    [HttpPost("verify-email"), EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        if (await verification.VerifyAsync(request.Token, ct)) return Ok(new { message = "Email đã được xác minh." });
        logger.LogInformation("EmailVerificationFailed");
        return BadRequest(new { message = "Liên kết không hợp lệ, đã dùng hoặc đã hết hạn." });
    }

    /// <summary>Gửi lại liên kết; response không tiết lộ email có tồn tại hay không.</summary>
    [HttpPost("resend-verification"), EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> Resend(ResendVerificationRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var userId = await db.Users.AsNoTracking().Where(x => x.Email == email).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (userId.HasValue)
        {
            // Chỉ chủ tài khoản cũ đã đăng nhập mới có thể bắt đầu xác minh cho tài khoản cũ.
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUser) && currentUser == userId.Value &&
                !await db.UserAuthStates.AnyAsync(x => x.UserId == currentUser, ct))
            {
                db.UserAuthStates.Add(new UserAuthState { UserId = currentUser, EmailVerified = false });
                await db.SaveChangesAsync(ct);
            }
            await verification.SendAsync(userId.Value, ct);
        }
        return Ok(new { message = "Nếu tài khoản cần xác minh, email hướng dẫn sẽ được gửi." });
    }
}
