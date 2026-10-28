# SAFE first-factory-UAT check against a locally running Visual Studio host.
# The only POST is user login. No factory records are created or changed.
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:7295',
    [string]$Username = 'ippcs-local-admin'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$target=[Uri]$BaseUrl
if($target.Scheme -ne 'https' -or
   $target.Host -notin @('localhost','127.0.0.1','::1') -or
   $target.AbsolutePath -ne '/' -or $target.Query -ne '') {
    throw 'For credential safety, this test accepts only a local HTTPS URL (e.g. https://localhost:7295).'
}
if([string]::IsNullOrWhiteSpace($Username))throw 'Enter a real local IPPCS username.'
$root=$BaseUrl.TrimEnd('/')
$session=New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Fetch([string]$Path) {
    $response=Invoke-WebRequest -Uri ($root+$Path) -WebSession $session -UseBasicParsing -TimeoutSec 20
    if($response.StatusCode -ne 200)throw "$Path returned HTTP $($response.StatusCode)."
    return $response
}

Write-Host '1/4 Checking HTTPS, host, and database migrations...' -ForegroundColor Cyan
$health=(Fetch '/health').Content | ConvertFrom-Json
if($health.status -ne 'ready'){
    throw 'IPPCS database is not fully ready. Recheck dedicated SQL installation and ConnectionStrings:Production.'
}
if($health.version -ne '0.8.0-first-uat1'){
    throw "Wrong IPPCS build: running $($health.version), expected 0.8.0-first-uat1. Checkout the correct first-test branch."
}
Write-Host '2/4 Checking all application entry points...' -ForegroundColor Cyan
$pages=@(
    '/login.html','/app.html','/planning.html','/planning-advanced.html',
    '/execution.html','/control-plan.html','/factory-reports.html',
    '/factory-print.html','/factory-master-data.html','/factory-release-readiness.html',
    '/factory-recall.html','/production-deviations.html','/mps-pegging.html',
    '/engineering-rebind.html','/system-users.html','/help.html'
)
foreach($p in $pages){
    $response=Fetch $p
    if($response.Content -notmatch '(?i)<!doctype html|<html')
        {throw "Broken HTML entry point: $p"}
}
Write-Host "PASS: $($pages.Count) application entry pages are served." -ForegroundColor Green

Write-Host '3/4 Checking authenticated local Administrator without modifying production data...' -ForegroundColor Cyan
$password=Read-Host 'Password of the LOCAL IPPCS development administrator (never saved or printed)' -AsSecureString
$bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
try{
    $plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $body=@{username=$Username;password=$plain} | ConvertTo-Json -Compress
    $null=Invoke-RestMethod -Uri ($root+'/auth/login') -Method Post -WebSession $session `
        -ContentType 'application/json' -Body $body -TimeoutSec 20
}finally{
    if($bstr -ne [IntPtr]::Zero){
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    Remove-Variable plain,body,password -ErrorAction SilentlyContinue
}
$who=(Fetch '/auth/me').Content | ConvertFrom-Json
if($who.username -ne $Username)throw 'Logged-in account does not match the requested IPPCS username.'

Write-Host '4/4 Checking master data and all 18 module route contracts...' -ForegroundColor Cyan
$readiness=(Fetch '/api/v1/factory-release-readiness').Content | ConvertFrom-Json
if($readiness.declaredModules -ne 18 -or $readiness.modules.Count -ne 18 -or
   $readiness.productionReleaseAuthorized -ne $false){
   throw 'The 18-module readiness contract or factory acceptance safety flag is invalid.'
}
foreach($path in @(
 '/api/v1/items','/api/v1/work-centers','/api/v1/routings','/api/v1/bom-revisions',
 '/api/v1/mps','/api/v1/manufacturing-orders','/api/v1/control-plans',
 '/api/v1/factory-reports/dimensions',
 '/api/v1/factory-master-options?kind=UnitOfMeasure'
)){
 $null=Fetch $path
}
Write-Host 'PASS: SQL schema, HTTPS app, login, all 18 module entries, master-data APIs and reporting routes.' -ForegroundColor Green
Write-Host ("Database setup prerequisites configured: {0}/{1}" -f $readiness.configuredSetupGates,$readiness.totalSetupGates)
Write-Warning 'READY TO START MANUAL FIRST FACTORY TEST IN DEVELOPMENT. This is NOT factory acceptance or IIS production authorization.'
Write-Host 'Open https://localhost:7295/help.html, then https://localhost:7295/factory-release-readiness.html'
