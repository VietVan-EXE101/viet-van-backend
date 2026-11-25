using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Data;
using VietVan.API.DTOs.Auth;
using VietVan.API.Entities;
using VietVan.API.Services.Interfaces;
using VietVan.API.Settings;
using VietVan.API.Services.Payments;
using VietVan.API.Services.Authentication;
namespace VietVan.API.Services.Implementations;

public class AuthService(VietVanDbContext db, IOptions<JwtSettings> jwtOpt, ILogger<AuthService> logger, AccountVerificationService verification) : IAuthService
{
    private readonly JwtSettings jwt = jwtOpt.Value; private readonly PasswordHasher<User> hasher = new();
    public async Task<AuthResponse> RegisterAsync(RegisterRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Username) && string.IsNullOrWhiteSpace(r.FullName)) throw new ArgumentException("Cần họ tên hoặc username.");
        var uName = string.IsNullOrWhiteSpace(r.Username) ? "user_" + Guid.NewGuid().ToString("N") : r.Username.Trim();
        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Username == uName || x.Email == email))
            throw new InvalidOperationException("Username hoặc Email đã tồn tại.");
        var free = await FreePackageLookup.Query(db.SubscriptionPackages.AsNoTracking()).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Chưa cấu hình gói Free.");
        var now = DateTime.UtcNow;
        var u = new User { Username = uName, FullName = r.FullName?.Trim(), Email = email, Role = "Customer", CreatedAt = now };
        u.PasswordHash = hasher.HashPassword(u, r.Password);
        // Lưu tài khoản và subscription trong cùng một SaveChanges để tránh tài khoản dở dang.
        u.Subscriptions.Add(new UserSubscription { User = u, PackageId = free.Id, StartDate = now,
            EndDate = SubscriptionRules.FreeEndDate(now, free.DurationInDays), IsActive = true });
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Users.Add(u);
            await db.SaveChangesAsync();
            db.UserAuthStates.Add(new UserAuthState { UserId = u.Id, EmailVerified = false, IsLocked = false });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var sent = await verification.SendAsync(u.Id);
        // Chưa cấp JWT cho tài khoản mới trước khi xác minh email.
        return new AuthResponse(u.Id, u.Username, u.Role, 0, "", u.FullName, true, sent);
    }
    public async Task<AuthResponse> LoginAsync(LoginRequest r)
    {
        var key = r.UsernameOrEmail.Trim();
        var email = key.ToLowerInvariant();
        var u = await db.Users
            .Where(x => x.Username == key || x.Email == email)
            .OrderByDescending(x => x.Email == email)
            .FirstOrDefaultAsync();
        if (u is null)
        {
            var connection = db.Database.GetDbConnection();
            logger.LogWarning(
                "Login failed: account not found. Identifier={Identifier}, Server={Server}, Database={Database}",
                key, connection.DataSource, connection.Database);
            throw new UnauthorizedAccessException("Sai tài khoản hoặc mật khẩu.");
        }

        if (!PasswordMatches(u, r.Password))
        {
            var connection = db.Database.GetDbConnection();
            logger.LogWarning(
                "Login failed: password mismatch. UserId={UserId}, Role={Role}, Server={Server}, Database={Database}",
                u.Id, u.Role, connection.DataSource, connection.Database);
            throw new UnauthorizedAccessException("Sai tài khoản hoặc mật khẩu.");
        }

        var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == u.Id);
        EnsureLoginAllowed(u, state);

        var now = DateTime.UtcNow;
        // Nullable MAX returns null when there are no active subscriptions.
        // Apply the Free tier fallback after SQL execution.
        var tier = await db.UserSubscriptions
            .Where(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now)
            .Select(s => (int?)(s.GrantedTierLevel ?? s.Package.TierLevel))
            .MaxAsync() ?? 0;

        return Build(u, tier, state?.TokenVersion ?? 0);
    }

    private bool PasswordMatches(User user, string password)
    {
        try
        {
            return hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // Hash demo/cũ bị hỏng không được gây 500 hoặc được coi như mật khẩu plain text.
            logger.LogWarning("Login rejected: invalid stored password hash. UserId={UserId} ErrorType={ErrorType}", user.Id, ex.GetType().Name);
            return false;
        }
    }
    internal static void EnsureLoginAllowed(User user, UserAuthState? state)
    {
        // Tài khoản bị khóa luôn bị từ chối, kể cả khi có quyền Admin.
        if (state?.IsLocked == true)
            throw new UnauthorizedAccessException("Tài khoản bị khóa hoặc email chưa được xác minh.");

        // Admin được cấp quyền bỏ qua xác minh email; Customer phải có bằng chứng xác minh.
        if (state?.EmailVerified != true && user.Role != "Admin")
            throw new UnauthorizedAccessException("Tài khoản bị khóa hoặc email chưa được xác minh.");
    }
    public async Task<AuthResponse> IssueAsync(User u)
    {
        var now = DateTime.UtcNow;
        var tier = await db.UserSubscriptions.Where(s => s.UserId == u.Id && s.IsActive && s.StartDate <= now && s.EndDate > now)
            .Select(s => (int?)(s.GrantedTierLevel ?? s.Package.TierLevel)).MaxAsync() ?? 0;
        var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == u.Id);
        EnsureLoginAllowed(u, state);
        return Build(u, tier, state?.TokenVersion ?? 0);
    }
    private AuthResponse Build(User u, int tier, int version) { var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, u.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()), new Claim(ClaimTypes.Name, u.Username), new Claim(ClaimTypes.Role, u.Role), new Claim("SubscriptionTierLevel", tier.ToString()), new Claim("token_version", version.ToString()) }; var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)); var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, expires: DateTime.UtcNow.AddMinutes(jwt.ExpiryMinutes), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)); return new(u.Id, u.Username, u.Role, tier, new JwtSecurityTokenHandler().WriteToken(token), u.FullName); }
}
