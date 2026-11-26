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

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void SomeeOfficialConnectionStringKeywordsAreAcceptedUnchanged(string environment)
    {
        const string value = "workstation id=sample.mssql.somee.com;packet size=4096;user id=sample_login;pwd=sample-only;data source=sample.mssql.somee.com;persist security info=False;initial catalog=Sample;Encrypt=True;TrustServerCertificate=False";
        Assert.Equal(value, DeploymentConfiguration.SqlConnection(Config(("ConnectionStrings:DefaultConnection", value)), new PlatformEnvironment { EnvironmentName = environment }));
        Assert.False(DeploymentConfiguration.IsLocalSqlServer(value));
    }

    [Theory]
    [InlineData("Server=sample.mssql.somee.com;Database=Sample;Integrated Security=True")]
    [InlineData("Server=sample.mssql.somee.com;Database=Sample;User Id=sample;Password=REPLACE_WITH_SOMEE_PASSWORD")]
    [InlineData("Server=sample.mssql.somee.com;Database=Sample;User Id=sample")]
    [InlineData("Server=sample\\.mssql.somee.com;Database=Sample;User Id=sample;Password=sample-only")]
    public void SomeeRejectsWindowsAuthenticationPlaceholdersAndMistypedHostnameEvenInDevelopment(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.SqlConnection(Config(("ConnectionStrings:DefaultConnection", value)), new PlatformEnvironment()));
        Assert.DoesNotContain("sample-only", error.Message);
    }

    [Fact]
    public void RealEnvironmentProviderOverridesJsonConnectionString()
    {
        var prefix = "VIETVAN_CONFIG_TEST_" + Guid.NewGuid().ToString("N") + "_";
        const string value = "Server=environment.mssql.somee.com;Database=Sample;User Id=sample;Password=sample-only";
        Environment.SetEnvironmentVariable(prefix + "ConnectionStrings__DefaultConnection", value);
        try
        {
            using var directory = new TestMediaDirectory();
            File.WriteAllText(Path.Combine(directory.Environment.ContentRootPath, "appsettings.Local.json"),
                "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=localhost;Database=Local;Integrated Security=True\"}}");
            var configuration = new ConfigurationBuilder().SetBasePath(directory.Environment.ContentRootPath)
                .AddJsonFile("appsettings.Local.json").AddEnvironmentVariables(prefix).Build();
            Assert.Equal(value, DeploymentConfiguration.SqlConnection(configuration, directory.Environment));
        }
        finally { Environment.SetEnvironmentVariable(prefix + "ConnectionStrings__DefaultConnection", null); }
    }

    [Theory]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=Local;Integrated Security=True", true)]
    [InlineData("Server=127.0.0.1;Database=Local;Integrated Security=True", true)]
    [InlineData("Server=sample.mssql.somee.com;Database=Sample;User Id=sample;Password=sample-only", false)]
    public void DevelopmentAdminProvisioningCanBeRestrictedToLocalSql(string value, bool local)
        => Assert.Equal(local, DeploymentConfiguration.IsLocalSqlServer(value));
}
