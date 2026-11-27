using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class GmailEmailSenderTests
{
    private sealed class Provider(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync(ct)));
            var response = responses[Requests.Count - 1];
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
        }
    }

    private static GmailEmailOptions Options() => new()
    {
        ClientId = "client-sentinel.apps.googleusercontent.com", ClientSecret = "secret-sentinel",
        RefreshToken = "refresh-sentinel", FromAddress = "sender@gmail.com"
    };

    private static GmailEmailSender Sender(HttpClient client, GmailEmailOptions? options = null) =>
        new(client, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    [Fact]
    public async Task RefreshesOfflineGrantAndSendsUtf8MimeWithoutExposingCredentialsToSendEndpoint()
    {
        using var provider = new Provider((HttpStatusCode.OK, "{\"access_token\":\"access-sentinel\"}"),
            (HttpStatusCode.OK, "{\"id\":\"gmail-message\"}"));
        using var client = new HttpClient(provider);
        var subject = string.Concat(Enumerable.Repeat("Đặt lại mật khẩu Việt Văn 🔐 ", 5));
        var html = "<p>Mã đặt lại: private-token-🔐</p>";
        await Sender(client).SendAsync("reader@example.com", subject, html);
        Assert.Equal(2, provider.Requests.Count);
        var refresh = provider.Requests[0];
        Assert.Equal("https://oauth2.googleapis.com/token", refresh.Url);
        Assert.Null(refresh.Auth);
        Assert.Contains("grant_type=refresh_token", refresh.Body);
        Assert.Contains("client_id=client-sentinel.apps.googleusercontent.com", refresh.Body);
        Assert.Contains("client_secret=secret-sentinel", refresh.Body);
        Assert.Contains("refresh_token=refresh-sentinel", refresh.Body);
        var send = provider.Requests[1];
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", send.Url);
        Assert.Equal("Bearer access-sentinel", send.Auth);
        Assert.DoesNotContain("secret-sentinel", send.Body);
        Assert.DoesNotContain("refresh-sentinel", send.Body);
        using var json = JsonDocument.Parse(send.Body);
        var raw = json.RootElement.GetProperty("raw").GetString()!;
        Assert.Matches("^[A-Za-z0-9_-]+$", raw);
        var base64 = raw.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        var mime = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        Assert.StartsWith("From: sender@gmail.com\r\nTo: reader@example.com\r\nSubject: ", mime);
        var parts = mime.Split("\r\n\r\n", 2);
        Assert.Contains("Content-Type: text/html; charset=utf-8", parts[0]);
        Assert.Equal(html, Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])));
        var words = Regex.Matches(parts[0], @"=\?UTF-8\?B\?([^?]+)\?=");
        Assert.All(words.Cast<Match>(), word => Assert.True(word.Length <= 75));
        Assert.Equal(subject, string.Concat(words.Cast<Match>().Select(word => Encoding.UTF8.GetString(Convert.FromBase64String(word.Groups[1].Value)))));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FailedRefreshStopsBeforeEmailAndDoesNotExposeProviderBody(HttpStatusCode status)
    {
        using var provider = new Provider((status, "{\"error\":\"secret-sentinel refresh-sentinel private-token\"}"));
        using var client = new HttpClient(provider);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Sender(client).SendAsync("reader@example.com", "Subject", "private-token"));
        Assert.Equal(status, error.StatusCode);
        Assert.Single(provider.Requests);
        AssertSafe(error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendFailurePreservesSafeHttpStatusAndDoesNotClaimAcceptance(HttpStatusCode status)
    {
        using var provider = new Provider((HttpStatusCode.OK, "{\"access_token\":\"access-sentinel\"}"),
            (status, "{\"error\":\"private-token reader@example.com\"}"));
        using var client = new HttpClient(provider);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Sender(client).SendAsync("reader@example.com", "Subject", "private-token"));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(2, provider.Requests.Count);
        AssertSafe(error);
    }

    [Theory]
    [InlineData("{}", 1)]
    [InlineData("not-json-secret-sentinel", 1)]
    [InlineData("{\"access_token\":\"bad token\"}", 1)]
    [InlineData("{}", 2)]
    [InlineData("not-json-private-token", 2)]
    public async Task IncompleteOrMalformedSuccessResponsesAreSafeFailures(string body, int stage)
    {
        using var provider = new Provider((HttpStatusCode.OK, stage == 1 ? body : "{\"access_token\":\"access-sentinel\"}"),
            (HttpStatusCode.OK, body));
        using var client = new HttpClient(provider);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Sender(client).SendAsync("reader@example.com", "Subject", "private-token"));
        Assert.Equal(stage, provider.Requests.Count);
        AssertSafe(error);
    }

    [Fact]
    public async Task MissingSettingsNeverContactGoogle()
    {
        using var provider = new Provider(); using var client = new HttpClient(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(client, new GmailEmailOptions()).SendAsync("reader@example.com", "Subject", "Body"));
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData("reader@example.com\r\nBcc: other@example.com")]
    [InlineData("Reader <reader@example.com>")]
    public async Task InvalidRecipientCannotInjectMimeHeadersOrContactGoogle(string recipient)
    {
        using var provider = new Provider(); using var client = new HttpClient(provider);
        await Assert.ThrowsAsync<ArgumentException>(() => Sender(client).SendAsync(recipient, "Subject", "Body"));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutSending()
    {
        using var provider = new Provider(); using var client = new HttpClient(provider);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(client).SendAsync("reader@example.com", "Subject", "Body", cancellation.Token));
        Assert.Empty(provider.Requests);
    }

    private static void AssertSafe(Exception error)
    {
        foreach (var secret in new[] { "secret-sentinel", "refresh-sentinel", "access-sentinel", "private-token", "reader@example.com" })
            Assert.DoesNotContain(secret, error.ToString());
    }
}
