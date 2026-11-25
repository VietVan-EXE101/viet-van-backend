using System.ComponentModel.DataAnnotations;
namespace VietVan.API.DTOs.Payments;

public sealed class CreatePaymentOrderRequest
{
    [Range(1, int.MaxValue)] public int PackageId { get; set; }
    // Giá tiền KHÔNG nằm trong request; snapshot lấy từ DB.
}
public sealed record PaymentOrderDto(string OrderCode, string PackageName, decimal Amount,
    string Currency, int DurationDays, string Status, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? PaidAt, string QrUrl, string TransferContent, string BankName, string BankAccount,
    string AccountHolder, string? ReviewReason, int PackageId);
public sealed record PackageDto(int Id, string Name, string Description, decimal Price,
    int DurationInDays, int TierLevel, string Benefits, bool IsActive, string? Code = null, string? BillingPeriod = null);
public sealed record MySubscriptionDto(int TierLevel, string TierName, DateTime? VipUntil,
    bool IsVip, IReadOnlyList<object> Subscriptions);
public sealed class EditPackageRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    [MaxLength(3000)] public string Benefits { get; set; } = "";
    [Range(0, 1000000000)] public decimal Price { get; set; }
    [Range(1, 3650)] public int DurationInDays { get; set; }
    [Range(0, 100)] public int TierLevel { get; set; }
    public bool IsActive { get; set; } = true;
}
