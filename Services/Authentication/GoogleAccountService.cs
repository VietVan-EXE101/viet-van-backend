using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Payments;

namespace VietVan.API.Services.Authentication;

public sealed class GoogleAccountService(VietVanDbContext db, AuthService auth)
{
    public async Task<VietVan.API.DTOs.Auth.AuthResponse> LoginAsync(GoogleIdentity identity, CancellationToken ct)
    {
        var email = identity.Email.Trim().ToLowerInvariant();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await IdentityLockAsync(identity, ct);
        var login = await db.UserExternalLogins.SingleOrDefaultAsync(x => x.Provider == "Google" && x.ProviderSubject == identity.Subject, ct);
        User user;
        if (login is null)
        {
            if (await db.Users.AnyAsync(x => x.Email == email, ct)) throw new InvalidOperationException("ACCOUNT_LINK_REQUIRED: Đăng nhập tài khoản hiện có rồi liên kết Google.");
            var free = await FreePackageLookup.Query(db.SubscriptionPackages).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Chưa cấu hình gói Free.");
            var now = DateTime.UtcNow;
            user = new User { Username = "google_" + Guid.NewGuid().ToString("N"), Email = email, Role = "Customer", CreatedAt = now };
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(user, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            user.Subscriptions.Add(new UserSubscription { PackageId = free.Id, StartDate = now, EndDate = SubscriptionRules.FreeEndDate(now, free.DurationInDays), IsActive = true });
            db.Users.Add(user); await db.SaveChangesAsync(ct);
            db.UserAuthStates.Add(new UserAuthState { UserId = user.Id, EmailVerified = true, EmailVerifiedAt = now });
            db.UserExternalLogins.Add(new UserExternalLogin { UserId = user.Id, ProviderSubject = identity.Subject, CreatedAt = now, LastLoginAt = now });
        }
        else
        {
            user = await db.Users.SingleAsync(x => x.Id == login.UserId, ct);
            var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (state is null || state.IsLocked || !state.EmailVerified) throw new UnauthorizedAccessException();
            login.LastLoginAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await auth.IssueAsync(user);
    }

    public async Task LinkAsync(int userId, GoogleIdentity identity, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await IdentityLockAsync(identity, ct);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (state?.IsLocked == true || !string.Equals(user.Email, identity.Email.Trim(), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        var login = await db.UserExternalLogins.SingleOrDefaultAsync(x => x.Provider == "Google" && x.ProviderSubject == identity.Subject, ct);
        if (login is not null && login.UserId != userId) throw new InvalidOperationException("Google account đã được liên kết.");
        if (login is null) db.UserExternalLogins.Add(new UserExternalLogin { UserId = userId, ProviderSubject = identity.Subject });
        if (state is null) db.UserAuthStates.Add(new UserAuthState { UserId = userId, EmailVerified = true, EmailVerifiedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task IdentityLockAsync(GoogleIdentity identity, CancellationToken ct)
    {
        static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        await DatabaseLocks.AcquireAsync(db, "google-sub:" + Hash(identity.Subject), ct);
        await DatabaseLocks.AcquireAsync(db, "google-email:" + Hash(identity.Email.Trim().ToLowerInvariant()), ct);
    }
}
