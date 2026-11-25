# Windows PowerShell 5.1; chỉ lưu giá trị được nhập vào User Secrets.
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'VietVan.API.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Không tìm thấy VietVan.API.csproj.' }
[xml]$xml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$id = @($xml.Project.PropertyGroup.UserSecretsId | Where-Object { $_ }) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($id)) { throw 'Project chưa có UserSecretsId.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Không tìm thấy dotnet.' }

function Set-SecretValue([string]$key, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    if ($value -match '(?i)(PASTE_|CHANGE_ME|REPLACE|YOUR_SECRET|YOUR_KEY)' -or $value -match '[\r\n]') {
        Write-Warning "Giá trị cho $key không hợp lệ hoặc là placeholder; giữ cấu hình cũ."
        return $false
    }
    # dotnet user-secrets nhận JSON trên stdin; không đưa secret vào process arguments.
    $payload = @{ $key = $value } | ConvertTo-Json -Compress
    $payload | & dotnet user-secrets set --project $project 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Không lưu được $key vào User Secrets." }
    return $true
}
function Read-SecretValue([string]$prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $ptr = [IntPtr]::Zero
    try { $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure); return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }; $secure.Dispose() }
}
Write-Host 'Để trống từng mục để giữ nguyên cấu hình hiện có.'
$google = Read-Host 'Google Client ID (chỉ giá trị công khai)'
if ($google -and $google -notmatch '^[A-Za-z0-9_-]+\.apps\.googleusercontent\.com$') { Write-Warning 'Client ID không đúng định dạng thông thường; không lưu.' }
elseif (Set-SecretValue 'GoogleSignIn:ClientId' $google) { Write-Host 'Đã lưu Google Client ID.' }
$gmail = Read-Host 'Địa chỉ Gmail gửi thư'
if ($gmail) {
    try { $address = New-Object System.Net.Mail.MailAddress($gmail); if ($address.Address -ne $gmail) { throw 'Invalid' } }
    catch { throw 'Địa chỉ Gmail không hợp lệ.' }
    [void](Set-SecretValue 'Email:Smtp:Host' 'smtp.gmail.com')
    [void](Set-SecretValue 'Email:Smtp:Port' '587')
    [void](Set-SecretValue 'Email:Smtp:UserName' $gmail)
    [void](Set-SecretValue 'Email:Smtp:FromAddress' $gmail)
    Write-Host 'Đã lưu cấu hình địa chỉ Gmail.'
}
$smtpPassword = Read-SecretValue 'Gmail App Password (ẩn)'
try { if (Set-SecretValue 'Email:Smtp:Password' $smtpPassword) { Write-Host 'Đã lưu Gmail App Password.' } } finally { $smtpPassword = $null }
$resetPage = Read-Host 'URL trang FE đặt lại mật khẩu (tùy chọn; chưa có URL thì email chứa mã reset)'
if ($resetPage) {
    $resetPageUri = $null
    if (-not [Uri]::TryCreate($resetPage, [UriKind]::Absolute, [ref]$resetPageUri) -or
        $resetPageUri.UserInfo -or $resetPageUri.Fragment -or
        ($resetPageUri.Scheme -ne 'https' -and -not ($resetPageUri.Scheme -eq 'http' -and $resetPageUri.IsLoopback))) {
        Write-Warning 'URL reset cần HTTPS, hoặc HTTP localhost khi Development; không chứa userinfo/fragment. Giữ cấu hình cũ.'
    }
    elseif (Set-SecretValue 'Email:PasswordResetUrl' $resetPage) { Write-Host 'Đã lưu URL trang reset mật khẩu.' }
}
$bankId = Read-Host 'BankId VietQR'
if ($bankId -and $bankId -notmatch '^[A-Za-z0-9]{2,30}$') { Write-Warning 'BankId không hợp lệ; không lưu.' }
elseif (Set-SecretValue 'SePay:BankId' $bankId) { Write-Host 'Đã lưu BankId.' }
$bankName = Read-Host 'Tên ngân hàng'
if (Set-SecretValue 'SePay:BankName' $bankName) { Write-Host 'Đã lưu tên ngân hàng.' }
$account = Read-Host 'Số tài khoản ngân hàng'
if ($account -and $account -notmatch '^[0-9]{5,25}$') { Write-Warning 'Số tài khoản không hợp lệ; không lưu.' }
elseif (Set-SecretValue 'SePay:AccountNumber' $account) { Write-Host 'Đã lưu số tài khoản.' }
$holder = Read-Host 'Tên chủ tài khoản'
if (Set-SecretValue 'SePay:AccountHolder' $holder) { Write-Host 'Đã lưu chủ tài khoản.' }
$webhookKey = Read-SecretValue 'SePay Webhook API Key (ẩn, ít nhất 24 ký tự)'
try {
    if ($webhookKey -and $webhookKey.Length -lt 24) { Write-Warning 'Webhook key quá ngắn; không lưu.' }
    elseif (Set-SecretValue 'SePay:WebhookApiKey' $webhookKey) { Write-Host 'Đã lưu webhook API key.' }
} finally { $webhookKey = $null }
Write-Host 'Chạy scripts/Test-Integrations.ps1 để kiểm tra cấu hình. Google login và email verification cần schema auth đã nâng cấp và cần kiểm thử provider.'
