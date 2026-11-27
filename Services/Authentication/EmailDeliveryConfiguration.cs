using VietVan.API.Hosting;

namespace VietVan.API.Services.Authentication;

// Shared by startup, verification and the read-only operator check. Never reports values.
public static class EmailDeliveryConfiguration
{
    public static string Provider(IConfiguration configuration)
    {
        var value = configuration["Email:Provider"] ?? (DeploymentConfiguration.IsRender(configuration) ? "Gmail" : "Smtp");
        if (value.Equals("Gmail", StringComparison.OrdinalIgnoreCase)) return "Gmail";
        if (value.Equals("Resend", StringComparison.OrdinalIgnoreCase)) return "Resend";
        if (value.Equals("Smtp", StringComparison.OrdinalIgnoreCase)) return "Smtp";
        throw new InvalidOperationException("Email__Provider chỉ hỗ trợ Gmail, Smtp hoặc Resend.");
    }

    public static Uri VerificationBaseUri(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Email:VerificationBaseUrl"];
        if (string.IsNullOrWhiteSpace(configured) && environment.IsDevelopment()) configured = "http://localhost:5080";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Cần cấu hình Email:VerificationBaseUrl an toàn.");
        return uri;
    }

    public static IReadOnlyList<string> InvalidKeys(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var invalid = new List<string>();
        try
        {
            if (Provider(configuration) == "Gmail")
                invalid.AddRange((configuration.GetSection("Email:Gmail").Get<GmailEmailOptions>() ?? new()).InvalidKeys());
            else if (Provider(configuration) == "Resend")
                invalid.AddRange((configuration.GetSection("Email:Resend").Get<ResendEmailOptions>() ?? new()).InvalidKeys());
            else invalid.AddRange((configuration.GetSection("Email:Smtp").Get<SmtpEmailOptions>() ?? new()).InvalidKeys());
        }
        // A conversion error (e.g. SMTP port) must not echo the configured value.
        catch (InvalidOperationException) { invalid.Add("Email:Provider/transport settings"); }
        try { _ = VerificationBaseUri(configuration, environment); }
        catch (InvalidOperationException) { invalid.Add("Email:VerificationBaseUrl"); }
        return invalid;
    }

    public static void LogStatus(IConfiguration configuration, IWebHostEnvironment environment, ILogger logger)
    {
        var provider = Provider(configuration);
        var invalid = InvalidKeys(configuration, environment);
        logger.LogInformation("Email delivery configured Provider={EmailProvider} ConfigurationReady={ConfigurationReady} InvalidKeys={InvalidKeys} Render={Render}; configuration check only, not delivery confirmation.",
            provider, invalid.Count == 0, invalid.Count == 0 ? "none" : string.Join(",", invalid), DeploymentConfiguration.IsRender(configuration));
        if (UsesResendTestSender(configuration))
            logger.LogWarning("Email delivery limited Provider=Resend Code=TEST_SENDER_RESTRICTION; verify a sender domain to email other recipients.");
    }

    private static bool UsesResendTestSender(IConfiguration configuration) => Provider(configuration) == "Resend" &&
        System.Net.Mail.MailAddress.TryCreate(configuration["Email:Resend:FromAddress"], out var from) &&
        from.Host.Equals("resend.dev", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> CheckAsync(IConfiguration configuration, IWebHostEnvironment environment, TextWriter output)
    {
        await output.WriteLineAsync("READ-ONLY email configuration: no email sent, no database, migration, seed or media access.");
        string provider;
        try { provider = Provider(configuration); }
        catch (InvalidOperationException)
        { await output.WriteLineAsync("FAIL Email:Provider (Gmail, Smtp or Resend required; value hidden)"); return 1; }
        var invalid = InvalidKeys(configuration, environment);
        await output.WriteLineAsync($"Provider={provider}; Render={DeploymentConfiguration.IsRender(configuration)}; ConfigurationReady={invalid.Count == 0}");
        foreach (var key in invalid) await output.WriteLineAsync("FAIL " + key);
        if (provider == "Smtp" && DeploymentConfiguration.IsRender(configuration))
            await output.WriteLineAsync("WARNING Render Free blocks SMTP ports 25/465/587; select Email__Provider=Gmail or Resend and configure HTTPS delivery.");
        if (UsesResendTestSender(configuration))
            await output.WriteLineAsync("WARNING TEST_SENDER_RESTRICTION: Resend test sender only emails the account owner; verify a domain for other recipients.");
        await output.WriteLineAsync("No secrets, addresses or URLs printed. Valid configuration does not prove provider acceptance or inbox delivery.");
        return invalid.Count == 0 ? 0 : 1;
    }
}
