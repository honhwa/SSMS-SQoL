<#
.SYNOPSIS
  Installs (or reinstalls) the built SsmsSqlHelper VSIX into the local SSMS 22 instance.
.EXAMPLE
  .\scripts\dev-install.ps1                 # install Debug build
  .\scripts\dev-install.ps1 -Configuration Release
  .\scripts\dev-install.ps1 -Uninstall
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$extensionId = 'SsmsSqlHelper.3f8a2c71-5d4e-4b9a-a6c2-1e7d9b0f4a53'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$instanceId = & $vswhere -products Microsoft.VisualStudio.Product.Ssms -version '[22.0,23.0)' -property instanceId
$ssmsPath = & $vswhere -products Microsoft.VisualStudio.Product.Ssms -version '[22.0,23.0)' -property installationPath
if (-not $instanceId) { throw 'SSMS 22 not found.' }

if (Get-Process ssms -ErrorAction SilentlyContinue) {
    throw 'SSMS is running. Close it first.'
}

$installer = Join-Path $ssmsPath 'Common7\IDE\VSIXInstaller.exe'

# Always uninstall first so a rebuilt VSIX with the same version is picked up
Write-Host "Uninstalling previous version (if any)..."
$p = Start-Process $installer -ArgumentList "/quiet /instanceIds:$instanceId /u:$extensionId" -Wait -PassThru
Write-Host "  exit code $($p.ExitCode)"
if ($Uninstall) { return }

$vsix = Join-Path $PSScriptRoot "..\src\SsmsSqlHelper\bin\$Configuration\SsmsSqlHelper.vsix" | Resolve-Path
Write-Host "Installing $vsix into SSMS instance $instanceId..."
$p = Start-Process $installer -ArgumentList "/quiet /instanceIds:$instanceId `"$vsix`"" -Wait -PassThru
if ($p.ExitCode -ne 0) {
    $log = Get-ChildItem $env:TEMP -Filter 'dd_VSIXInstaller_*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    throw "VSIXInstaller failed with exit code $($p.ExitCode). See $($log.FullName)"
}
Write-Host 'Installed. Start SSMS and check Tools > SQL Helper > Show Active Connection.'
