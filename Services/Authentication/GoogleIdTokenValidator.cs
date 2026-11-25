using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace VietVan.API.Services.Authentication;

public sealed class GoogleSignInOptions
{
    public string ClientId { get; set; } = "";
}

public sealed record GoogleIdentity(string Subject, string Email);

public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken = default);
}

/// <summary>Kiểm tra ID token Google; caller phải cấp và tiêu thụ nonce một lần ở server.</summary>
public sealed class GoogleIdTokenValidator(
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    IOptions<GoogleSignInOptions> options) : IGoogleIdTokenValidator
{
    public async Task<GoogleIdentity> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken = default)
    {
        var clientId = options.Value.ClientId;
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Google Sign-In chưa được cấu hình.");
        if (string.IsNullOrWhiteSpace(expectedNonce) || expectedNonce.Length > 256 ||
            string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16384)
            throw new SecurityTokenValidationException("Thông tin đăng nhập Google không hợp lệ.");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(idToken))
            throw new SecurityTokenValidationException("Thông tin đăng nhập Google không hợp lệ.");
        var config = await configurationManager.GetConfigurationAsync(cancellationToken);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuers = new[] { "accounts.google.com", "https://accounts.google.com" },
            ValidateAudience = true, ValidAudience = clientId, IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateIssuerSigningKey = true, IssuerSigningKeys = config.SigningKeys,
            RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }, ClockSkew = TimeSpan.FromSeconds(30)
        };
        var principal = handler.ValidateToken(idToken, parameters, out _);
        var subject = principal.FindFirst("sub")?.Value;
        var email = principal.FindFirst("email")?.Value;
        var verified = principal.FindFirst("email_verified")?.Value;
        var nonce = principal.FindFirst("nonce")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 150 ||
            !string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(nonce) || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(nonce)), SHA256.HashData(Encoding.UTF8.GetBytes(expectedNonce))))
            throw new SecurityTokenValidationException("Thông tin đăng nhập Google không hợp lệ.");
        return new GoogleIdentity(subject, email);
    }
}
