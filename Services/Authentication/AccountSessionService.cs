using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;

namespace VietVan.API.Services.Authentication;

public sealed class AccountSessionService(VietVanDbContext db, IEmailSender sender, IConfiguration configuration,
    IWebHostEnvironment environment, ILogger<AccountSessionService> logger)
{
    public async Task ForgotAsync(string email, CancellationToken ct)
    {
        // A reset page is optional for API clients: email the existing single-use token
        // when no page is configured. Reject an explicitly invalid URL before looking up
        // accounts, so configuration errors never disclose whether an email exists.
        var configured = configuration["Email:PasswordResetUrl"];
        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(configured) &&
            (!Uri.TryCreate(configured, UriKind.Absolute, out url) || url.UserInfo.Length > 0 || url.Fragment.Length > 0 ||
             (url.Scheme != "https" && !(environment.IsDevelopment() && url.IsLoopback && url.Scheme == "http"))))
        {
            logger.LogWarning("PasswordResetNotConfigured Key=Email:PasswordResetUrl");
            throw new PasswordResetNotConfiguredException();
        }
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || await db.UserAuthStates.AnyAsync(s => s.UserId == user.Id && s.IsLocked, ct))
        {
            logger.LogInformation("PasswordResetEmailSkipped Reason=AccountNotEligible");
            return;
        }
        var raw = EmailVerificationToken.Create(); var now = DateTime.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await DatabaseLocks.AcquireAsync(db, $"auth-user:{user.Id}", ct);
            if (await db.PasswordResetTokens.AnyAsync(x => x.UserId == user.Id && x.CreatedAt > now.AddMinutes(-1), ct))
            {
                logger.LogInformation("PasswordResetEmailSkipped UserId={UserId} Reason=Cooldown", user.Id);
                return;
            }
            await db.PasswordResetTokens.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.ConsumedAt, now), ct);
            db.PasswordResetTokens.Add(new PasswordResetRecord { UserId = user.Id, EmailSnapshot = user.Email, TokenHash = EmailVerificationToken.Hash(raw), CreatedAt = now, ExpiresAt = now.AddMinutes(30) });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            logger.LogInformation("PasswordResetEmailStarted UserId={UserId}", user.Id);
            var html = $"<p>Bạn đã yêu cầu đặt lại mật khẩu Việt Văn.</p><p>Mã đặt lại mật khẩu:</p><p><code>{WebUtility.HtmlEncode(raw)}</code></p><p>Mã có hiệu lực 30 phút và chỉ dùng được một lần.</p>";
            if (url is not null)
                html += $"<p><a href=\"{WebUtility.HtmlEncode(url.AbsoluteUri + "#" + raw)}\">Đặt lại mật khẩu</a></p>";
            html += "<p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>";
            await sender.SendAsync(user.Email, "Đặt lại mật khẩu Việt Văn", html, ct);
            logger.LogInformation("PasswordResetEmailAcceptedByProvider UserId={UserId}", user.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var smtpStatus = ex is System.Net.Mail.SmtpException smtp ? smtp.StatusCode.ToString() : null;
            var httpStatus = ex is HttpRequestException http ? (int?)http.StatusCode : null;
            logger.LogWarning("PasswordResetEmailFailed UserId={UserId} Sender={Sender} Type={ErrorType} HttpStatus={HttpStatus} SmtpStatus={SmtpStatus}",
                user.Id, sender.GetType().Name, ex.GetType().Name, httpStatus, smtpStatus);
        }
    }

    public async Task<bool> ResetAsync(string token, string password, CancellationToken ct)
    {
        if (password.Length is < 8 or > 128) throw new ArgumentException("Mật khẩu cần từ 8 đến 128 ký tự.");
        byte[] hash; try { hash = EmailVerificationToken.Hash(token); } catch (ArgumentException) { return false; }
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.PasswordResetTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (row is null) return false;
        await DatabaseLocks.AcquireAsync(db, $"auth-user:{row.UserId}", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == row.UserId, ct);
        var state = await db.UserAuthStates.SingleOrDefaultAsync(x => x.UserId == row.UserId, ct);
        if (user is null || state?.IsLocked == true || user.Email != row.EmailSnapshot) return false;
        var consumed = await db.PasswordResetTokens.Where(x => x.Id == row.Id && x.ConsumedAt == null && x.ExpiresAt > now).ExecuteUpdateAsync(u => u.SetProperty(x => x.ConsumedAt, now), ct);
        if (consumed != 1) return false;
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        if (state is null) { state = new UserAuthState { UserId = user.Id, EmailVerified = false }; db.Add(state); }
        state.TokenVersion++;
        await db.PasswordResetTokens.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.ConsumedAt, now), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }

    public async Task LogoutAsync(int userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLocks.AcquireAsync(db, $"auth-user:{userId}", ct);
        var state = await db.UserAuthStates.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (state is null) { state = new UserAuthState { UserId = userId, EmailVerified = true }; db.Add(state); }
        state.TokenVersion++;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
