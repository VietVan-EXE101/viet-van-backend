using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using VietVan.API.Hosting;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public class MediaStorageDeploymentTests
{
    private static Dictionary<string, string?> RenderSettings() => new()
    {
        ["RENDER"] = "true", ["R2:AccountId"] = new string('a', 32),
        ["R2:AccessKeyId"] = new string('b', 32), ["R2:SecretAccessKey"] = new string('c', 64),
        ["R2:BucketName"] = "vietvan-media", ["R2:ServiceUrl"] = $"https://{new string('a', 32)}.r2.cloudflarestorage.com"
    };

    [Fact]
    public void RenderWithOnlyFiveR2VariablesResolvesDurableStorageAcrossRestarts()
    {
        using var media = new TestMediaDirectory(); media.Environment.EnvironmentName = "Production";
        // Each new container builds an independent service provider, with no local files.
        for (var restart = 0; restart < 2; restart++)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(RenderSettings()).Build();
            var services = new ServiceCollection(); services.AddMediaStorage(configuration, media.Environment);
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            Assert.Equal("R2", provider.GetRequiredService<IOptions<StorageOptions>>().Value.Provider);
            Assert.IsType<R2FileStorageService>(scope.ServiceProvider.GetRequiredService<IFileStorageService>());
        }
    }

    [Fact]
    public void ExactFiveDoubleUnderscoreEnvironmentVariablesBindAndOverrideJson()
    {
        using var media = new TestMediaDirectory(); media.Environment.EnvironmentName = "Production";
        var prefix = "VV_STORAGE_" + Guid.NewGuid().ToString("N") + "_";
        var settings = RenderSettings();
        try
        {
            foreach (var pair in settings) Environment.SetEnvironmentVariable(prefix + pair.Key.Replace(":", "__"), pair.Value);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["R2:BucketName"] = "json-wrong", ["R2:AccessKeyId"] = "json-wrong" })
                .AddEnvironmentVariables(prefix).Build();
            var services = new ServiceCollection(); services.AddMediaStorage(configuration, media.Environment);
            using var provider = services.BuildServiceProvider();
            var actual = provider.GetRequiredService<IOptions<R2Options>>().Value;
            Assert.Equal(settings["R2:AccountId"], actual.AccountId);
            Assert.Equal(settings["R2:AccessKeyId"], actual.AccessKeyId);
            Assert.Equal(settings["R2:SecretAccessKey"], actual.SecretAccessKey);
            Assert.Equal(settings["R2:BucketName"], actual.BucketName);
            Assert.Equal(settings["R2:ServiceUrl"], actual.ServiceUrl);
        }
        finally
        { foreach (var pair in settings) Environment.SetEnvironmentVariable(prefix + pair.Key.Replace(":", "__"), null); }
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("Typo")]
    public void RenderRefusesExplicitEphemeralOrUnknownStorage(string selected)
    {
        using var media = new TestMediaDirectory();
        var settings = RenderSettings(); settings["Storage:Provider"] = selected;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMediaStorage(configuration, media.Environment));
    }

    [Fact]
    public void MissingR2CredentialsFailStartupRatherThanResolveLocal()
    {
        using var media = new TestMediaDirectory();
        var settings = RenderSettings(); settings.Remove("R2:SecretAccessKey");
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMediaStorage(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), media.Environment));
        Assert.Contains("R2:SecretAccessKey", error.Message);
        Assert.DoesNotContain(settings["R2:AccessKeyId"]!, error.Message);
    }

    [Fact]
    public void LocalDevelopmentStillUsesLocalAndRenderNeverReadsLocalJson()
    {
        using var media = new TestMediaDirectory();
        File.WriteAllText(Path.Combine(media.Environment.ContentRootPath, "appsettings.Local.json"),
            "{\"Storage\":{\"Provider\":\"Local\"},\"R2\":{\"BucketName\":\"local-wrong\"}}");
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(media.Environment);
        services.AddMediaStorage(new ConfigurationBuilder().Build(), media.Environment);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsType<LocalFileStorageService>(scope.ServiceProvider.GetRequiredService<IFileStorageService>());
        var render = new ServiceCollection();
        render.AddMediaStorage(new ConfigurationBuilder().AddInMemoryCollection(RenderSettings()).Build(), media.Environment);
        using var renderProvider = render.BuildServiceProvider();
        Assert.Equal("vietvan-media", renderProvider.GetRequiredService<IOptions<R2Options>>().Value.BucketName);
        Assert.Equal("R2", renderProvider.GetRequiredService<IOptions<StorageOptions>>().Value.Provider);
    }

    private sealed class CapturedLog : ILogger<R2FileStorageService>
    {
        public readonly List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class UnavailableR2 : IR2ObjectClient
    {
        public Task<RemoteObject?> HeadAsync(string key, CancellationToken ct) => throw new MediaStorageUnavailableException(403, "ACCESS_DENIED");
        public Task<bool> PutFileAsync(string key, string path, string contentType, string sha256, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> PresignGetAsync(string key, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public Task<string> ReadHashAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ReadFailuresKeepProviderAndLogOnlySafeDiagnostics()
    {
        var log = new CapturedLog(); var options = Options.Create(R2StorageTests.Configuration());
        var storage = new R2FileStorageService(new UnavailableR2(), options, log);
        await Assert.ThrowsAsync<MediaStorageUnavailableException>(() => storage.GetAccessAsync(
            "pro/assets/secret-object.gif?X-Amz-Signature=secret-sentinel", default));
        Assert.Contains("ACCESS_DENIED", log.Messages.Single());
        Assert.Contains(options.Value.BucketName, log.Messages.Single());
        Assert.DoesNotContain("secret", log.Messages.Single());
        Assert.DoesNotContain(options.Value.AccessKeyId, log.Messages.Single());
        log.Messages.Clear();
        storage = new R2FileStorageService(new MemoryR2(), options, log);
        Assert.Null(await storage.GetAccessAsync("pro/assets/missing.gif", default));
        Assert.Contains("OBJECT_NOT_FOUND", log.Messages.Single());
    }
}
