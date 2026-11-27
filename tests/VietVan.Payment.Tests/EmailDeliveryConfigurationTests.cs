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
        ["RENDER"] = "true", ["Email:Provider"] = "Resend", ["Email:Resend:ApiKey"] = "test-secret-sentinel",
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
        Assert.Contains("Provider=Gmail", log);
        Assert.Contains("Email:Gmail:ClientId", log); Assert.Contains("Email:Gmail:ClientSecret", log);
        Assert.Contains("Email:Gmail:RefreshToken", log); Assert.Contains("Email:Gmail:FromAddress", log);
        Assert.Contains("Email:VerificationBaseUrl", log);
        Assert.DoesNotContain("oauth-secret-sentinel", log);
    }

    [Theory]
    [InlineData("Gmail")]
    [InlineData("gmail")]
    public async Task GmailDiagnosticsValidateOnlySelectedTransportWithoutPrintingSecrets(string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RENDER"] = "true", ["Email:Provider"] = provider,
            ["Email:Gmail:ClientId"] = "client-sentinel.apps.googleusercontent.com",
            ["Email:Gmail:ClientSecret"] = "secret-sentinel", ["Email:Gmail:RefreshToken"] = "refresh-sentinel",
            ["Email:Gmail:FromAddress"] = "sender@gmail.com", ["Email:VerificationBaseUrl"] = "https://backend.example.test"
        };
        using var output = new StringWriter();
        Assert.Equal(0, await EmailDeliveryConfiguration.CheckAsync(Config(settings), new PlatformEnvironment { EnvironmentName = "Production" }, output));
        Assert.Contains("Provider=Gmail", output.ToString());
        Assert.Contains("ConfigurationReady=True", output.ToString());
        foreach (var secret in new[] { "client-sentinel", "secret-sentinel", "refresh-sentinel", "sender@gmail.com" })
            Assert.DoesNotContain(secret, output.ToString());
        settings.Remove("Email:Gmail:RefreshToken");
        Assert.Contains("Email:Gmail:RefreshToken", EmailDeliveryConfiguration.InvalidKeys(Config(settings), new PlatformEnvironment()));
    }

    [Fact]
    public void LocalDefaultRemainsSmtpAndRenderDefaultUsesGmail()
    {
        Assert.Equal("Smtp", EmailDeliveryConfiguration.Provider(Config(new())));
        Assert.Equal("Gmail", EmailDeliveryConfiguration.Provider(Config(new() { ["RENDER"] = "true" })));
    }

    [Theory]
    [InlineData("REPLACE_GMAIL_CLIENT_SECRET", "refresh-token", "sender@gmail.com", "Email:Gmail:ClientSecret")]
    [InlineData("client-secret", "bad\ntoken", "sender@gmail.com", "Email:Gmail:RefreshToken")]
    [InlineData("client-secret", "refresh-token", "sender@gmail.com\r\nBcc: other@example.com", "Email:Gmail:FromAddress")]
    public void GmailRejectsPlaceholderSecretsAndHeaderInjection(string secret, string token, string from, string invalid)
    {
        var options = new GmailEmailOptions { ClientId = "client-id", ClientSecret = secret, RefreshToken = token, FromAddress = from };
        Assert.Contains(invalid, options.InvalidKeys());
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
