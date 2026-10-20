$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$evidence = Join-Path $PSScriptRoot 'Evidence'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
dotnet run --project (Join-Path $PSScriptRoot 'Tests\Gather.Tests.csproj') -c Release -p:NuGetAudit=false -- (Join-Path $evidence 'core-result.json')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
$app = Start-Process -FilePath (Join-Path $PSScriptRoot 'Portable\Gather.exe') -ArgumentList @('--smoke-test', ('"' + $evidence + '"')) -WindowStyle Hidden -Wait -PassThru
if ($app.ExitCode -ne 0) { throw 'UI smoke tests failed; see Evidence\smoke-result.json' }
Get-Content (Join-Path $evidence 'smoke-result.json')
