using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VietVan.API.DTOs.Auth;
using VietVan.API.Entities;
using VietVan.API.Services.Interfaces;

namespace VietVan.API.Data;

public static class AdminAccountSetup
{
    public const string Email = "admin@gmail.com";

    public static async Task ConfigureAsync(IServiceProvider services, string password, bool resetPassword = true)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Mật khẩu Admin phải có ít nhất 6 ký tự.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VietVanDbContext>();
        var connection = db.Database.GetDbConnection();
        Console.WriteLine($"Server: {connection.DataSource}; Database: {connection.Database}");

        // Only provision the requested account. Never create, drop or reset schema.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email.Trim().ToLower() == Email);
        if (user is null)
        {
            if (await db.Users.AnyAsync(x => x.Username == Email))
                throw new InvalidOperationException("Username admin@gmail.com đã thuộc tài khoản khác; chưa thay đổi dữ liệu.");

            user = new User { Username = Email, Email = Email, Role = "Admin", CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
        }

        user.Email = Email;
        user.Role = "Admin";
        var hasher = new PasswordHasher<User>();
        if (resetPassword || NeedsPasswordUpdate(hasher, user, password))
            user.PasswordHash = hasher.HashPassword(user, password);
        await db.SaveChangesAsync();

        // Verify through the real login flow before committing the account changes.
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var result = await auth.LoginAsync(new LoginRequest { UsernameOrEmail = Email, Password = password });
        if (result.UserId != user.Id || result.Role != "Admin")
            throw new InvalidOperationException("Kiểm tra đăng nhập Admin thất bại; thay đổi đã được hủy.");

        await transaction.CommitAsync();
        Console.WriteLine($"Thành công: {Email}, role Admin. Đã kiểm tra đăng nhập.");
    }

    private static bool NeedsPasswordUpdate(PasswordHasher<User> hasher, User user, string password)
    {
        try
        {
            return hasher.VerifyHashedPassword(user, user.PasswordHash, password)
                != PasswordVerificationResult.Success;
        }
        catch (FormatException)
        {
            // Old SQL demo accounts may contain a placeholder instead of an Identity hash.
            return true;
        }
    }
}
