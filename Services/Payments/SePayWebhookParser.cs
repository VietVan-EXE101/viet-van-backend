using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace VietVan.API.Services.Payments;

public sealed record SePayReceipt(string TransactionId, decimal Amount, string AccountNumber,
    string TransferType, string Content, string? Code, string Currency, string BodyHash);

public static class SePayWebhookParser
{
    private static readonly Regex OrderCodePattern = new(@"(?<![A-Z0-9])VV[A-F0-9]{16}(?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static SePayReceipt Parse(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Payload phải là JSON object.");
        static string S(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return "";
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
        }
        if (!root.TryGetProperty("transferAmount", out var amountElement) ||
            !decimal.TryParse(amountElement.ToString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            throw new FormatException("transferAmount không hợp lệ.");
        var id = S(root, "id").Trim();
        if (id.Length is < 1 or > 100 || !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$"))
            throw new FormatException("Mã giao dịch SePay không hợp lệ.");
        var content = S(root, "content");
        if (content.Length > 2000) throw new FormatException("content quá dài.");
        var currency = S(root, "currency").Trim();
        if (currency.Length == 0) currency = "VND";
        return new SePayReceipt(id, amount, S(root, "accountNumber").Trim(),
            S(root, "transferType").Trim(), content, S(root, "code").Trim(), currency.ToUpperInvariant(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
    }
    public static string? ExtractOrderCode(SePayReceipt receipt)
    {
        var contentMatches = OrderCodePattern.Matches(receipt.Content.ToUpperInvariant())
            .Select(x => x.Value.ToUpperInvariant()).Distinct().ToList();
        var codeMatches = OrderCodePattern.Matches((receipt.Code ?? "").ToUpperInvariant())
            .Select(x => x.Value.ToUpperInvariant()).Distinct().ToList();
        // Có mã xung đột hoặc nhiều mã trong một chuyển khoản => không đoán đơn hàng.
        var combined = contentMatches.Union(codeMatches).Distinct().ToList();
        if (combined.Count != 1) return null;
        if (!string.IsNullOrWhiteSpace(receipt.Code) && codeMatches.Count == 0) return null;
        return combined[0];
    }
}
