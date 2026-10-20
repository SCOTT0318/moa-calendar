param([string]$InstallerPath = (Join-Path $PSScriptRoot '..\dist\Moa-Setup.exe'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('Evidence\Installer-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $testRoot 'App'; $data = Join-Path $testRoot 'UserData'
if (-not [IO.Path]::GetFullPath($target).StartsWith((Join-Path $root 'Evidence') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe isolated install path' }
New-Item -ItemType Directory -Force -Path $data | Out-Null
$sentinel = Join-Path $data 'preserve.txt'; 'user-data-preservation-sentinel' | Set-Content $sentinel -Encoding utf8
$before = (Get-FileHash $sentinel).Hash
function Install {
 $process = Start-Process -FilePath $InstallerPath -ArgumentList ('/CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /SMOKETEST=1 /TASKS="" /DIR="' + $target + '" /LOG="' + (Join-Path $testRoot 'setup.log') + '"') -WindowStyle Hidden -Wait -PassThru
 if ($process.ExitCode -ne 0) { throw "Installer returned $($process.ExitCode)" }
}
Install
foreach ($file in 'Gather.exe','Gather.dll','coreclr.dll','THIRD-PARTY-NOTICES.md') { if (-not (Test-Path (Join-Path $target $file))) { throw "Missing $file" } }
$runtime = Get-Content (Join-Path $target 'Gather.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'Installed payload is not self-contained' }
$ui = Start-Process -FilePath (Join-Path $target 'Gather.exe') -ArgumentList @('--smoke-test', ('"' + (Join-Path $testRoot 'Checks') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($ui.ExitCode -ne 0) { throw 'Installed app UI check failed' }
$hashes = Get-ChildItem -LiteralPath $data -File | ForEach-Object { @{ Name = $_.Name; SHA256 = (Get-FileHash $_.FullName).Hash } }
Install
if ((Get-FileHash $sentinel).Hash -ne $before) { throw 'Reinstall changed user data' }
$uninstaller = Join-Path $target 'unins000.exe'
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw 'Uninstall failed' }
if ((Get-FileHash $sentinel).Hash -ne $before) { throw 'Uninstall changed user data' }
$result = @{ success = $true; runtimeIncluded = $true; installedUiVerified = $true; reinstallPreservedData = $true; uninstallPreservedData = $true; signature = [string](Get-AuthenticodeSignature -LiteralPath $InstallerPath).Status; installerSHA256 = (Get-FileHash $InstallerPath).Hash; uiResult = (Get-Content (Join-Path $testRoot 'Checks\smoke-result.json') -Raw | ConvertFrom-Json); verifiedAt = [DateTimeOffset]::Now.ToString('O') }
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'Evidence\installer-result.json') -Encoding utf8
Write-Host 'Installer / installed UI / reinstall / uninstall checks passed. User data preserved.'
