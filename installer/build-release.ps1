<#
    Assemble the Ajazz / Stream Dock release package (and a zip) from source.

    Inputs:
      Elite\bin\Release\com.mhwlng.elite.sdPlugin   the built plugin  (dotnet build -c Release)
      bridge-native\build\streamdock-profile-bridge.dll   the built bridge  (bridge-native\build\build.bat)
      installer\*                                    the installer scripts and README

    Output:
      release\streamdeck-elite-ajazz\               the package
      release\streamdeck-elite-ajazz-v<ver>.zip     zipped, ready to upload as a release asset
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$pluginBuild = Join-Path $root 'Elite\bin\Release\com.mhwlng.elite.sdPlugin'
$dll         = Join-Path $root 'bridge-native\build\streamdock-profile-bridge.dll'
$installer   = Join-Path $root 'installer'
$out         = Join-Path $root 'release\streamdeck-elite-ajazz'

foreach ($p in @($pluginBuild, $dll)) {
    if (-not (Test-Path $p)) { throw "missing build output: $p (build the plugin and the DLL first)" }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item (Join-Path $out 'plugin') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $out 'bridge') -ItemType Directory -Force | Out-Null

# package-root files
foreach ($f in 'Install.bat','Install.ps1','Uninstall.bat','Uninstall.ps1','README.txt') {
    Copy-Item (Join-Path $installer $f) $out
}

# plugin and bridge
Copy-Item $pluginBuild (Join-Path $out 'plugin') -Recurse
Copy-Item $dll (Join-Path $out 'bridge\sdbridge.dll')
Copy-Item (Join-Path $root 'bridge-native\Patch-StreamDock.ps1') (Join-Path $out 'bridge')

# version from the assembly
$ver = (Select-String -Path (Join-Path $root 'Elite\Elite.csproj') -Pattern '<AssemblyVersion>([0-9.]+)').Matches[0].Groups[1].Value
$zip = Join-Path $root ("release\streamdeck-elite-ajazz-v{0}.zip" -f $ver)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip

$files = (Get-ChildItem $out -Recurse -File).Count
Write-Host "package: $out  ($files files)"
Write-Host "zip:     $zip"
