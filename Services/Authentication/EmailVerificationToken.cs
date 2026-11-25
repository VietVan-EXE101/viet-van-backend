using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace VietVan.API.Services.Authentication;

/// <summary>Token ngẫu nhiên; chỉ hash được dùng để lưu database.</summary>
public static class EmailVerificationToken
{
    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Token không hợp lệ.", nameof(token));
        return SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }
}
