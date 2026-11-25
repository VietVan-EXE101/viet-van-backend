using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Services.Authentication;
using VietVan.API.DTOs.Auth;

namespace VietVan.API.Controllers;

[ApiController, Route("api/auth/google"), EnableRateLimiting("auth-sensitive")]
public sealed class GoogleAuthController(GoogleChallengeStore challenges, IGoogleIdTokenValidator validator,
    IOptions<GoogleSignInOptions> options, GoogleAccountService accounts, IWebHostEnvironment environment) : ControllerBase
{
    public sealed record GoogleTokenRequest([Required, MaxLength(16384)] string IdToken);
    private const string CookieName = "vv_google_challenge";

    private CookieOptions ChallengeCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        // http://localhost:3001 and http://localhost:5080 are same-site. Their
        // different ports still require credentialed CORS, not SameSite=None.
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth/google"
    };

    [HttpPost("challenge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult CreateChallenge()
    {
        if (string.IsNullOrWhiteSpace(options.Value.ClientId)) return StatusCode(503, new { message = "Google Sign-In chưa được cấu hình." });
        if (!Request.IsHttps && !environment.IsDevelopment()) return BadRequest(new { message = "Cần HTTPS." });
        var challenge = challenges.Create();
        Response.Headers.CacheControl = "no-store";
        var cookieOptions = ChallengeCookieOptions();
        cookieOptions.MaxAge = TimeSpan.FromMinutes(5);
        Response.Cookies.Append(CookieName, challenge.Key, cookieOptions);
        return Ok(new { clientId = options.Value.ClientId, nonce = challenge.Nonce });
    }

    private async Task<GoogleIdentity?> IdentityAsync(GoogleTokenRequest request, CancellationToken ct)
    {
        if (!Request.IsHttps && !environment.IsDevelopment()) return null;
        var cookie = Request.Cookies[CookieName];
        Response.Cookies.Delete(CookieName, ChallengeCookieOptions());
        if (string.IsNullOrEmpty(cookie) || cookie.Length > 128) return null;
        var nonce = challenges.Consume(cookie);
        if (nonce is null) return null;
        try { return await validator.ValidateAsync(request.IdToken, nonce, ct); }
        catch (SecurityTokenException) { return null; }
    }

    [HttpPost]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Login(GoogleTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ClientId)) return StatusCode(503, new { message = "Google Sign-In chưa được cấu hình." });
        var identity = await IdentityAsync(request, ct);
        if (identity is null) return Unauthorized(new { message = "Token Google hoặc challenge không hợp lệ." });
        try { return Ok(await accounts.LoginAsync(identity, ct)); }
        catch (UnauthorizedAccessException)
        { return Unauthorized(new { message = "Tài khoản không thể đăng nhập." }); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ACCOUNT_LINK_REQUIRED:", StringComparison.Ordinal))
        { return Conflict(new { code = "ACCOUNT_LINK_REQUIRED", message = ex.Message }); }
    }

    [Authorize, HttpPost("link")]
    public async Task<IActionResult> Link(GoogleTokenRequest request, CancellationToken ct)
    {
        var identity = await IdentityAsync(request, ct);
        if (identity is null) return Unauthorized();
        await accounts.LinkAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), identity, ct);
        return NoContent();
    }
}
