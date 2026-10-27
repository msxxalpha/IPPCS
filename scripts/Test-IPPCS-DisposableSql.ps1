# IPPCS standalone SQL integration test on Windows. Requires Docker Desktop (Linux containers).
# Database migrations run ONLY against a random, disposable SQL Server container.
# No warehouse, purchasing, sales, HR connection, or existing database is ever touched.
[CmdletBinding()]
param([int]$SqlPort=11433,[int]$WebPort=5295,[switch]$SkipPreflight)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project=Join-Path $root 'src/IPPCS.Web/IPPCS.Web.csproj'
$name='ippcs-ci-'+[guid]::NewGuid().ToString('N').Substring(0,12)
$dbName='IPPCS_Accept_'+[guid]::NewGuid().ToString('N').Substring(0,8)
$logs=Join-Path ([IO.Path]::GetTempPath()) $name
$started=$false
$web=$null
$oldLocation=Get-Location
$keys=@('ASPNETCORE_ENVIRONMENT','ConnectionStrings__Production','Bootstrap__AdminUsername',
        'Bootstrap__AdminPassword','CI_ADMIN_PASSWORD','IPPCS_E2E_BASE')
$previous=@{}
foreach($key in $keys){
 $entry=Get-Item ("Env:"+$key) -ErrorAction SilentlyContinue
 $previous[$key]=if($null -eq $entry){$null}else{$entry.Value}
}
function Require([string]$cmd){
 if(-not(Get-Command $cmd -ErrorAction SilentlyContinue)){throw "Required command not found: $cmd"}
}
function PortFree([int]$port){
 if($port -lt 1024 -or $port -gt 65535){throw 'Test ports must be 1024..65535.'}
 $s=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
 try{$s.Start()}catch{throw "Port $port is occupied; no services were modified."}
 finally{$s.Stop()}
}
try{
 Require 'docker';Require 'dotnet';Require 'node';Require 'python'
 & docker info --format '{{.ServerVersion}}' | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Docker Engine is not running.'}
 if($SqlPort -eq $WebPort){throw 'SQL and HTTP ports must differ.'}
 PortFree $SqlPort
 PortFree $WebPort
 Set-Location $root
 if(-not $SkipPreflight){
  & (Join-Path $PSScriptRoot 'Validate-IPPCS.ps1')
  if(-not $?){throw 'Build, unit, JS, or EF model validation failed.'}
 }else{
  Write-Warning 'Build/unit/EF preflight skipped: this does NOT satisfy the full release gate.'
  & dotnet restore $project
  if($LASTEXITCODE -ne 0){throw 'Restore failed.'}
  & dotnet build $project -c Release --no-restore
  if($LASTEXITCODE -ne 0){throw 'Build failed.'}
 }
 & dotnet ef --version | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Install dotnet-ef version 10 to run EF migrations.'}
 New-Item -ItemType Directory -Path $logs -Force | Out-Null

 # Generated credentials never appear in the repository or log files.
 $sqlPassword='Ci_Aa1!'+[guid]::NewGuid().ToString('N')
 $adminPassword='Ci_Aa1!'+[guid]::NewGuid().ToString('N')
 $env:ASPNETCORE_ENVIRONMENT='Development'
 $env:ConnectionStrings__Production="Server=127.0.0.1,$SqlPort;Database=$dbName;User Id=sa;Password=$sqlPassword;Encrypt=True;TrustServerCertificate=True;"
 $env:Bootstrap__AdminUsername='ci-admin'
 $env:Bootstrap__AdminPassword=$adminPassword
 $env:CI_ADMIN_PASSWORD=$adminPassword
 $env:IPPCS_E2E_BASE="http://127.0.0.1:$WebPort"
 Write-Host "Starting isolated SQL 2022 container $name; test database $dbName" -ForegroundColor Cyan
 & docker run -d --name $name -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e "MSSQL_SA_PASSWORD=$sqlPassword" -p "127.0.0.1:$($SqlPort):1433" mcr.microsoft.com/mssql/server:2022-latest | Out-Null
 if($LASTEXITCODE -ne 0){throw 'SQL Server container failed to start.'}
 $started=$true
 $migrated=$false
 for($attempt=1;$attempt -le 12;$attempt++){
  & dotnet ef database update --project $project --startup-project $project --configuration Release 1> (Join-Path $logs 'migration.log') 2> (Join-Path $logs 'migration-errors.log')
  if($LASTEXITCODE -eq 0){$migrated=$true;break}
  Start-Sleep -Seconds 5
 }
 if(-not $migrated){throw "Isolated EF migration failed: inspect $logs"}
 $webArgs=@('run','--project','src/IPPCS.Web/IPPCS.Web.csproj','--no-build','-c','Release','--no-launch-profile','--urls',"http://127.0.0.1:$WebPort")
 $web=Start-Process -FilePath 'dotnet' -PassThru -WorkingDirectory $root -ArgumentList $webArgs -RedirectStandardOutput (Join-Path $logs 'web.log') -RedirectStandardError (Join-Path $logs 'web-errors.log')
 $ready=$false
 for($attempt=1;$attempt -le 30;$attempt++){
  if($web.HasExited){break}
  try{
   $health=Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Uri "http://127.0.0.1:$WebPort/health"
   if($health.StatusCode -eq 200){$ready=$true;break}
  }catch{}
  Start-Sleep -Seconds 2
 }
 if(-not $ready){throw "Development web host did not pass health check: $logs"}
 $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
 $base="http://127.0.0.1:$WebPort"
 $login=@{username='ci-admin';password=$adminPassword}|ConvertTo-Json
 $null=Invoke-RestMethod -Uri ($base+'/auth/login') -Method Post -WebSession $session -Body $login -ContentType 'application/json'
 $csrf=Invoke-RestMethod -Uri ($base+'/auth/csrf') -WebSession $session
 if(-not $csrf.token){throw 'Local auth CSRF token missing.'}
 $seed=@{code='SHOCK-CI-001';name='Disposable IPPCS test shock absorber';unit='PCS';type=2}|ConvertTo-Json
 $null=Invoke-RestMethod -Uri ($base+'/api/v1/items') -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-IPPCS-CSRF'=$csrf.token} -Body $seed
 Write-Host 'Running the full authenticated manufacturing SQL acceptance suite...' -ForegroundColor Cyan
 & python (Join-Path $root 'tests/production_e2e_smoke.py')
 if($LASTEXITCODE -ne 0){throw "SQL end-to-end suite failed; see $logs"}
 Write-Host 'PASS: isolated SQL acceptance (still requires actual factory UAT before production).' -ForegroundColor Green
}finally{
 if($null -ne $web){try{if(-not $web.HasExited){Stop-Process -Id $web.Id -Force -ErrorAction SilentlyContinue}}catch{}}
 if($started){& docker rm -f $name | Out-Null}
 foreach($key in $keys){
  if($null -eq $previous[$key]){Remove-Item ("Env:"+$key) -ErrorAction SilentlyContinue}
  else{Set-Item ("Env:"+$key) $previous[$key]}
 }
 Set-Location $oldLocation
 Write-Host "Temporary container removed; diagnostic files: $logs" -ForegroundColor DarkGray
}
