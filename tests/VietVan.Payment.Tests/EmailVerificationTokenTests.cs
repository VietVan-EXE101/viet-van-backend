using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class EmailVerificationTokenTests
{
    [Fact]
    public void Tokens_are_random_and_hash_is_fixed_length()
    {
        var first = EmailVerificationToken.Create();
        var second = EmailVerificationToken.Create();
        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.Equal(32, EmailVerificationToken.Hash(first).Length);
        Assert.Equal(EmailVerificationToken.Hash(first), EmailVerificationToken.Hash(first));
        Assert.NotEqual(EmailVerificationToken.Hash(first), EmailVerificationToken.Hash(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-token")]
    public void Invalid_tokens_are_rejected(string token) =>
        Assert.Throws<ArgumentException>(() => EmailVerificationToken.Hash(token));
}
