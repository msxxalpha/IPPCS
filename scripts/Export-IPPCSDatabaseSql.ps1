# Produce a single, reviewable SSMS/sqlcmd SQL file for a brand-new IPPCS_ database.
# NO connection to SQL Server, no automatic database changes, no credentials.
[CmdletBinding()]
param(
    [ValidatePattern('^IPPCS_[A-Za-z0-9_]{1,54}$')]
    [string]$Database = 'IPPCS_Dev',
    [string]$Output = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src/IPPCS.Web/IPPCS.Web.csproj'
$builder = Join-Path $PSScriptRoot 'build_ippcs_install_sql.py'
if (-not $Output) {
    $Output = Join-Path $root "database/${Database}_Complete_Install.sql"
}
$TempFile = Join-Path ([System.IO.Path]::GetTempPath()) (
    'ippcs-ef-' + [Guid]::NewGuid().ToString('N') + '.sql')
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install .NET SDK 10 before exporting IPPCS SQL.'
    }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
        throw 'Install Python 3 for the database preflight/header composer.'
    }
    & dotnet ef --version
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet-ef 10 is required: dotnet tool install --global dotnet-ef --version 10.0.0'
    }
    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'IPPCS build failed, SQL was not generated.' }
    # Include baseline, every follow-up migration, Identity schema and seed data.
    & dotnet ef migrations script 0 --idempotent --no-build -c ProductionDb `
        --project $project --startup-project $project `
        --configuration Release --output $TempFile
    if ($LASTEXITCODE -ne 0) { throw 'EF migration script generation failed.' }
    & python $builder --source $TempFile --output $Output --database $Database
    if ($LASTEXITCODE -ne 0) { throw 'Installer composition failed.' }
    Write-Host "SQL generated, NOT executed: $Output" -ForegroundColor Green
    Write-Warning 'Review target and back up existing IPPCS data before execution. This file has no bootstrap password.'
}
finally {
    Remove-Item -LiteralPath $TempFile -Force -ErrorAction SilentlyContinue
}
