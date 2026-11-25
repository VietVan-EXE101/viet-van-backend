using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class ResendEmailSenderTests
{
    private sealed class Provider(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public bool Called { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            Assert.Equal("https://api.resend.com/emails", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key", request.Headers.Authorization?.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("sender@example.test", json.RootElement.GetProperty("from").GetString());
            Assert.Equal("reader@example.test", json.RootElement.GetProperty("to")[0].GetString());
            Assert.Equal("Subject", json.RootElement.GetProperty("subject").GetString());
            Assert.Equal("<p>verification token</p>", json.RootElement.GetProperty("html").GetString());
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ResendEmailSender Sender(HttpClient client) => new(client,
        Options.Create(new ResendEmailOptions { ApiKey = "test-api-key", FromAddress = "sender@example.test" }));

    [Fact]
    public async Task SendsVerificationHtmlThroughHttpsAndRequiresProviderAcceptance()
    {
        using var provider = new Provider(HttpStatusCode.OK, "{\"id\":\"test-delivery\"}"); using var client = new HttpClient(provider);
        await Sender(client).SendAsync("reader@example.test", "Subject", "<p>verification token</p>");
        Assert.True(provider.Called);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"message\":\"private-email-content\"}")]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"message\":\"private-email-content\"}")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task FailedOrUnconfirmedDeliveryNeverClaimsSuccessOrLeaksProviderBody(HttpStatusCode status, string body)
    {
        using var provider = new Provider(status, body); using var client = new HttpClient(provider);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Sender(client).SendAsync("reader@example.test", "Subject", "<p>verification token</p>"));
        Assert.DoesNotContain("private-email-content", exception.Message);
        Assert.DoesNotContain("test-api-key", exception.Message);
        Assert.DoesNotContain("verification token", exception.Message);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotContactProvider()
    {
        using var provider = new Provider(HttpStatusCode.OK, "{}"); using var client = new HttpClient(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ResendEmailSender(client, Options.Create(new ResendEmailOptions())).SendAsync("reader@example.test", "Subject", "Body"));
        Assert.False(provider.Called);
    }
}
