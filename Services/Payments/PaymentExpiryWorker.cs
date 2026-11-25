using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using VietVan.API.Entities;
namespace VietVan.API.Services.Payments;

public sealed class PaymentExpiryWorker(IServiceScopeFactory scopes,ILogger<PaymentExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope=scopes.CreateScope();
                    var db=scope.ServiceProvider.GetRequiredService<VietVanDbContext>();
                    // Kiểm tra schema trước khi chạy worker; tiếp tục kiểm tra lại ở chu kỳ sau
                    // để worker tự phục hồi sau khi migration được áp dụng.
                    var connection = db.Database.GetDbConnection();
                    await connection.OpenAsync(stoppingToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.PaymentOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.PaymentOrders', N'Status') IS NOT NULL AND COL_LENGTH(N'dbo.PaymentOrders', N'ExpiresAt') IS NOT NULL THEN 1 ELSE 0 END";
                    var schemaReady = Convert.ToInt32(await command.ExecuteScalarAsync(stoppingToken)) == 1;
                    if (!schemaReady)
                    {
                        logger.LogError("PaymentExpiryWorker chưa chạy: dbo.PaymentOrders hoặc các cột Status/ExpiresAt chưa sẵn sàng. Kiểm tra database và migration Payment; sẽ thử lại sau một phút.");
                        continue;
                    }
                    // Có WHERE Status Pending: không thể ghi đè đơn đã Paid bởi webhook.
                    var expiredCount = await db.PaymentOrders.Where(o=>o.Status==PaymentStates.Pending && o.ExpiresAt<=DateTime.UtcNow)
                        .ExecuteUpdateAsync(x=>x.SetProperty(o=>o.Status,PaymentStates.Expired),stoppingToken);
                    if (expiredCount > 0)
                        logger.LogInformation("Đã chuyển {ExpiredCount} đơn thanh toán quá hạn sang Expired.", expiredCount);
                }
                catch(OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch(Exception e){ logger.LogError(e,"Không thể kiểm tra schema hoặc cập nhật trạng thái đơn hết hạn; sẽ thử lại sau một phút."); }
            }
        }
        catch(OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
