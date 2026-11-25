using Microsoft.Extensions.Configuration;
using VietVan.API.Hosting;
using Xunit;

namespace VietVan.Payment.Tests;

public class DeploymentConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => x.Value)).Build();

    [Fact]
    public void LocalLaunchProfileIsNotOverriddenWithoutPortOrRender()
        => Assert.Null(DeploymentConfiguration.ListenUrl(Config()));

    [Theory]
    [InlineData(null, "http://0.0.0.0:10000")]
    [InlineData("12345", "http://0.0.0.0:12345")]
    public void RenderUsesPortAndBindsAllIpv4Interfaces(string? port, string expected)
        => Assert.Equal(expected, DeploymentConfiguration.ListenUrl(Config(("RENDER", "true"), ("PORT", port))));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("not-a-port")]
    public void InvalidPortFailsClearly(string port)
        => Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.ListenUrl(Config(("PORT", port))));

    [Theory]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=Test;Integrated Security=True")]
    [InlineData("Server=remote.example.test;Database=Test;Integrated Security=True")]
    [InlineData("Server=localhost;Database=Test;User Id=test;Password=sample")]
    [InlineData("Server=tcp:127.0.0.1,1433;Database=Test;User Id=test;Password=sample")]
    [InlineData("Server=[::1],1433;Database=Test;User Id=test;Password=sample")]
    [InlineData("Server=remote.example.test;Database=Test;User Id=test")]
    [InlineData("")]
    public void ProductionRefusesLocalWindowsOrIncompleteSqlConfiguration(string connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.SqlConnection(
            Config(("ConnectionStrings:DefaultConnection", connection)), new PlatformEnvironment { EnvironmentName = "Production" }));
        Assert.DoesNotContain("Password=", error.Message);
    }

    [Fact]
    public void RemoteSqlConnectionIsPreservedWithoutSwitchingDatabaseProvider()
    {
        const string connection = "Server=tcp:sql.example.test,1433;Database=VietVanDemo;User Id=test;Password=sample;Encrypt=True;TrustServerCertificate=False";
        Assert.Equal(connection, DeploymentConfiguration.SqlConnection(Config(("ConnectionStrings:DefaultConnection", connection)), new PlatformEnvironment { EnvironmentName = "Production" }));
    }

    [Fact]
    public void DevelopmentFallbackDoesNotOverrideEnvironmentConfiguration()
    {
        using var directory = new TestMediaDirectory();
        File.WriteAllText(Path.Combine(directory.Environment.ContentRootPath, "appsettings.Local.json"),
            "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=localhost;Database=Local;Integrated Security=True\"}}");
        Assert.Contains("Database=Local", DeploymentConfiguration.SqlConnection(Config(), directory.Environment));
        const string configured = "Server=localhost;Database=EnvironmentOverride;Integrated Security=True";
        Assert.Equal(configured, DeploymentConfiguration.SqlConnection(Config(("ConnectionStrings:DefaultConnection", configured)), directory.Environment));
    }
}
