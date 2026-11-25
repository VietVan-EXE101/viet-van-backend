namespace VietVan.API.Services.Payments;

public class SePayOptions
{
    public string BankId { get; set; } = "";
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountHolder { get; set; } = "";
    public string WebhookApiKey { get; set; } = "";
    public int OrderLifetimeMinutes { get; set; } = 15;
}
