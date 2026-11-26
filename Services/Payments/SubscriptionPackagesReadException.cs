namespace VietVan.API.Services.Payments;

public sealed class SubscriptionPackagesReadException(Exception innerException)
    : Exception("Không thể tải danh sách gói đăng ký.", innerException);
