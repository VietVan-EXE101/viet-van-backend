using Amazon.S3;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

public static class MediaStorageConfiguration
{
    public static IConfiguration Effective(IConfiguration configuration, IWebHostEnvironment environment)
    {
        // File local chỉ làm fallback Development. Env/User Secrets/CLI luôn ưu tiên hơn.
        var local = new ConfigurationBuilder().SetBasePath(environment.ContentRootPath);
        if (environment.IsDevelopment() && !DeploymentConfiguration.IsRender(configuration))
            local.AddJsonFile("appsettings.Local.json", optional: true);
        return local.AddConfiguration(configuration).Build();
    }

    public static void AddMediaStorage(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var effective = Effective(configuration, environment);
        var storage = effective.GetSection("Storage").Get<StorageOptions>() ?? new();
        services.AddSingleton<IOptions<StorageOptions>>(Options.Create(storage));
        if (storage.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
        else if (storage.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            var r2 = effective.GetSection("R2").Get<R2Options>() ?? new();
            _ = r2.Endpoint(); // Lỗi cấu hình an toàn trước khi mở server; không gọi mạng.
            services.AddSingleton<IOptions<R2Options>>(Options.Create(r2));
            services.AddSingleton<IAmazonS3>(_ => R2ObjectClient.Create(r2));
            services.AddSingleton<IR2ObjectClient, R2ObjectClient>();
            services.AddScoped<IFileStorageService, R2FileStorageService>();
        }
        else throw new InvalidOperationException("Storage__Provider chỉ hỗ trợ Local hoặc R2.");
    }
}
