using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using VietVan.API.Data;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Interfaces;
using VietVan.API.Settings;
using VietVan.API.Services.Payments;
using VietVan.API.Services.Authentication;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Threading.RateLimiting;
using VietVan.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using VietVan.API.Middlewares;
using VietVan.API.Hosting;

var setupAdmin = args.Contains("--setup-admin", StringComparer.OrdinalIgnoreCase);
var checkDatabaseReadOnly = args.Contains("--check-db-readonly", StringComparer.OrdinalIgnoreCase);
var checkPaymentDatabaseReadOnly = args.Contains("--check-payment-db-readonly", StringComparer.OrdinalIgnoreCase);
var checkEmailConfiguration = args.Contains("--check-email-config", StringComparer.OrdinalIgnoreCase);
var migrateMedia = args.Contains("--migrate-media-to-r2", StringComparer.OrdinalIgnoreCase);
var testR2 = args.Contains("--test-r2", StringComparer.OrdinalIgnoreCase);
var auditMediaStorage = args.Contains("--audit-media-storage-readonly", StringComparer.OrdinalIgnoreCase);
var planMediaRecovery = args.Contains("--plan-missing-media-recovery", StringComparer.OrdinalIgnoreCase);
var inspectMedia = args.FirstOrDefault(arg => arg.StartsWith("--inspect-drama-media=", StringComparison.OrdinalIgnoreCase));
var restoreMedia = args.FirstOrDefault(arg => arg.StartsWith("--restore-missing-r2-media=", StringComparison.OrdinalIgnoreCase));
var applyMediaMigration = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var testEmailIndex = Array.FindIndex(args, x => x.Equals("--test-email", StringComparison.OrdinalIgnoreCase));
var testEmail = testEmailIndex >= 0;
var testRecipient = testEmail && testEmailIndex + 1 < args.Length ? args[testEmailIndex + 1] : null;
var builder = WebApplication.CreateBuilder(args
    .Where((arg, index) => !arg.Equals("--setup-admin", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--check-db-readonly", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--check-payment-db-readonly", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--check-email-config", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--migrate-media-to-r2", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--test-r2", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--audit-media-storage-readonly", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--plan-missing-media-recovery", StringComparison.OrdinalIgnoreCase)
        && !arg.StartsWith("--inspect-drama-media=", StringComparison.OrdinalIgnoreCase)
        && !arg.StartsWith("--restore-missing-r2-media=", StringComparison.OrdinalIgnoreCase)
        && !arg.Equals("--apply", StringComparison.OrdinalIgnoreCase)
        && index != testEmailIndex && (!testEmail || index != testEmailIndex + 1))
    .ToArray());
var renderDeployment = DeploymentConfiguration.IsRender(builder.Configuration);
if (renderDeployment) builder.Logging.ClearProviders().AddConsole();
var listenUrl = DeploymentConfiguration.ListenUrl(builder.Configuration);
if (listenUrl is not null) builder.WebHost.UseUrls(listenUrl);

if (checkEmailConfiguration)
{
    Environment.ExitCode = await EmailDeliveryConfiguration.CheckAsync(builder.Configuration, builder.Environment, Console.Out);
    return;
}

// Chỉ lệnh cục bộ được gọi tường minh mới thử gửi thư; không gửi khi khởi động API.
if (testEmail)
{
    if (!builder.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("Lệnh thử email chỉ được phép trong Development.");
        Environment.ExitCode = 1;
        return;
    }
    if (!System.Net.Mail.MailAddress.TryCreate(testRecipient, out _))
    {
        Console.Error.WriteLine("Cần một địa chỉ nhận hợp lệ sau --test-email.");
        Environment.ExitCode = 1;
        return;
    }
    try
    {
        var provider = EmailDeliveryConfiguration.Provider(builder.Configuration);
        var smtp = builder.Configuration.GetSection("Email:Smtp").Get<SmtpEmailOptions>() ?? new();
        var resend = builder.Configuration.GetSection("Email:Resend").Get<ResendEmailOptions>() ?? new();
        var invalid = provider == "Resend" ? resend.InvalidKeys() : smtp.InvalidKeys();
        if (invalid.Count > 0)
        {
            Console.Error.WriteLine("Thiếu hoặc sai cấu hình: " + string.Join(", ", invalid));
            Environment.ExitCode = 1;
            return;
        }
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        IEmailSender sender = provider == "Resend"
            ? new ResendEmailSender(http, Microsoft.Extensions.Options.Options.Create(resend))
            : new SmtpEmailSender(Microsoft.Extensions.Options.Options.Create(smtp));
        await sender.SendAsync(testRecipient!, "Kiểm tra email Việt Văn", "<p>Đây là email kiểm tra cấu hình gửi thư Việt Văn.</p>");
        Console.WriteLine($"Provider={provider} đã chấp nhận email thử; hãy kiểm tra hộp thư và thư rác. Không sửa database/media.");
    }
    catch (Exception ex)
    {
        var status = ex is System.Net.Mail.SmtpException smtpException ? $", mã SMTP {smtpException.StatusCode}" : "";
        if (ex is HttpRequestException http && http.StatusCode.HasValue) status = $", HTTP {(int)http.StatusCode.Value}";
        Console.Error.WriteLine($"Không gửi được email: {ex.GetType().Name}{status}.");
        Environment.ExitCode = 1;
    }
    return;
}

if (planMediaRecovery)
{
    Environment.ExitCode = await MediaRecoveryPlanner.RunAsync(builder.Configuration, builder.Environment, Console.Out);
    return;
}
if (auditMediaStorage)
{
    Environment.ExitCode = await MediaStorageAudit.RunAsync(builder.Configuration, builder.Environment, Console.Out);
    return;
}
if (inspectMedia is not null)
{
    Environment.ExitCode = await DramaMediaInspection.RunAsync(builder.Configuration, builder.Environment,
        inspectMedia["--inspect-drama-media=".Length..], Console.Out);
    return;
}
if (restoreMedia is not null)
{
    Environment.ExitCode = await DramaMediaRecovery.RunAsync(builder.Configuration, builder.Environment,
        restoreMedia["--restore-missing-r2-media=".Length..], applyMediaMigration, Console.Out);
    return;
}
if (testR2)
{
    Environment.ExitCode = await R2SmokeCheck.RunAsync(builder.Configuration, builder.Environment, Console.Out);
    return;
}
if (migrateMedia)
{
    Environment.ExitCode = await MediaMigration.RunAsync(builder.Configuration, builder.Environment, applyMediaMigration, Console.Out);
    return;
}
if (checkDatabaseReadOnly || checkPaymentDatabaseReadOnly)
{
    Environment.ExitCode = await DatabaseReadOnlyCheck.RunAsync(builder.Configuration, builder.Environment, Console.Out,
        paymentsOnly: checkPaymentDatabaseReadOnly);
    return;
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
var sqlConnection = DeploymentConfiguration.SqlConnection(builder.Configuration, builder.Environment);
builder.Services.AddDbContext<VietVanDbContext>(options =>
    options.UseSqlServer(sqlConnection)
        .AddInterceptors(SqlConnectionOpeningRetryInterceptor.Instance)
        .EnableSensitiveDataLogging(false));
if (renderDeployment)
{
    builder.Services.Configure<ForwardedHeadersOptions>(DeploymentConfiguration.ConfigureRenderProxy);
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
}
builder.Services.AddScoped<WorkDeletionService>();
builder.Services.Configure<SePayOptions>(builder.Configuration.GetSection("SePay"));
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddHostedService<PaymentExpiryWorker>();
builder.Services.AddScoped<IPaymentGateway,VietQrGateway>();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddOptions<GoogleSignInOptions>().Bind(builder.Configuration.GetSection("GoogleSignIn"))
    .Validate(options => options.ChallengeCookieSameSite is SameSiteMode.Strict or SameSiteMode.Lax or SameSiteMode.None,
        "GoogleSignIn__ChallengeCookieSameSite phải là Strict, Lax hoặc None.").ValidateOnStart();
builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(_ =>
    new ConfigurationManager<OpenIdConnectConfiguration>("https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true }));
builder.Services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
builder.Services.Configure<SmtpEmailOptions>(builder.Configuration.GetSection("Email:Smtp"));
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection("Email:Resend"));
var emailProvider = EmailDeliveryConfiguration.Provider(builder.Configuration);
if (emailProvider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddScoped<IEmailSender>(services => services.GetRequiredService<ResendEmailSender>());
}
else if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase)) builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else throw new InvalidOperationException("Email__Provider chỉ hỗ trợ Smtp hoặc Resend.");
builder.Services.AddScoped<AccountVerificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountSessionService>();
builder.Services.AddScoped<GoogleAccountService>();
builder.Services.AddScoped<DramaPlatformService>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddScoped<AdminPlatformService>();
builder.Services.AddMediaStorage(builder.Configuration, builder.Environment);
builder.Services.AddScoped<IAuthorizationHandler, ProAuthorizationHandler>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => {
    // Exact origins (without paths); Development adds localhost:3001 in its JSON config.
    // Credentials are needed for the HttpOnly Google challenge cookie on both POSTs.
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
}));
builder.Services.AddSingleton<GoogleChallengeStore>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("payment-sensitive", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth-sensitive", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("drama-upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("review-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32 ||
    jwt.Key.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ||
    jwt.Key.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Thiếu JWT secret an toàn. Cấu hình Jwt__Key bằng biến môi trường/secret manager.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents { OnTokenValidated = LiveAccountValidator.ValidateAsync };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options => options.AddPolicy("Pro", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProRequirement())));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IWorkService, WorkService>();
