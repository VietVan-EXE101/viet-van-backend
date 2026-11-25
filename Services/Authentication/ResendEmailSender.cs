using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace VietVan.API.Services.Authentication;

public sealed class ResendEmailOptions
{
    public string ApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "";
}

/// <summary>HTTPS email transport for hosts that block SMTP, including Render Free.</summary>
public sealed class ResendEmailSender(HttpClient client, IOptions<ResendEmailOptions> settings) : IEmailSender
{
    private sealed record AcceptedEmail(string Id);

    public async Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default)
    {
        var config = settings.Value;
        if (string.IsNullOrWhiteSpace(config.ApiKey) || !MailAddress.TryCreate(config.FromAddress, out _))
            throw new InvalidOperationException("Cần cấu hình Email:Resend:ApiKey và Email:Resend:FromAddress hợp lệ.");
        if (!MailAddress.TryCreate(recipient, out _)) throw new ArgumentException("Địa chỉ nhận email không hợp lệ.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(new { from = config.FromAddress, to = new[] { recipient }, subject, html });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            // Provider response bodies may include email content. Never log or forward them.
            throw new HttpRequestException("Email HTTPS provider rejected the request.", null, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<AcceptedEmail>(cancellationToken);
        if (string.IsNullOrWhiteSpace(accepted?.Id))
            throw new HttpRequestException("Email HTTPS provider did not confirm acceptance.");
    }
}
