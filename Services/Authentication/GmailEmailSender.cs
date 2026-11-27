using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VietVan.API.Services.Authentication;

public sealed class GmailEmailOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string FromAddress { get; set; } = "";

    public IReadOnlyList<string> InvalidKeys()
    {
        var invalid = new List<string>();
        if (!CredentialPresent(ClientId)) invalid.Add("Email:Gmail:ClientId");
        if (!CredentialPresent(ClientSecret)) invalid.Add("Email:Gmail:ClientSecret");
        if (!CredentialPresent(RefreshToken)) invalid.Add("Email:Gmail:RefreshToken");
        if (!IsPlainAddress(FromAddress)) invalid.Add("Email:Gmail:FromAddress");
        return invalid;
    }

    private static bool CredentialPresent(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsWhiteSpace) && !value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlainAddress(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(c => char.IsControl(c) || c > 127) && MailAddress.TryCreate(value, out var address) &&
        address.Address == value;
}

/// <summary>Gmail HTTPS delivery using the sender's offline gmail.send grant. No database or storage access.</summary>
public sealed class GmailEmailSender(HttpClient client, IOptions<GmailEmailOptions> settings) : IEmailSender
{
    private sealed record AccessToken([property: JsonPropertyName("access_token")] string? Value);
    private sealed record AcceptedEmail(string? Id);

    public async Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default)
    {
        var config = settings.Value;
        var invalid = config.InvalidKeys();
        if (invalid.Count > 0)
            throw new InvalidOperationException("Thiếu/sai cấu hình Gmail API: " + string.Join(", ", invalid));
        if (!GmailEmailOptions.IsPlainAddress(recipient)) throw new ArgumentException("Địa chỉ nhận email không hợp lệ.");

        // Refresh per delivery: no token files or shared mutable Authorization headers.
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret,
                ["refresh_token"] = config.RefreshToken, ["grant_type"] = "refresh_token"
            })
        };
        using var tokenResponse = await client.SendAsync(refresh, cancellationToken);
        RequireSuccess(tokenResponse, "Gmail OAuth refresh rejected.");
        var token = await ReadSafeAsync<AccessToken>(tokenResponse, cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.Value) || token.Value.Any(char.IsWhiteSpace))
            throw new HttpRequestException("Gmail OAuth did not return a usable access token.");

        var mime = $"From: {config.FromAddress}\r\nTo: {recipient}\r\nSubject: {EncodeSubject(subject)}\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(html), Base64FormattingOptions.InsertLineBreaks) + "\r\n";
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var send = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        send.Content = JsonContent.Create(new { raw });
        using var response = await client.SendAsync(send, cancellationToken);
        RequireSuccess(response, "Gmail API rejected the email.");
        var accepted = await ReadSafeAsync<AcceptedEmail>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(accepted?.Id))
            throw new HttpRequestException("Gmail API did not confirm acceptance.");
    }

    private static void RequireSuccess(HttpResponseMessage response, string message)
    {
        // Google error bodies can contain addresses or credentials. Do not forward or log them.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<T?> ReadSafeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadFromJsonAsync<T>(ct); }
        catch (JsonException) { throw new HttpRequestException("Gmail API returned an invalid response.", null, response.StatusCode); }
    }

    private static string EncodeSubject(string subject)
    {
        // RFC 2047 encoded words, each below 75 characters; never copy input into MIME headers.
        var words = new List<string>();
        var chunk = new StringBuilder();
        var bytes = 0;
        foreach (var rune in subject.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 42)
            {
                words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
                chunk.Clear(); bytes = 0;
            }
            chunk.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (chunk.Length > 0)
            words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
        return string.Join("\r\n ", words);
    }
}
