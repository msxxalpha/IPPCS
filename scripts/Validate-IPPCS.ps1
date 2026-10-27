# IPPCS local pre-merge verification, safe on a developer workstation.
# This script NEVER updates, drops, migrates or recreates a database.
# For authenticated end-to-end SQL tests use the separate disposable CI workflow.
[CmdletBinding()]
param(
    [switch]$SkipEfCheck,
    [switch]$SkipJavaScript
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Gate([string]$Name, [string]$Command, [string[]]$Arguments) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE; no database changed."
    }
}

$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Project = Join-Path $Root 'src/IPPCS.Web/IPPCS.Web.csproj'
$Tests = Join-Path $Root 'tests/IPPCS.Tests/IPPCS.Tests.csproj'
$AppScripts = Join-Path $Root 'src/IPPCS.Web/wwwroot/js'
$JalaliTests = Join-Path $Root 'tests/jalali.test.cjs'
$Start = Get-Location
try {
    Set-Location $Root
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Install .NET SDK 10 before running IPPCS gates."
    }

    Invoke-Gate 'Restore application' 'dotnet' @('restore', $Project)
    Invoke-Gate 'Restore unit tests' 'dotnet' @('restore', $Tests)
    Invoke-Gate 'Release build of application' 'dotnet' @('build', $Project, '-c', 'Release', '--no-restore')
    Invoke-Gate 'Release xUnit regression' 'dotnet' @('test', $Tests, '-c', 'Release', '--no-restore')

    if (-not $SkipJavaScript) {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            throw "Install Node.js LTS to run browser and Jalali date tests."
        }
        $Scripts = @(Get-ChildItem -LiteralPath $AppScripts -Filter '*.js' -File)
        if ($Scripts.Count -eq 0) { throw 'No application JavaScript files found.' }
        foreach ($Script in $Scripts) {
            Invoke-Gate "Node syntax: $($Script.Name)" 'node' @('--check', $Script.FullName)
        }
        $BrowserTests = @(
            $JalaliTests,
            (Join-Path $Root 'tests/factory-recall-ui.test.cjs'),
            (Join-Path $Root 'tests/eco-ui.test.cjs'),
            (Join-Path $Root 'tests/factory-master-ui.test.cjs'),
            (Join-Path $Root 'tests/factory-first-run-ui.test.cjs')
        )
        Invoke-Gate 'Jalali and accessible factory UI browser contract tests' 'node' (@('--test') + $BrowserTests)
    }

    if (-not $SkipEfCheck) {
        # Only compare the EF model to the committed snapshot. No DB migrate.
        & dotnet ef --version
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-ef 10 is required; install it before full verification."
        }
        Invoke-Gate 'EF model vs committed migrations (read-only)' 'dotnet' @(
            'ef', 'migrations', 'has-pending-model-changes',
            '--project', $Project, '--startup-project', $Project)
    }
    else {
        Write-Warning 'EF model check skipped: result is NOT a full release gate.'
    }
    if ($SkipJavaScript) {
        Write-Warning 'Browser and Jalali checks skipped: result is NOT a full release gate.'
    }

    $Git = 'UNKNOWN'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $Resolved = (& git rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $Resolved) { $Git = $Resolved }
    }
    Write-Host "PASS: Local source-only checks. SHA: $Git" -ForegroundColor Green
    Write-Warning 'NOT a release approval. SQL disposable E2E, backup/restore, IIS role/HTTPS checks and 18-module factory UAT remain mandatory.'
}
finally {
    Set-Location $Start
}
