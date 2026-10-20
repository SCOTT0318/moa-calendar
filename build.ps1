$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet publish (Join-Path $PSScriptRoot 'Gather.csproj') -c Release --no-self-contained -p:NuGetAudit=false -m:1 -nr:false -o (Join-Path $PSScriptRoot 'Portable')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.ko.md'), (Join-Path $PSScriptRoot 'INTEGRATION.ko.md') -Destination (Join-Path $PSScriptRoot 'Portable') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination (Join-Path $PSScriptRoot 'Portable') -Recurse -Force
if (Test-Path (Join-Path $PSScriptRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $PSScriptRoot 'Portable') -Recurse -Force }
Write-Host "Ready: $PSScriptRoot\Portable\Gather.exe"
