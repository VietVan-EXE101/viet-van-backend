using System.Net;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;

namespace VietVan.API.Services.Authentication;

public sealed class AccountVerificationService(VietVanDbContext db, IEmailSender sender,
    IConfiguration configuration, IWebHostEnvironment environment, ILogger<AccountVerificationService> logger)
{
    private string VerificationUrl(string token)
    {
        var uri = EmailDeliveryConfiguration.VerificationBaseUri(configuration, environment);
        return uri.AbsoluteUri.TrimEnd('/') + "/verify-email.html#" + Uri.EscapeDataString(token);
    }

    public async Task<bool> SendAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (user is null || state is null || state.EmailVerified || state.IsLocked)
        {
            // Internal reason only; public resend response remains identical for all accounts.
            logger.LogInformation("EmailVerificationSkipped UserId={UserId} Reason={Reason}", userId,
                user is null ? "AccountMissing" : state is null ? "AuthStateMissing" : state.EmailVerified ? "AlreadyVerified" : "AccountLocked");
            return false;
        }
        logger.LogInformation("EmailVerificationRequested UserId={UserId}", userId);
        var raw = EmailVerificationToken.Create();
        string url;
        try { url = VerificationUrl(raw); }
        catch (InvalidOperationException) { logger.LogWarning("VerificationBaseUrl chưa được cấu hình hợp lệ."); return false; }
        var now = DateTime.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var resource = $"email-verify:{userId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Verification lock timeout', 1;", ct);
            if (await db.EmailVerificationTokens.AnyAsync(x => x.UserId == userId && x.CreatedAt > now.AddMinutes(-1), ct))
            {
                logger.LogInformation("EmailVerificationSkipped UserId={UserId} Reason=Cooldown", userId);
                return false;
            }
            // Chỉ dọn token của tài khoản này sau khi đã hết hạn hơn bảy ngày.
            await db.EmailVerificationTokens.Where(x => x.UserId == userId && x.ExpiresAt < now.AddDays(-7))
                .ExecuteDeleteAsync(ct);
            await db.EmailVerificationTokens.Where(x => x.UserId == userId && x.ConsumedAt == null)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.ConsumedAt, now), ct);
            db.EmailVerificationTokens.Add(new EmailVerificationRecord { UserId = userId, EmailSnapshot = user.Email,
                TokenHash = EmailVerificationToken.Hash(raw), CreatedAt = now, ExpiresAt = now.AddHours(1) });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        logger.LogInformation("VerificationTokenCreated UserId={UserId}", userId);
        try
        {
            var html = $"<p>Xin chào,</p><p>Vui lòng xác minh email Việt Văn trong vòng một giờ:</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">Xác minh email</a></p><p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>";
            logger.LogInformation("EmailSendStarted UserId={UserId}", userId);
            await sender.SendAsync(user.Email, "Xác minh email Việt Văn", html, ct);
            logger.LogInformation("EmailSendAcceptedByProvider UserId={UserId}", userId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Không ghi token, nội dung email hay chi tiết provider có thể chứa dữ liệu nhạy cảm.
            var smtpStatus = ex is System.Net.Mail.SmtpException smtp ? smtp.StatusCode.ToString() : null;
            var httpStatus = ex is HttpRequestException http ? (int?)http.StatusCode : null;
            logger.LogWarning("EmailSendFailed UserId={UserId} Sender={Sender} ErrorType={ErrorType} HttpStatus={HttpStatus} SmtpStatus={SmtpStatus}",
                userId, sender.GetType().Name, ex.GetType().Name, httpStatus, smtpStatus);
            return false;
        }
    }

    public async Task<bool> VerifyAsync(string token, CancellationToken ct = default)
    {
        byte[] hash;
        try { hash = EmailVerificationToken.Hash(token); } catch (ArgumentException) { return false; }
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // UPDATE có điều kiện là điểm tiêu thụ nguyên tử khi nhiều request gửi cùng token.
        var row = await db.EmailVerificationTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now) return false;
        var consumed = await db.EmailVerificationTokens.Where(x => x.Id == row.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.ConsumedAt, now), ct);
        if (consumed != 1) return false;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.UserId, ct);
        if (user is null || !string.Equals(user.Email, row.EmailSnapshot, StringComparison.OrdinalIgnoreCase)) return false;
        var updated = await db.UserAuthStates.Where(x => x.UserId == row.UserId && !x.IsLocked && !x.EmailVerified)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.EmailVerified, true).SetProperty(s => s.EmailVerifiedAt, now), ct);
        if (updated != 1) return false;
        await tx.CommitAsync(ct);
        logger.LogInformation("EmailVerificationSucceeded UserId={UserId}", row.UserId);
        return true;
    }
}
