using System.Text.RegularExpressions;

namespace VietVan.API.Services.Storage;

public sealed class R2Options
{
    public string AccountId { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string ServiceUrl { get; set; } = "";
    public int PublicUrlMinutes { get; set; } = 60;
    public int ProtectedUrlMinutes { get; set; } = 5;

    public IReadOnlyList<string> InvalidKeys()
    {
        var errors = new List<string>();
        if (PublicUrlMinutes is < 1 or > 120) errors.Add("R2:PublicUrlMinutes");
        if (ProtectedUrlMinutes is < 1 or > 15) errors.Add("R2:ProtectedUrlMinutes");
        if (!Regex.IsMatch(AccountId ?? "", "^[a-fA-F0-9]{32}$")) errors.Add("R2:AccountId");
        if (!Regex.IsMatch(BucketName ?? "", "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")) errors.Add("R2:BucketName");
        if (!Regex.IsMatch(AccessKeyId ?? "", "^[a-zA-Z0-9]{16,128}$") ||
            (AccessKeyId?.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ?? false)) errors.Add("R2:AccessKeyId");
        if (!Regex.IsMatch(SecretAccessKey ?? "", "^[a-zA-Z0-9/+=]{32,128}$") ||
            (SecretAccessKey?.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ?? false)) errors.Add("R2:SecretAccessKey");
        if (!string.IsNullOrWhiteSpace(ServiceUrl) && !ValidEndpoint(out _)) errors.Add("R2:ServiceUrl");
        return errors;
    }

    public Uri Endpoint()
    {
        var errors = InvalidKeys();
        if (errors.Count > 0)
            throw new InvalidOperationException("Thiếu/sai cấu hình: " + string.Join(", ", errors));
        if (!ValidEndpoint(out var endpoint)) throw new InvalidOperationException("R2:ServiceUrl không hợp lệ.");
        return endpoint!;
    }

    private bool ValidEndpoint(out Uri? endpoint)
    {
        var value = string.IsNullOrWhiteSpace(ServiceUrl) ? $"https://{AccountId}.r2.cloudflarestorage.com" : ServiceUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out endpoint) || endpoint.Scheme != "https" ||
            !endpoint.IsDefaultPort || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 ||
            endpoint.Fragment.Length > 0 || endpoint.AbsolutePath != "/" ||
            !(endpoint.Host.Equals($"{AccountId}.r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
              endpoint.Host.Equals($"{AccountId}.eu.r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    internal static string Key(string value)
    {
        var key = value.Replace('\\', '/');
        if (!(key.StartsWith("drama-assets/", StringComparison.Ordinal) ||
              key.StartsWith("free/", StringComparison.Ordinal) || key.StartsWith("pro/", StringComparison.Ordinal) ||
              key.StartsWith("users/avatars/", StringComparison.Ordinal)) ||
            key.Split('/').Any(part => part is "" or "." or "..") ||
            key.Any(c => char.IsControl(c) || c == ':'))
            throw new ArgumentException("Storage key không hợp lệ.");
        return key;
    }
}

public sealed class MediaStorageUnavailableException : IOException
{
    public int? HttpStatus { get; }
    public string FailureCode { get; }
    public MediaStorageUnavailableException(int? httpStatus = null, string failureCode = "STORAGE_UNAVAILABLE")
        : base("Không truy cập được kho media. Kiểm tra cấu hình R2 và quyền bucket.")
    { HttpStatus = httpStatus; FailureCode = failureCode; }
}