builder.Services.AddScoped<WorkCoverStorage>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
// Quản lý kịch tương tác: không thay đổi các service hiện có.
builder.Services.AddScoped<DramaService>();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<WorkCreateSwaggerFilter>();
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Viet Van API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();
// Email configuration failures are reported without taking media/API storage offline.
EmailDeliveryConfiguration.LogStatus(builder.Configuration, builder.Environment, app.Logger);

// An explicit local command, never executed by normal API startup or an HTTP endpoint.
if (setupAdmin)
{
    Console.Write($"Nhập mật khẩu mới cho {AdminAccountSetup.Email}: ");
    var password = Console.ReadLine() ?? "";
    try
    {
        await AdminAccountSetup.ConfigureAsync(app.Services, password);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Không thể thiết lập Admin: {exception.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        await app.DisposeAsync();
    }
    return;
}

// Provision only the explicitly configured development account in the existing schema.
if (app.Environment.IsDevelopment())
{
    if (builder.Configuration.GetValue<bool>("DevelopmentAdmin:Enabled") && DeploymentConfiguration.IsLocalSqlServer(sqlConnection))
    {
        var adminPassword = builder.Configuration["DevelopmentAdmin:Password"]
            ?? throw new InvalidOperationException("Thiếu cấu hình DevelopmentAdmin:Password.");
        await AdminAccountSetup.ConfigureAsync(app.Services, adminPassword, resetPassword: false);
    }

}

if (renderDeployment)
{
    app.UseForwardedHeaders();
    if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        app.Logger.LogWarning("Render Free chặn SMTP 25/465/587. Dùng Email__Provider=Resend để gửi qua HTTPS.");
}
var activeStorage = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
var activeBucket = activeStorage.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase)
    ? app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<VietVan.API.Services.Storage.R2Options>>().Value.BucketName : "none";
