using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace VietVan.API.Services.Authentication;

// Bộ nhớ tiến trình: khi chạy nhiều instance cần sticky sessions hoặc kho dùng chung.
public sealed class GoogleChallengeStore(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (string Nonce, DateTime Expires)> entries = new();
    public (string Key, string Nonce) Create()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var pair in entries.Where(x => x.Value.Expires <= now)) entries.TryRemove(pair.Key, out _);
        if (entries.Count >= 10000) throw new InvalidOperationException("Quá nhiều yêu cầu đăng nhập.");
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        entries[key] = (nonce, now.AddMinutes(5));
        return (key, nonce);
    }
    public string? Consume(string key) => entries.TryRemove(key, out var item) && item.Expires > clock.GetUtcNow().UtcDateTime ? item.Nonce : null;
}
