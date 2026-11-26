using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VietVan.API.Hosting;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class EmailDeliveryConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static Dictionary<string, string?> Settings() => new()
    {
        ["RENDER"] = "true", ["Email:Resend:ApiKey"] = "test-secret-sentinel",
        ["Email:Resend:FromAddress"] = "sender@example.test",
        ["Email:VerificationBaseUrl"] = "https://backend.example.test"
    };

    [Fact]
    public async Task GoogleClientConfigurationCannotSupplyEmailTransportCredentials()
    {
        var values = new Dictionary<string, string?> { ["RENDER"] = "true", ["GoogleSignIn:ClientId"] = "oauth-secret-sentinel" };
        using var output = new StringWriter();
        Assert.Equal(1, await EmailDeliveryConfiguration.CheckAsync(Config(values), new PlatformEnvironment { EnvironmentName = "Production" }, output));
        var log = output.ToString();
        Assert.Contains("Provider=Resend", log);
        Assert.Contains("Email:Resend:ApiKey", log); Assert.Contains("Email:Resend:FromAddress", log);
        Assert.Contains("Email:VerificationBaseUrl", log);
        Assert.DoesNotContain("oauth-secret-sentinel", log);
    }

    [Theory]
    [InlineData("REPLACE_RESEND_API_KEY")]
    [InlineData("key-with\nnewline")]
    [InlineData("")]
    public void PlaceholderOrMalformedKeysFailBeforeContactingProvider(string key)
    {
        var settings = Settings(); settings["Email:Resend:ApiKey"] = key;
        Assert.Contains("Email:Resend:ApiKey", EmailDeliveryConfiguration.InvalidKeys(Config(settings), new PlatformEnvironment()));
    }

    [Fact]
    public async Task DiagnosticChecksConfigurationWithoutPrintingValuesOrClaimingDelivery()
    {
        using var output = new StringWriter();
        Assert.Equal(0, await EmailDeliveryConfiguration.CheckAsync(Config(Settings()), new PlatformEnvironment { EnvironmentName = "Production" }, output));
        var log = output.ToString();
        Assert.Contains("ConfigurationReady=True", log);
        Assert.Contains("no email sent", log); Assert.Contains("does not prove", log);
        Assert.DoesNotContain("test-secret-sentinel", log);
        Assert.DoesNotContain("sender@example.test", log); Assert.DoesNotContain("backend.example.test", log);
    }

    [Fact]
    public async Task TestSenderWarnsAboutOtherRecipientsEvenWhenConfigurationIsComplete()
    {
        var settings = Settings(); settings["Email:Resend:FromAddress"] = "onboarding@resend.dev";
        using var output = new StringWriter();
        Assert.Equal(0, await EmailDeliveryConfiguration.CheckAsync(Config(settings), new PlatformEnvironment(), output));
        Assert.Contains("TEST_SENDER_RESTRICTION", output.ToString());
        Assert.DoesNotContain("onboarding@resend.dev", output.ToString());
    }

    [Theory]
    [InlineData("http://backend.example.test")]
    [InlineData("https://backend.example.test/#token-sentinel")]
    public void ProductionVerificationUrlRetainsHttpsAndNoFragmentValidation(string url)
    {
        var settings = Settings(); settings["Email:VerificationBaseUrl"] = url;
        Assert.Contains("Email:VerificationBaseUrl", EmailDeliveryConfiguration.InvalidKeys(Config(settings), new PlatformEnvironment { EnvironmentName = "Production" }));
    }

    private sealed class Logs : ILogger<AccountVerificationService>
    {
        public readonly List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class RejectedEmail : IEmailSender
    {
        public Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider-secret-sentinel", null, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProviderHttpFailureIsDiagnosableWithoutExposingAccountOrToken()
    {
        await using var fixture = await PlatformTestDatabase.CreateAsync(); var user = await fixture.AddUserAsync(verified: false);
        var logs = new Logs(); var verification = new AccountVerificationService(fixture.Db, new RejectedEmail(), Config(Settings()),
            new PlatformEnvironment { EnvironmentName = "Production" }, logs);
        Assert.False(await verification.SendAsync(user.Id));
        var failed = Assert.Single(logs.Messages, line => line.Contains("EmailSendFailed"));
        Assert.Contains("HttpStatus=403", failed);
        Assert.DoesNotContain("provider-secret-sentinel", string.Join("\n", logs.Messages));
        Assert.DoesNotContain(user.Email, string.Join("\n", logs.Messages));
        Assert.False(await verification.SendAsync(user.Id));
        Assert.Contains(logs.Messages, line => line.Contains("Reason=Cooldown"));
    }
}
