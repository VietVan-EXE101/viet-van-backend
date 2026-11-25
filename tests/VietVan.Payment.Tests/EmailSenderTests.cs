using Microsoft.Extensions.Options;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class EmailSenderTests
{
    [Fact]
    public async Task Missing_smtp_configuration_fails_without_claiming_delivery()
    {
        var sender = new SmtpEmailSender(Options.Create(new SmtpEmailOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("user@example.test", "Subject", "Body"));
    }
}
