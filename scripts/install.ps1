<#
.SYNOPSIS
  Installs (or updates) SQL Helper into SSMS 22 for the current user. Ships next to SsmsSqlHelper.vsix.
.EXAMPLE
  .\install.ps1              # install / update
  .\install.ps1 -Uninstall   # remove
#>
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$extensionId = 'SsmsSqlHelper.3f8a2c71-5d4e-4b9a-a6c2-1e7d9b0f4a53'

function Stop-WithMessage($message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    exit 1
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { Stop-WithMessage 'Visual Studio Installer not found. Is SSMS 22 installed?' }

$instances = @(& $vswhere -products Microsoft.VisualStudio.Product.Ssms -version '[22.0,23.0)' -format json | ConvertFrom-Json)
if ($instances.Count -eq 0) { Stop-WithMessage 'SSMS 22 was not found on this computer. SQL Helper supports SSMS 22 only.' }

if (Get-Process ssms -ErrorAction SilentlyContinue) {
    Stop-WithMessage 'SSMS is running. Please close all SSMS windows and run this again.'
}

$vsix = Join-Path $PSScriptRoot 'SsmsSqlHelper.vsix'
if (-not $Uninstall -and -not (Test-Path $vsix)) { Stop-WithMessage "SsmsSqlHelper.vsix was not found next to this script ($PSScriptRoot)." }

foreach ($instance in $instances) {
    $installer = Join-Path $instance.installationPath 'Common7\IDE\VSIXInstaller.exe'
    Write-Host "SSMS $($instance.installationVersion) ($($instance.installationPath))"

    # Uninstall first so an update with the same version number is picked up too
    $p = Start-Process $installer -ArgumentList "/quiet /instanceIds:$($instance.instanceId) /u:$extensionId" -Wait -PassThru
    Write-Host "  removed previous version (exit code $($p.ExitCode); a failure here is fine when nothing was installed)"
    if ($Uninstall) { continue }

    $p = Start-Process $installer -ArgumentList "/quiet /instanceIds:$($instance.instanceId) `"$vsix`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        $log = Get-ChildItem $env:TEMP -Filter 'dd_VSIXInstaller_*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
        Stop-WithMessage "Installation failed (exit code $($p.ExitCode)). Log: $($log.FullName)"
    }
    Write-Host '  installed' -ForegroundColor Green

    # Lets SSMS rebuild its menu and extension caches so the new Tools menu entries show on first start
    $ssms = Join-Path $instance.installationPath 'Common7\IDE\SSMS.exe'
    if (Test-Path $ssms) {
        Start-Process $ssms -ArgumentList '/updateconfiguration' -Wait
    }
}

Write-Host ''
if ($Uninstall) { Write-Host 'SQL Helper was removed. Your snippets and settings in %AppData%\SsmsSqlHelper were kept.' }
else { Write-Host 'Done. Start SSMS and look for Tools > SQL Helper.' -ForegroundColor Green }
