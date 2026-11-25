namespace VietVan.API.Entities;

// Các trạng thái được lưu bằng chuỗi để dễ đối soát, tuyệt đối không nhận trạng thái từ client.
public static class PaymentStates
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
    public const string RequiresReview = "RequiresReview";
    public const string Refunded = "Refunded";
}

public class PaymentOrder
{
    public long Id { get; set; }
    public string OrderCode { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int PackageId { get; set; }
    public SubscriptionPackage Package { get; set; } = null!;
    public string PackageNameSnapshot { get; set; } = "";
    public decimal Amount { get; set; }
    public int DurationDaysSnapshot { get; set; }
    public int TierLevelSnapshot { get; set; }
    public string? BillingPeriodSnapshot { get; set; }
    public string Currency { get; set; } = "VND";
    public string Provider { get; set; } = "SePay";
    public string Status { get; set; } = PaymentStates.Pending;
    public string IdempotencyKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? ReviewReason { get; set; }
    public ICollection<PaymentBankTransaction> BankTransactions { get; set; } = new List<PaymentBankTransaction>();
}

public class PaymentBankTransaction
{
    public long Id { get; set; }
    public string Provider { get; set; } = "SePay";
    public string ProviderTransactionId { get; set; } = "";
    public long? PaymentOrderId { get; set; }
    public PaymentOrder? PaymentOrder { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string? AccountNumberMasked { get; set; }
    public string? ClaimedOrderCode { get; set; }
    public string Status { get; set; } = PaymentStates.RequiresReview;
    public string ReviewReason { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentWebhookEvent
{
    public long Id { get; set; }
    public string Provider { get; set; } = "SePay";
    public string ProviderEventId { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentAuditLog
{
    public long Id { get; set; }
    public long? PaymentOrderId { get; set; }
    public PaymentOrder? PaymentOrder { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
