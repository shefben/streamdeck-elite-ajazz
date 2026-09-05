<#
    Removes everything Install.ps1 added: reverts the Stream Dock patch, removes the bridge DLL,
    and removes the plugin. The Elite profiles are left in place - they are ordinary Stream Dock
    profiles and may hold buttons you arranged; delete them by hand in Stream Dock if you want.
#>

[CmdletBinding()]
param(
    [string] $StreamDockDir = "C:\Program Files (x86)\Stream Dock AJAZZ Global"
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$exe       = Join-Path $StreamDockDir 'Stream Dock AJAZZ.exe'
$patcher   = Join-Path $here 'bridge\Patch-StreamDock.ps1'
$dll       = Join-Path $StreamDockDir 'sdbridge.dll'
$pluginDst = Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins\com.mhwlng.elite.sdPlugin'

Get-Process | Where-Object { $_.Path -like '*Stream Dock*' } | ForEach-Object {
    Write-Host "closing Stream Dock (pid $($_.Id))"
    $_ | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 3

Write-Host '==> Reverting the Stream Dock patch'
if (Test-Path "$exe.bak") {
    & $patcher -Exe $exe -Revert
} else {
    Write-Warning "no backup at $exe.bak - leaving the executable as is."
}

Write-Host '==> Removing the bridge DLL'
if (Test-Path $dll) { Remove-Item $dll -Force; Write-Host "  removed $dll" }

Write-Host '==> Removing the plugin'
if (Test-Path $pluginDst) { Remove-Item $pluginDst -Recurse -Force; Write-Host "  removed $pluginDst" }

Write-Host ''
Write-Host 'Done. The Elite profiles were left in Stream Dock; remove them there if you want.'
