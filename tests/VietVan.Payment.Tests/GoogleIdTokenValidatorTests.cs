using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public sealed class GoogleIdTokenValidatorTests
{
    private sealed class TestConfiguration(SecurityKey key) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var config = new OpenIdConnectConfiguration(); config.SigningKeys.Add(key);
            return Task.FromResult(config);
        }
        public void RequestRefresh() { }
    }

    [Theory]
    [InlineData("client", "https://accounts.google.com", "true", "nonce", false, true)]
    [InlineData("client", "accounts.google.com", "true", "nonce", false, true)]
    [InlineData("wrong", "https://accounts.google.com", "true", "nonce", false, false)]
    [InlineData("client/", "https://accounts.google.com", "true", "nonce", false, false)]
    [InlineData("client", "https://example.com", "true", "nonce", false, false)]
    [InlineData("client", "https://accounts.google.com", "false", "nonce", false, false)]
    [InlineData("client", "https://accounts.google.com", "true", "other", false, false)]
    [InlineData("client", "https://accounts.google.com", "true", "", false, false)]
    [InlineData("client", "https://accounts.google.com", "true", "nonce", true, false)]
    public async Task Validates_google_token_and_nonce(string audience, string issuer, string verified, string nonce, bool expired, bool valid)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var token = new JwtSecurityToken(issuer, audience,
            new[] { new Claim("sub", "subject"), new Claim("email", "person@example.test"),
                new Claim("email_verified", verified), new Claim("nonce", nonce) },
            expires: DateTime.UtcNow.AddMinutes(expired ? -5 : 5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var value = new JwtSecurityTokenHandler().WriteToken(token);
        var validator = new GoogleIdTokenValidator(new TestConfiguration(key), Options.Create(new GoogleSignInOptions { ClientId = "client" }));
        if (valid) Assert.Equal("subject", (await validator.ValidateAsync(value, "nonce")).Subject);
        else await Assert.ThrowsAnyAsync<SecurityTokenException>(() => validator.ValidateAsync(value, "nonce"));
    }

    [Fact]
    public async Task Rejects_token_signed_by_untrusted_key()
    {
        using var issuer = RSA.Create(2048); using var other = RSA.Create(2048);
        var issuerKey = new RsaSecurityKey(issuer) { KeyId = "one" };
        var token = new JwtSecurityToken("https://accounts.google.com", "client",
            new[] { new Claim("sub", "subject"), new Claim("email", "p@example.test"), new Claim("email_verified", "true"), new Claim("nonce", "nonce") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(issuerKey, SecurityAlgorithms.RsaSha256));
        var validator = new GoogleIdTokenValidator(new TestConfiguration(new RsaSecurityKey(other)), Options.Create(new GoogleSignInOptions { ClientId = "client" }));
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => validator.ValidateAsync(new JwtSecurityTokenHandler().WriteToken(token), "nonce"));
    }

    private sealed class UnavailableConfiguration : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => throw new InvalidOperationException("Metadata should not be fetched.");
        public void RequestRefresh() { }
    }

    [Fact]
    public async Task Rejects_token_without_expiration()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var token = new JwtSecurityToken("https://accounts.google.com", "client",
            [new("sub", "subject"), new("email", "person@example.test"), new("email_verified", "true"), new("nonce", "nonce")],
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var validator = new GoogleIdTokenValidator(new TestConfiguration(key), Options.Create(new GoogleSignInOptions { ClientId = "client" }));
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => validator.ValidateAsync(new JwtSecurityTokenHandler().WriteToken(token), "nonce"));
    }

    [Fact]
    public async Task Client_id_is_rejected_as_token_before_fetching_provider_metadata()
    {
        var validator = new GoogleIdTokenValidator(new UnavailableConfiguration(), Options.Create(new GoogleSignInOptions { ClientId = "test.apps.googleusercontent.com" }));
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => validator.ValidateAsync("test.apps.googleusercontent.com", "nonce"));
    }
}
