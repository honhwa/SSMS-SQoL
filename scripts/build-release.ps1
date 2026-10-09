<#
.SYNOPSIS
  Builds the Release VSIX and packs the team installer into dist\SsmsSqlHelper-<version>.zip
  (Install.cmd, Uninstall.cmd, install.ps1, SsmsSqlHelper.vsix, README).
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$manifest = [xml](Get-Content (Join-Path $root 'src\SsmsSqlHelper\source.extension.vsixmanifest'))
$version = $manifest.PackageManifest.Metadata.Identity.Version
$assemblyInfo = Get-Content (Join-Path $root 'src\SsmsSqlHelper\Properties\AssemblyInfo.cs') -Raw
$assemblyVersion = "$version.0"
if ($assemblyInfo -notmatch ('AssemblyVersion\("' + [regex]::Escape($assemblyVersion) + '"\)') -or
    $assemblyInfo -notmatch ('AssemblyFileVersion\("' + [regex]::Escape($assemblyVersion) + '"\)')) {
    throw "AssemblyInfo.cs versions must match VSIX version $version before packaging."
}
$informationalMatch = [regex]::Match($assemblyInfo, 'AssemblyInformationalVersion\("([^"]+)"\)')
$releaseLabel = if ($informationalMatch.Success) { $informationalMatch.Groups[1].Value } else { $version }
if ($releaseLabel -ne $version -and -not $releaseLabel.StartsWith("$version-")) {
    throw "Informational version $releaseLabel must start with $version."
}

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found.' }

Write-Host 'Running tests...'
dotnet test (Join-Path $root 'tests\SsmsSqlHelper.Tests') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not packing.' }

Write-Host 'Building Release...'
& $msbuild (Join-Path $root 'SsmsSqlHelper.sln') /restore /t:Rebuild /p:Configuration=Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$vsix = Join-Path $root 'src\SsmsSqlHelper\bin\Release\SsmsSqlHelper.vsix'

$name = "SsmsSqlHelper-$releaseLabel"
$stage = Join-Path $root "dist\$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item $vsix $stage
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $stage
Copy-Item (Join-Path $root 'README.md') $stage
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
SQL Helper $releaseLabel - add-in for SQL Server Management Studio 22

Install:   close SSMS, then double-click Install.cmd
Update:    close SSMS, double-click Install.cmd again
Remove:    double-click Uninstall.cmd (your snippets and settings in %AppData%\SsmsSqlHelper are kept)

After installing, open SSMS and use Tools > SQL Helper.

Shortcuts:
  Tab          Expand snippets, SELECT *, INSERT, and EXEC parameters
  Ctrl+F12     Select a procedure, table, view, or function in Object Explorer
  F12          Open ALTER script for procedure/view/function, or Table Designer for a table

Check updates: Tools > SQL Helper > Check for Updates (opens GitHub Releases)

Full guide: README.md in this folder.
"@

$zip = Join-Path $root "dist\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Created $zip" -ForegroundColor Green