app.Logger.LogInformation("Media storage configured Provider={StorageProvider} BucketName={BucketName} Environment={Environment} Render={Render}; no local fallback for R2.",
    activeStorage.Provider, activeBucket, app.Environment.EnvironmentName, renderDeployment);
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseMiddleware<ApiExceptionMiddleware>();
// Render probes the internal HTTP port. Health must not redirect to external HTTPS.
app.UseWhen(context => !context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase), branch => branch.UseHttpsRedirection());
// Chỉ static HTML/CSS/JS công khai. Media được đưa ra ngoài wwwroot để kiểm tra VIP.
app.Use(async (ctx, next) =>
{
    // Chặn truy cập đường dẫn static cũ, tránh bypass kiểm tra VIP bằng URL trực tiếp.
    if (ctx.Request.Path.StartsWithSegments("/drama-assets"))
    { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    await next();
});
app.UseStaticFiles();
app.UseRouting();
// CORS must handle preflight before authentication/authorization and rate limiting.
app.UseCors();
app.UseAuthentication();
// Token bị thu hồi không được tiếp tục đi vào action AllowAnonymous với danh tính cũ.
app.Use(async (context, next) => {
    if (context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
        !(await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)).Succeeded)
    { context.Response.StatusCode = 401; return; }
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();
