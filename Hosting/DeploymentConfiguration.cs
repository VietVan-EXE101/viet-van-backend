using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;

namespace VietVan.API.Hosting;

public static class DeploymentConfiguration
{
    public static bool IsRender(IConfiguration configuration) =>
        string.Equals(configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase);

    public static string? ListenUrl(IConfiguration configuration)
    {
        var configured = configuration["PORT"];
        if (string.IsNullOrWhiteSpace(configured) && !IsRender(configuration)) return null;
        if (string.IsNullOrWhiteSpace(configured)) configured = "10000";
        if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("PORT phải là số nguyên từ 1 đến 65535.");
        return $"http://0.0.0.0:{port}";
    }

    public static string SqlConnection(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var value = configuration.GetConnectionString("DefaultConnection");
        // An ignored, Development-only fallback preserves the existing local database
        // settings without publishing them or overriding environment/User Secrets.
        var localDevelopment = environment.IsDevelopment() && !IsRender(configuration);
        if (string.IsNullOrWhiteSpace(value) && localDevelopment)
            value = new ConfigurationBuilder().SetBasePath(environment.ContentRootPath)
                .AddJsonFile("appsettings.Local.json", optional: true).Build().GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Thiếu ConnectionStrings__DefaultConnection. Xem README phần triển khai Render.");
        SqlConnectionStringBuilder sql;
        try { sql = new SqlConnectionStringBuilder(value); }
        catch (ArgumentException) { throw new InvalidOperationException("ConnectionStrings__DefaultConnection không đúng định dạng SQL Server."); }
        if (sql.DataSource.Contains("\\.", StringComparison.Ordinal))
            throw new InvalidOperationException("SQL Server address chứa dấu \\ trước dấu chấm. Hãy sao chép Connection string chính thức trong Somee > Database > Connection details.");
        var somee = sql.DataSource.TrimEnd('.').EndsWith(".mssql.somee.com", StringComparison.OrdinalIgnoreCase) ||
            sql.DataSource.Contains(".mssql.somee.com,", StringComparison.OrdinalIgnoreCase);
        if (somee && (sql.IntegratedSecurity || string.IsNullOrWhiteSpace(sql.UserID) ||
            string.IsNullOrWhiteSpace(sql.InitialCatalog) || string.IsNullOrWhiteSpace(sql.Password) ||
            sql.Password.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Kết nối Somee cần SQL Authentication và mật khẩu thật. Dán Connection string chính thức vào ConnectionStrings:DefaultConnection trong appsettings.Local.json hoặc User Secrets; trên Render dùng ConnectionStrings__DefaultConnection.");
        if (!localDevelopment)
        {
            var server = sql.DataSource.Trim();
            if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
            var host = server.Split(',')[0].Trim().Trim('[', ']');
            if (sql.IntegratedSecurity || string.IsNullOrWhiteSpace(sql.UserID) || string.IsNullOrWhiteSpace(sql.Password) ||
                string.IsNullOrWhiteSpace(sql.InitialCatalog) || string.IsNullOrWhiteSpace(host) ||
                host is "." or "(local)" || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || server.Contains('\\') ||
                (System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip)))
                throw new InvalidOperationException("Production cần SQL Server từ xa, database và SQL username/password qua ConnectionStrings__DefaultConnection; không dùng localhost, named instance hoặc Windows Authentication.");
        }
        return value;
    }

    public static bool IsLocalSqlServer(string connectionString)
    {
        var source = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
        if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source[4..];
        var host = source.Split('\\')[0].Split(',')[0].Trim().Trim('[', ']');
        return host is "." or "(local)" || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            (System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
    }

    public static void ConfigureRenderProxy(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        // Enabled only on Render: its ingress is the only public route to this port.
        // Render manages proxy addresses. Trust the nearest ingress, never forwarded Host.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
}
