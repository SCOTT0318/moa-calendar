param([string]$CompilerPath, [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$version = ([xml](Get-Content (Join-Path $PSScriptRoot 'Gather.csproj') -Raw)).Project.PropertyGroup.Version
$publish = Join-Path $PSScriptRoot 'dist\Moa-win-x64'
$dist = Join-Path $PSScriptRoot 'dist'
if (-not $SkipPublish) {
 dotnet publish (Join-Path $PSScriptRoot 'Gather.csproj') -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:PublishSingleFile=false -m:1 -nr:false -o $publish
 if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
}
if (-not (Test-Path (Join-Path $publish 'coreclr.dll'))) { throw 'Self-contained runtime is missing' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.ko.md'),(Join-Path $PSScriptRoot 'INTEGRATION.ko.md'),(Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $publish -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $publish -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $publish -Recurse -Force
if (Test-Path (Join-Path $PSScriptRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $publish -Recurse -Force }
if (-not $CompilerPath) {
 $compilerRoot = Join-Path $PSScriptRoot 'tools\InnoSetup'
 $CompilerPath = Join-Path $compilerRoot 'compiler\ISCC.exe'
 if (-not (Test-Path -LiteralPath $CompilerPath)) {
  New-Item -ItemType Directory -Force -Path $compilerRoot | Out-Null
  $bootstrap = Join-Path $compilerRoot 'innosetup-7.1.0-x64.exe'
  Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $bootstrap
  if ((Get-FileHash $bootstrap).Hash -ne '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F') { throw 'Compiler checksum mismatch' }
  $signature = Get-AuthenticodeSignature $bootstrap
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') { throw 'Compiler publisher signature mismatch' }
  $process = Start-Process -FilePath $bootstrap -ArgumentList ('/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /TASKS="" /DIR="' + (Join-Path $compilerRoot 'compiler') + '"') -WindowStyle Hidden -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw 'Compiler preparation failed' }
 }
}
$stage = Join-Path ([IO.Path]::GetTempPath()) ('Moa-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
& $CompilerPath '/Q' "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$stage" (Join-Path $PSScriptRoot 'installer\Moa.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed; staging kept at $stage" }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$source = Join-Path $stage 'Moa-Setup.exe'; $target = Join-Path $dist 'Moa-Setup.exe'
Copy-Item -LiteralPath $source -Destination $target -Force
if ((Get-FileHash $source).Hash -ne (Get-FileHash $target).Hash) { throw 'Installer copy mismatch' }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $dist 'Moa-win-x64.zip') -Force
Get-ChildItem $dist -File | Where-Object { $_.Name -in 'Moa-Setup.exe','Moa-win-x64.zip' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLowerInvariant(),$_.Name } | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Installer ready: $target"
