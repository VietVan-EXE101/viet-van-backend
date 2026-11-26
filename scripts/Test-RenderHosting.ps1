param([string]$PublishDirectory = (Join-Path $PSScriptRoot '../artifacts/render-publish'))
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'VietVan.API.dll'))) { throw 'Publish the API before running this script.' }
foreach ($privatePath in @('appsettings.Local.json', 'appsettings.Development.json', 'PrivateMedia/drama-assets', 'wwwroot/uploads/work-covers')) {
    if (Test-Path -LiteralPath (Join-Path $publishPath $privatePath)) { throw "Publish output contains local settings/media: $privatePath" }
}
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$randomBytes = New-Object byte[] 64
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $random.GetBytes($randomBytes) } finally { $random.Dispose() }
$temporarySecret = [Convert]::ToBase64String($randomBytes)
# A reserved .invalid domain prevents this smoke test from reaching a real SQL database.
# No endpoint below accesses SQL, sends email, or processes a payment.
$overrides = @{
    ASPNETCORE_ENVIRONMENT = 'Production'; RENDER = 'true'; PORT = [string]$port
    # Smoke hosting không dùng R2 credentials thật hoặc gọi kho media.
    Storage__Provider = 'Local'
    ConnectionStrings__DefaultConnection = "Server=tcp:sql.example.invalid,1433;Database=SmokeOnly;User Id=smoke;Password=$temporarySecret;Encrypt=True;TrustServerCertificate=False;Connection Timeout=1"
    Jwt__Key = $temporarySecret; Swagger__Enabled = 'true'; AllowedHosts = 'localhost;127.0.0.1'
    Cors__AllowedOrigins__0 = 'https://frontend.example.test'
    GoogleSignIn__ClientId = 'render-smoke.apps.googleusercontent.com'
    GoogleSignIn__ChallengeCookieSameSite = 'None'
    Email__Provider = 'Resend'; SePay__WebhookApiKey = $temporarySecret
    DevelopmentAdmin__Enabled = 'false'
}
$previous = @{}
$process = $null
$logDirectory = Join-Path $publishPath '../render-smoke-logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
function Assert-Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-Status([scriptblock]$Action) {
    try { return [int](& $Action).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }; throw }
}
try {
    foreach ($name in $overrides.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $overrides[$name], 'Process')
    }
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList 'VietVan.API.dll' -WorkingDirectory $publishPath -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logDirectory 'stdout.log') -RedirectStandardError (Join-Path $logDirectory 'stderr.log')
    $baseUrl = "http://127.0.0.1:$port"
    $ready = $false
    $lastProbeError = ''
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        if ($process.HasExited) { throw 'Published API stopped before listening. Check artifacts/render-smoke-logs.' }
        try { $health = Invoke-WebRequest -UseBasicParsing "$baseUrl/health" -TimeoutSec 2; $ready = $health.StatusCode -eq 200; if ($ready) { break } } catch { $lastProbeError = $_.Exception.Message }
        Start-Sleep -Milliseconds 200
    }
    Assert-Check $ready ("GET /health did not return 200 on internal HTTP. " + $lastProbeError)
    Assert-Check (($health.Content | ConvertFrom-Json).status -eq 'ok') 'Unexpected health response.'
    Write-Output 'PASS published Production host: PORT and HTTP /health without database access'
    $swagger = Invoke-WebRequest -UseBasicParsing "$baseUrl/swagger/v1/swagger.json" -TimeoutSec 5
    Assert-Check ($swagger.StatusCode -eq 200) 'Swagger is not available.'
    Write-Output 'PASS Swagger in Production'
    $preflight = Invoke-WebRequest -UseBasicParsing "$baseUrl/api/auth/google" -Method Options -TimeoutSec 5 -Headers @{
        'X-Forwarded-Proto' = 'https'; Origin = 'https://frontend.example.test'
        'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'content-type'
    }
    Assert-Check ($preflight.StatusCode -eq 204 -and $preflight.Headers['Access-Control-Allow-Credentials'] -eq 'true' -and
        $preflight.Headers['Access-Control-Allow-Origin'] -eq 'https://frontend.example.test') 'Credentialed CORS preflight failed.'
    Write-Output 'PASS HTTPS proxy and exact-origin credentialed CORS'
    $challenge = Invoke-WebRequest -UseBasicParsing "$baseUrl/api/auth/google/challenge" -Method Post -TimeoutSec 5 -Headers @{ 'X-Forwarded-Proto' = 'https' }
    $cookie = [string]$challenge.Headers['Set-Cookie']
    Assert-Check ($challenge.StatusCode -eq 200 -and $cookie -match '(?i)samesite=none' -and $cookie -match '(?i); secure' -and $cookie -match '(?i)httponly') 'Google cookie is not Secure/HttpOnly/SameSite=None behind HTTPS proxy.'
    $loginStatus = Get-Status { Invoke-WebRequest -UseBasicParsing "$baseUrl/api/auth/google" -Method Post -ContentType 'application/json' `
        -Body '{"idToken":"render-smoke.apps.googleusercontent.com"}' -Headers @{ 'X-Forwarded-Proto' = 'https'; Cookie = $cookie.Split(';')[0] } -TimeoutSec 5 }
    Assert-Check ($loginStatus -eq 401) 'Client ID was accepted as an ID token.'
    Write-Output 'PASS Google HTTPS challenge cookie and invalid-token rejection'
    $webhookStatus = Get-Status { Invoke-WebRequest -UseBasicParsing "$baseUrl/api/payments/webhooks/sepay" -Method Post -ContentType 'application/json' `
        -Body '{}' -Headers @{ 'X-Forwarded-Proto' = 'https'; Authorization = 'Apikey invalid-test-key' } -TimeoutSec 5 }
    Assert-Check ($webhookStatus -eq 401) 'SePay webhook did not reject invalid API key.'
    Write-Output 'PASS SePay API-key verification without payment processing'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
