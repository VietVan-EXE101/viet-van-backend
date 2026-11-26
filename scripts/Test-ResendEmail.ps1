[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Recipient,
    [string]$FromAddress = 'onboarding@resend.dev',
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

function Get-SafeResendReason([int]$Status, [string]$Body) {
    $message = ''
    try { $message = [string](ConvertFrom-Json -InputObject $Body).message } catch { }
    # Never echo the response body, addresses, key or request payload.
    if ($message -match '(?i)only send testing emails') { return 'TEST_RECIPIENT_NOT_ACCOUNT_OWNER: use the exact email associated with this Resend account.' }
    if ($message -match '(?i)domain.{0,160}(not verified|verify)|verify.{0,160}domain') { return 'SENDER_DOMAIN_NOT_VERIFIED: verify a sender domain before emailing other users.' }
    if ($message -match '(?i)api.?key|access token') { return 'API_KEY_REJECTED: check the complete key, its sending permission and any domain restriction.' }
    if ($Status -eq 401) { return 'AUTHENTICATION_FAILED: check the complete Resend key.' }
    if ($Status -eq 403) { return 'FORBIDDEN: check sender domain, test-recipient restriction and key permission in Resend Logs.' }
    if ($Status -eq 429) { return 'RATE_LIMIT_OR_QUOTA: inspect usage and retry later.' }
    if ($Status -ge 500) { return 'PROVIDER_UNAVAILABLE: inspect Resend status and retry later.' }
    return 'REQUEST_REJECTED: inspect the corresponding POST /emails entry in Resend Logs; do not share the key.'
}

try {
    $recipientAddress = [System.Net.Mail.MailAddress]::new($Recipient)
    $senderAddress = [System.Net.Mail.MailAddress]::new($FromAddress)
    if ($Recipient -match '[\r\n]' -or $FromAddress -match '[\r\n]') { throw 'Invalid address.' }
} catch {
    Write-Output 'FAIL: Recipient and FromAddress must be valid email addresses.'
    exit 1
}

Write-Output 'Resend transport test only. No database/R2 access, password change or account verification change.'
if ($senderAddress.Host -eq 'resend.dev') {
    Write-Output 'TEST SENDER: Recipient must be the exact email associated with the Resend account that owns the key.'
}
if ($ValidateOnly) {
    Write-Output 'PASS argument validation. No key requested and no email sent.'
    exit 0
}

# Interactive secret entry prevents the key from entering command history or files.
$secureKey = Read-Host 'Paste the full Resend API key (hidden input; never paste it into chat)' -AsSecureString
$keyPointer = [IntPtr]::Zero
$apiKey = $null
$request = $null
$response = $null
$http = $null
$handler = $null
$previousTls = [System.Net.ServicePointManager]::SecurityProtocol
$resultCode = 1
try {
    $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($apiKey) -or -not $apiKey.StartsWith('re_') -or
        $apiKey -match '\s' -or $apiKey.Contains('...')) {
        Write-Output 'FAIL: enter the complete key starting with re_, without quotes, spaces or ellipsis.'
    } else {
        Add-Type -AssemblyName System.Net.Http
        [System.Net.ServicePointManager]::SecurityProtocol = $previousTls -bor [System.Net.SecurityProtocolType]::Tls12
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
        $http = [System.Net.Http.HttpClient]::new($handler)
        $http.Timeout = [TimeSpan]::FromSeconds(30)
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, 'https://api.resend.com/emails')
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $apiKey)
        $payload = @{
            from = $senderAddress.Address
            to = @($recipientAddress.Address)
            subject = 'Viet Van - Resend delivery test'
            html = '<p>This is a Viet Van email transport test. It does not change your password or account verification.</p>'
        } | ConvertTo-Json -Depth 3 -Compress
        $request.Content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($response.IsSuccessStatusCode) {
            $accepted = $null
            try { $accepted = ConvertFrom-Json -InputObject $body } catch { }
            if ([string]::IsNullOrWhiteSpace([string]$accepted.id)) {
                Write-Output "FAIL HTTP=${status}: provider did not confirm acceptance. Check Resend Logs."
            } else {
                Write-Output "PASS HTTP=${status}: Resend accepted the test email. Check Resend Emails, Inbox and Spam."
                Write-Output 'Acceptance is not proof of inbox delivery. If this test arrives but Render mail does not, compare Render configuration and Backend account/cooldown logs.'
                $resultCode = 0
            }
        } else {
            Write-Output ('FAIL HTTP=' + $status + ' ' + (Get-SafeResendReason -Status $status -Body $body))
        }
    }
} catch {
    # Exception messages may contain request data. Print only the type.
    Write-Output ('FAIL NETWORK_OR_CLIENT_ERROR Type=' + $_.Exception.GetType().Name + '; no secret or provider body printed.')
} finally {
    if ($null -ne $response) { $response.Dispose() }
    if ($null -ne $request) { $request.Dispose() }
    if ($null -ne $http) { $http.Dispose() } elseif ($null -ne $handler) { $handler.Dispose() }
    if ($keyPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer) }
    $apiKey = $null
    if ($null -ne $secureKey) { $secureKey.Dispose() }
    [System.Net.ServicePointManager]::SecurityProtocol = $previousTls
}
exit $resultCode
