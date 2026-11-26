using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using VietVan.API.Middlewares;
using Xunit;

namespace VietVan.Payment.Tests;

public class ApiExceptionMiddlewareTests
{
    [Fact]
    public async Task InvalidResetPageReturnsServiceErrorWithTraceAndNoConfigurationDetails()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "reset-config-trace" };
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw new VietVan.API.Services.Authentication.PasswordResetNotConfiguredException(), new CaptureLogger());
        await middleware.InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("PASSWORD_RESET_NOT_CONFIGURED", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("reset-config-trace", json.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("Email:PasswordResetUrl", body);
    }

    private sealed class CaptureLogger : ILogger<ApiExceptionMiddleware>
    {
        public Exception? Exception { get; private set; }
        public string? Message { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Exception = exception; Message = formatter(state, exception); }
    }

    // Tạo lỗi provider để kiểm tra middleware, không mở kết nối SQL Server.
    private static SqlException SqlError(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length).First();
        var parameters = constructor.GetParameters().Select(p => p.Name == "infoNumber" ? (object)number :
            p.ParameterType == typeof(string) ? "private-database-diagnostic" :
            p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        var error = constructor.Invoke(parameters);
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(collection, [error]);
        var factory = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .First(m => m.Name == "CreateException" && m.GetParameters().Length == 2);
        return (SqlException)factory.Invoke(null, [collection, "16.0.0"])!;
    }

    [Theory]
    [InlineData(207, "DATABASE_UPGRADE_REQUIRED")]
    [InlineData(208, "DATABASE_UPGRADE_REQUIRED")]
    [InlineData(53, "DATABASE_UNAVAILABLE")]
    [InlineData(18456, "DATABASE_UNAVAILABLE")]
    public async Task DatabaseFailureHasActionableCodeAndLogsTraceWithoutLeakingSql(int number, string code)
    {
        var exception = SqlError(number); var logger = new CaptureLogger();
        var context = new DefaultHttpContext { TraceIdentifier = "login-error-trace" };
        context.Request.Path = "/api/auth/login"; context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw exception, logger);
        await middleware.InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal("login-error-trace", json.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("private-database-diagnostic", body);
        Assert.Same(exception, logger.Exception);
        Assert.Contains("login-error-trace", logger.Message);
    }

    [Fact]
    public async Task ExhaustedConnectionRetriesKeepDatabaseUnavailableContract()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var error = new AggregateException(new AggregateException(SqlError(53)), SqlError(53));
        await new ApiExceptionMiddleware(_ => throw error, new CaptureLogger()).InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("DATABASE_UNAVAILABLE", body);
        Assert.DoesNotContain("private-database-diagnostic", body);
    }

    [Theory]
    [InlineData(10060, "DATABASE_UNAVAILABLE")]
    [InlineData(10054, "DATABASE_UNAVAILABLE")]
    [InlineData(11001, "DATABASE_UNAVAILABLE")]
    [InlineData(233, "DATABASE_UNAVAILABLE")]
    [InlineData(40613, "DATABASE_UNAVAILABLE")]
    [InlineData(207, "DATABASE_UPGRADE_REQUIRED")]
    public async Task PackageReadPreservesWrappedSqlFailureInsteadOfReturningStateConflict(int number, string code)
    {
        var error = new VietVan.API.Services.Payments.SubscriptionPackagesReadException(
            new InvalidOperationException("EF provider wrapper", SqlError(number)));
        var logger = new CaptureLogger();
        var context = new DefaultHttpContext { TraceIdentifier = "packages-db-trace" };
        context.Request.Path = "/api/subscription-packages"; context.Response.Body = new MemoryStream();
        await new ApiExceptionMiddleware(_ => throw error, logger).InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private-database-diagnostic", body);
        Assert.Same(error, logger.Exception); Assert.Contains("packages-db-trace", logger.Message);
    }

    [Fact]
    public async Task UnexplainedPackageReadFailureIsLoggedAsServerErrorWhileBusinessConflictsStay409()
    {
        var logger = new CaptureLogger(); var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var failure = new VietVan.API.Services.Payments.SubscriptionPackagesReadException(new InvalidOperationException("private-query-detail"));
        await new ApiExceptionMiddleware(_ => throw failure, logger).InvokeAsync(context);
        Assert.Equal(500, context.Response.StatusCode); Assert.Same(failure, logger.Exception);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("SUBSCRIPTION_PACKAGES_UNAVAILABLE", body); Assert.DoesNotContain("private-query-detail", body);
        var business = new DefaultHttpContext(); business.Response.Body = new MemoryStream();
        await new ApiExceptionMiddleware(_ => throw new InvalidOperationException("business state"), new CaptureLogger()).InvokeAsync(business);
        Assert.Equal(409, business.Response.StatusCode);
    }
}
