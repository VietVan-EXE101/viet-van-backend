using System.Globalization;
using Microsoft.Extensions.Options;
using VietVan.API.Entities;
namespace VietVan.API.Services.Payments;

public interface IPaymentGateway
{
    string Name { get; }
    string BuildQrUrl(PaymentOrder order);
    string BankName { get; }
    string AccountNumber { get; }
    string AccountHolder { get; }
}

public sealed class VietQrGateway(IOptions<SePayOptions> options) : IPaymentGateway
{
    private readonly SePayOptions config = options.Value;
    public string Name => "SePay";
    public string BankName => config.BankName;
    public string AccountNumber => config.AccountNumber;
    public string AccountHolder => config.AccountHolder;

    public string BuildQrUrl(PaymentOrder order)
    {
        if (string.IsNullOrWhiteSpace(config.BankId) || string.IsNullOrWhiteSpace(config.AccountNumber) ||
            string.IsNullOrWhiteSpace(config.AccountHolder) ||
            !System.Text.RegularExpressions.Regex.IsMatch(config.BankId, "^[A-Za-z0-9]{2,30}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(config.AccountNumber, "^[0-9]{5,25}$"))
            throw new InvalidOperationException("Chưa cấu hình thông tin tài khoản ngân hàng hợp lệ.");
        // VietQR chỉ là lệnh chuyển khoản; thanh toán thành công chỉ đến từ webhook đã xác thực.
        return $"https://img.vietqr.io/image/{config.BankId}-{config.AccountNumber}-compact2.png" +
            $"?amount={order.Amount.ToString("0", CultureInfo.InvariantCulture)}" +
            $"&addInfo={Uri.EscapeDataString(order.OrderCode)}" +
            $"&accountName={Uri.EscapeDataString(config.AccountHolder)}";
    }
}
