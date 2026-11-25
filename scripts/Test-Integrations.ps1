# Kiểm tra cấu hình cục bộ, không kết nối dịch vụ hoặc hiển thị giá trị.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'VietVan.API.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Không tìm thấy VietVan.API.csproj.' }
[xml]$xml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$id = @($xml.Project.PropertyGroup.UserSecretsId | Where-Object { $_ }) | Select-Object -First 1
if (-not $id -or $id -notmatch '^[A-Za-z0-9._-]+$') { throw 'UserSecretsId không hợp lệ.' }
$values = @{}
function Add-ObjectValues($obj, [string]$prefix) {
    if ($null -eq $obj) { return }
    foreach ($p in $obj.PSObject.Properties) {
        $key = if ($prefix) { $prefix + ':' + $p.Name } else { $p.Name }
        if ($p.Value -is [System.Management.Automation.PSCustomObject]) { Add-ObjectValues $p.Value $key }
        elseif ($null -ne $p.Value) { $values[$key] = [string]$p.Value }
    }
}
foreach ($name in @('appsettings.json','appsettings.Development.json')) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) { Add-ObjectValues (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json) '' }
}
$secretFile = Join-Path (Join-Path (Join-Path $env:APPDATA 'Microsoft\UserSecrets') $id) 'secrets.json'
if (Test-Path -LiteralPath $secretFile) { Add-ObjectValues (Get-Content -LiteralPath $secretFile -Raw -Encoding UTF8 | ConvertFrom-Json) '' }
function Get-Setting([string]$key) {
    $environment = [Environment]::GetEnvironmentVariable($key.Replace(':','__'))
    if ($null -ne $environment) { return $environment }
    return $values[$key]
}
function Is-Configured([string]$value) {
    return (-not [string]::IsNullOrWhiteSpace($value) -and $value -notmatch '(?i)(PASTE_|CHANGE_ME|REPLACE|YOUR_SECRET|YOUR_KEY)')
}
function Show-Status([string]$name, [bool]$ok, [string]$hint) {
    if ($ok) { Write-Host ("[OK] {0}" -f $name) }
    else { Write-Host ("[THIẾU/KHÔNG HỢP LỆ] {0}: {1}" -f $name,$hint) }
}
$client = Get-Setting 'GoogleSignIn:ClientId'
Show-Status 'Google Client ID' ((Is-Configured $client) -and $client -match '^[A-Za-z0-9_-]+\.apps\.googleusercontent\.com$') 'Chạy Configure-Integrations.ps1.'
$hostValue = Get-Setting 'Email:Smtp:Host'; $port = Get-Setting 'Email:Smtp:Port'
$smtpUser = Get-Setting 'Email:Smtp:UserName'; $smtpPassword = Get-Setting 'Email:Smtp:Password'; $from = Get-Setting 'Email:Smtp:FromAddress'
$portNumber = 0
$smtpOk = (Is-Configured $hostValue) -and [int]::TryParse($port, [ref]$portNumber) -and ($portNumber -ge 1) -and ($portNumber -le 65535) -and (Is-Configured $smtpUser) -and (Is-Configured $smtpPassword) -and (Is-Configured $from)
if ($smtpOk) { try { [void](New-Object System.Net.Mail.MailAddress($smtpUser)); [void](New-Object System.Net.Mail.MailAddress($from)) } catch { $smtpOk = $false } }
Show-Status 'SMTP' $smtpOk 'Cần host, port, tài khoản, App Password và FromAddress.'
$resetPage = Get-Setting 'Email:PasswordResetUrl'; $resetPageUri = $null
$resetPageOk = (Is-Configured $resetPage) -and [Uri]::TryCreate($resetPage, [UriKind]::Absolute, [ref]$resetPageUri)
if ($resetPageOk) { $resetPageOk = -not $resetPageUri.UserInfo -and -not $resetPageUri.Fragment -and ($resetPageUri.Scheme -eq 'https' -or ($resetPageUri.Scheme -eq 'http' -and $resetPageUri.IsLoopback)) }
if ([string]::IsNullOrWhiteSpace($resetPage)) { Show-Status 'Reset mật khẩu qua mã email' $true '' }
else { Show-Status 'Trang reset mật khẩu (tùy chọn)' $resetPageOk 'URL đã cấu hình phải trỏ đến trang FE; HTTP localhost chỉ dùng Development.' }
$bank = Get-Setting 'SePay:BankId'; $bankName = Get-Setting 'SePay:BankName'; $account = Get-Setting 'SePay:AccountNumber'; $holder = Get-Setting 'SePay:AccountHolder'
$bankOk = (Is-Configured $bank) -and $bank -match '^[A-Za-z0-9]{2,30}$' -and (Is-Configured $bankName) -and (Is-Configured $account) -and $account -match '^[0-9]{5,25}$' -and (Is-Configured $holder)
Show-Status 'SePay ngân hàng' $bankOk 'Cần BankId, BankName, AccountNumber và AccountHolder.'
$webhook = Get-Setting 'SePay:WebhookApiKey'
Show-Status 'SePay Webhook API Key' ((Is-Configured $webhook) -and $webhook.Length -ge 24) 'Cần key tối thiểu 24 ký tự.'
$connection = Get-Setting 'ConnectionStrings:DefaultConnection'; $sqlOk = $false
if (Is-Configured $connection) { try { $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connection); $sqlOk = $builder.InitialCatalog -eq 'VietVan_New' -and $builder.IntegratedSecurity } catch { $sqlOk = $false } }
Show-Status 'SQL configuration' $sqlOk 'Cần database VietVan_New và Windows Authentication.'
$launch = Get-Content -LiteralPath (Join-Path $root 'Properties\launchSettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$url = [Environment]::GetEnvironmentVariable('ASPNETCORE_URLS'); if (-not $url) { $url = $launch.profiles.http.applicationUrl }
$urlOk = $false
foreach ($candidate in ($url -split ';')) { $uri = $null; if ([Uri]::TryCreate($candidate, [UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -in @('http','https')) { $urlOk = $true; break } }
Show-Status 'Backend URL' $urlOk 'Kiểm tra ASPNETCORE_URLS hoặc launchSettings.json.'
Write-Host 'Chỉ kiểm tra giá trị cục bộ, không kiểm tra kết nối hoặc xác nhận provider.'
Write-Host 'Cần schema auth đã nâng cấp; cấu hình đủ chưa chứng minh Google, SMTP hoặc SePay hoạt động.'
