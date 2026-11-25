$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests/VietVan.DatabaseBootstrap/VietVan.DatabaseBootstrap.csproj'
$temporaryScript = Join-Path $root 'artifacts/FreshBaseline.generated.sql'
$output = Join-Path $PSScriptRoot 'VietVan_New_Baseline.sql'
New-Item -ItemType Directory -Force -Path (Split-Path $temporaryScript) | Out-Null
dotnet ef migrations script --project $project --startup-project $project --context FreshDatabaseContext --no-build --output $temporaryScript
if ($LASTEXITCODE -ne 0) { throw 'EF could not generate the migration script.' }
$raw = [System.IO.File]::ReadAllText($temporaryScript)
$batches = [regex]::Split($raw, '(?im)^\s*GO\s*\r?$')
$script = New-Object System.Text.StringBuilder
[void]$script.AppendLine('-- Generated from FreshBaseline. Run only against a confirmed empty VietVan_New database.')
[void]$script.AppendLine('SET NOCOUNT ON; SET XACT_ABORT ON;')
[void]$script.AppendLine('BEGIN TRY')
[void]$script.AppendLine("  IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;")
[void]$script.AppendLine("  IF ISNULL(CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')), N'') <> N'SQLEXPRESS' THROW 51001, 'Expected SQL Server instance SQLEXPRESS.', 1;")
[void]$script.AppendLine("  IF SCHEMA_NAME() <> N'dbo' THROW 51002, 'The executing database user must have default schema dbo.', 1;")
[void]$script.AppendLine("  IF @@TRANCOUNT <> 0 THROW 51003, 'Run outside an existing transaction.', 1;")
[void]$script.AppendLine("  IF EXISTS (SELECT 1 FROM sys.objects WHERE is_ms_shipped=0 AND type IN ('U','V','P','FN','IF','TF','SN','SO')) THROW 51004, 'Database contains user objects; no changes made.', 1;")
[void]$script.AppendLine('  BEGIN TRANSACTION;')
foreach ($batch in $batches) {
    $batch = $batch.Trim()
    if (-not $batch -or $batch -match '^(?i:BEGIN\s+TRANSACTION|COMMIT)\s*;?$') { continue }
    [void]$script.AppendLine("  EXEC sys.sp_executesql N'" + $batch.Replace("'", "''") + "';")
}
[void]$script.AppendLine('  COMMIT TRANSACTION;')
[void]$script.AppendLine("  PRINT N'VietVan_New baseline completed.';")
[void]$script.AppendLine('END TRY')
[void]$script.AppendLine('BEGIN CATCH')
[void]$script.AppendLine('  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;')
[void]$script.AppendLine('  THROW;')
[void]$script.AppendLine('END CATCH;')
[System.IO.File]::WriteAllText($output, $script.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Generated $output"
