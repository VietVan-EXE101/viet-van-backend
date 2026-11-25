using Microsoft.Extensions.Options;
using VietVan.API.Services.Authentication;
using Xunit;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task MissingConfigurationFailsBeforeSending()
    {
        var sender = new SmtpEmailSender(Options.Create(new SmtpEmailOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("test@example.com", "test", "test"));
        Assert.Contains("Email:Smtp:Password", error.Message);
        Assert.Contains("Email:Smtp:Host", error.Message);
    }

    [Fact]
    public void InvalidFromAddressIsReportedByKey()
    {
        var options = new SmtpEmailOptions { Host = "smtp.example.com", Port = 587, UserName = "user", Password = "value", FromAddress = "invalid" };
        Assert.Equal(new[] { "Email:Smtp:FromAddress" }, options.InvalidKeys());
    }
}
