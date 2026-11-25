using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace VietVan.API.Services.Authentication;

public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";

    public IReadOnlyList<string> InvalidKeys()
    {
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(Host)) invalid.Add("Email:Smtp:Host");
        if (Port is < 1 or > 65535) invalid.Add("Email:Smtp:Port");
        if (string.IsNullOrWhiteSpace(UserName)) invalid.Add("Email:Smtp:UserName");
        if (string.IsNullOrWhiteSpace(Password)) invalid.Add("Email:Smtp:Password");
        if (!MailAddress.TryCreate(FromAddress, out _)) invalid.Add("Email:Smtp:FromAddress");
        return invalid;
    }
}

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default);
}

/// <summary>SMTP STARTTLS; lỗi gửi được truyền về caller, không báo thành công giả.</summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> settings) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken = default)
    {
        var config = settings.Value;
        var invalid = config.InvalidKeys();
        if (invalid.Count > 0)
            throw new InvalidOperationException("Cấu hình SMTP thiếu hoặc không hợp lệ: " + string.Join(", ", invalid));
        using var message = new MailMessage { From = new MailAddress(config.FromAddress), Subject = subject,
            Body = html, IsBodyHtml = true, SubjectEncoding = System.Text.Encoding.UTF8, BodyEncoding = System.Text.Encoding.UTF8 };
        message.To.Add(new MailAddress(recipient));
        using var client = new SmtpClient(config.Host, config.Port) { EnableSsl = true,
            UseDefaultCredentials = false, Credentials = new NetworkCredential(config.UserName, config.Password) };
        await client.SendMailAsync(message, cancellationToken);
    }
}
