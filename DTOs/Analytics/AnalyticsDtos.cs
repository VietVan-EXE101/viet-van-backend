namespace VietVan.API.DTOs.Analytics;
public record TransactionDto(long Id,int UserId,string Username,string PackageName,decimal Amount,DateTime PaymentDate,string Status);
public record RevenueAnalyticsResponse(decimal TotalRevenue,int SuccessfulTransactions,IReadOnlyList<TransactionDto> RecentTransactions);
