<#
.SYNOPSIS
  Builds the Release VSIX and packs the team installer into dist\SsmsSqlHelper-<version>.zip
  (Install.cmd, Uninstall.cmd, install.ps1, SsmsSqlHelper.vsix, README).
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found.' }

Write-Host 'Running tests...'
dotnet test (Join-Path $root 'tests\SsmsSqlHelper.Tests') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not packing.' }

Write-Host 'Building Release...'
& $msbuild (Join-Path $root 'SsmsSqlHelper.sln') /restore /t:Rebuild /p:Configuration=Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$vsix = Join-Path $root 'src\SsmsSqlHelper\bin\Release\SsmsSqlHelper.vsix'
$manifest = [xml](Get-Content (Join-Path $root 'src\SsmsSqlHelper\source.extension.vsixmanifest'))
$version = $manifest.PackageManifest.Metadata.Identity.Version

$name = "SsmsSqlHelper-$version"
$stage = Join-Path $root "dist\$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item $vsix $stage
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $stage
Set-Content (Join-Path $stage 'Install.cmd') -Encoding ASCII -Value @'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
'@
Set-Content (Join-Path $stage 'Uninstall.cmd') -Encoding ASCII -Value @'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
'@
Set-Content (Join-Path $stage 'README.txt') -Encoding UTF8 -Value @"
SQL Helper $version - add-in for SQL Server Management Studio 22

Install:   close SSMS, then double-click Install.cmd
Update:    close SSMS, double-click Install.cmd again
Remove:    double-click Uninstall.cmd (your snippets and settings in %AppData%\SsmsSqlHelper are kept)

After installing, open SSMS and use Tools > SQL Helper.
"@

$zip = Join-Path $root "dist\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Created $zip" -ForegroundColor Green
