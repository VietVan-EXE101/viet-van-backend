namespace VietVan.API.Entities;

public sealed class UserAuthState
{
    public int UserId { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public bool IsLocked { get; set; }
    public int TokenVersion { get; set; }
}

public sealed class PasswordResetRecord
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public string EmailSnapshot { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public sealed class AdminAuditLog
{
    public long Id { get; set; }
    public int AdminUserId { get; set; }
    public int TargetUserId { get; set; }
    public string Action { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EmailVerificationRecord
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string EmailSnapshot { get; set; } = "";
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public sealed class UserExternalLogin
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "Google";
    public string ProviderSubject { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
