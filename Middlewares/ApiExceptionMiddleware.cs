using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Services.Implementations;
using VietVan.API.DTOs.Common;
using VietVan.API.Services.Authentication;

namespace VietVan.API.Middlewares;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var sql = FindSqlException(ex);
            var (status, message, code) = sql?.Number switch
            {
                207 or 208 => (503, "Cấu trúc database chưa tương thích với Backend. Cần kiểm tra và chạy migration còn thiếu.", "DATABASE_UPGRADE_REQUIRED"),
                -2 or -1 or 2 or 53 or 4060 or 18456 => (503, "Backend chưa kết nối được database. Vui lòng kiểm tra cấu hình và SQL Server.", "DATABASE_UNAVAILABLE"),
                _ => Classify(ex)
            };
            if (status >= 500)
            {
                // Chi tiết chỉ ở log server; response không chứa SQL, hash hoặc connection string.
                logger.LogError(ex, "API failure Type={ErrorType} SqlNumber={SqlNumber} TraceId={TraceId} Path={Path}",
                    ex.GetType().Name, sql?.Number, context.TraceIdentifier, context.Request.Path);
            }
            context.Response.Clear(); context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ApiErrorResponse(message, context.TraceIdentifier, code));
        }
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        if (exception is SqlException sql) return sql;
        // SqlClient connection retries wrap exhausted attempts in AggregateException.
        if (exception is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.Select(FindSqlException).FirstOrDefault(x => x is not null);
        return exception.InnerException is null ? null : FindSqlException(exception.InnerException);
    }

    private static (int Status, string Message, string? Code) Classify(Exception ex)
    {
        if (ex is PasswordResetNotConfiguredException)
            return (503, "Chức năng đặt lại mật khẩu tạm thời chưa sẵn sàng.", "PASSWORD_RESET_NOT_CONFIGURED");
        var (status, message) = ex switch
            {
                UploadException upload => (upload.StatusCode, upload.Message),
                KeyNotFoundException => (404, ex.Message),
                ArgumentException => (400, ex.Message),
                UnauthorizedAccessException => (403, "Không có quyền truy cập."),
                DbUpdateConcurrencyException => (409, "Dữ liệu đã thay đổi. Vui lòng tải lại."),
                DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "Dữ liệu đã tồn tại."),
                DbUpdateException { InnerException: SqlException { Number: 547 } } => (409, "Dữ liệu đang được tham chiếu."),
                InvalidOperationException => (409, "Thao tác không phù hợp trạng thái hiện tại."),
                BadHttpRequestException bad => (bad.StatusCode, "Request không hợp lệ hoặc vượt giới hạn."),
                _ => (500, "Không thể xử lý yêu cầu.")
            };
        return (status, message, status >= 500 ? "INTERNAL_SERVER_ERROR" : null);
    }
}
