<#
    Installer for the Elite Dangerous plugin on Stream Dock (Ajazz / Mirabox), AKP153E.

    Does everything the plugin needs on this hardware:
      1. installs the plugin and all its artwork into Stream Dock
      2. creates the Elite Main / OnFoot / InSRV / InFighter profiles it switches between
      3. installs the profile bridge DLL next to Stream Dock
      4. code-caves Stream Dock so it loads the bridge on its own

    Run through Install.bat, which elevates and closes Stream Dock first.
#>

[CmdletBinding()]
param(
    [string] $StreamDockDir = "C:\Program Files (x86)\Stream Dock AJAZZ Global",
    [string] $DeviceUUID = "AKP153E"
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$pluginSrc = Join-Path $here 'plugin\com.mhwlng.elite.sdPlugin'
$dllSrc    = Join-Path $here 'bridge\sdbridge.dll'
$patcher   = Join-Path $here 'bridge\Patch-StreamDock.ps1'

$sdData    = Join-Path $env:APPDATA 'HotSpot\StreamDock'
$pluginDst = Join-Path $sdData 'plugins\com.mhwlng.elite.sdPlugin'
$profDir   = Join-Path $sdData 'profiles'
$exe       = Join-Path $StreamDockDir 'Stream Dock AJAZZ.exe'

function Step($msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }

# --- preflight ---
foreach ($p in @($pluginSrc, $dllSrc, $patcher)) {
    if (-not (Test-Path $p)) { throw "missing from the package: $p" }
}
if (-not (Test-Path $exe)) {
    throw "Stream Dock not found at $exe. Install Stream Dock first, or pass -StreamDockDir."
}
if (-not (Test-Path $sdData)) {
    throw "Stream Dock has not run yet ($sdData is missing). Start it once, then re-run."
}

Get-Process | Where-Object { $_.Path -like '*Stream Dock*' } | ForEach-Object {
    Write-Host "closing Stream Dock (pid $($_.Id))"
    $_ | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 3

# --- 1. plugin ---
Step 'Installing the Elite Dangerous plugin'
if (Test-Path $pluginDst) { Remove-Item $pluginDst -Recurse -Force }
Copy-Item $pluginSrc $pluginDst -Recurse
$count = (Get-ChildItem $pluginDst -Recurse -File).Count
Write-Host "  $count files -> $pluginDst"

# --- 2. profiles ---
Step 'Creating the Elite profiles'

# Copy the device identity from a profile Stream Dock already has for this deck.
$device = $null
Get-ChildItem $profDir -Directory -Filter '*.sdProfile' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($device) { return }
    $mf = Join-Path $_.FullName 'manifest.json'
    if (Test-Path $mf) {
        try { $m = Get-Content $mf -Raw | ConvertFrom-Json } catch { return }
        if ($m.DeviceUUID -eq $DeviceUUID) {
            $device = [pscustomobject]@{
                DeviceModel        = $m.DeviceModel
                DeviceSerialNumber = $m.DeviceSerialNumber
                DeviceUUID         = $m.DeviceUUID
            }
        }
    }
}
if (-not $device) {
    Write-Warning "no existing $DeviceUUID profile to copy the device identity from."
    Write-Warning "open Stream Dock once with the deck plugged in, then re-run - skipping profiles."
} else {
    $existing = @{}
    Get-ChildItem $profDir -Directory -Filter '*.sdProfile' -ErrorAction SilentlyContinue | ForEach-Object {
        $mf = Join-Path $_.FullName 'manifest.json'
        if (Test-Path $mf) {
            try { $m = Get-Content $mf -Raw | ConvertFrom-Json } catch { return }
            if ($m.DeviceUUID -eq $DeviceUUID -and $m.Name) { $existing[$m.Name] = $true }
        }
    }

    $alphabet = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray()
    function New-Uid {
        $r = New-Object Random
        $blk = { param($n) -join (1..$n | ForEach-Object { $alphabet[$r.Next($alphabet.Length)] }) }
        '{0}-{1}-{2}-{3}-{4}' -f (& $blk 8), (& $blk 4), (& $blk 4), (& $blk 4), (& $blk 12)
    }

    foreach ($name in 'Elite Main','Elite OnFoot','Elite InSRV','Elite InFighter') {
        if ($existing.ContainsKey($name)) { Write-Host "  exists: $name"; continue }
        $folder = (New-Uid) + '.sdProfile'
        $path = Join-Path $profDir $folder
        New-Item (Join-Path $path 'Images') -ItemType Directory -Force | Out-Null
        New-Item (Join-Path $path 'profiles') -ItemType Directory -Force | Out-Null
        $manifest = [ordered]@{
            Actions            = @{}
            DeviceModel        = $device.DeviceModel
            DeviceSerialNumber = $device.DeviceSerialNumber
            DeviceUUID         = $device.DeviceUUID
            Name               = $name
            Pages              = [ordered]@{ Current = $folder; Pages = @($folder) }
            Version            = '1.0'
        }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $path 'manifest.json') -Encoding UTF8
        Write-Host "  create: $name"
    }
    Write-Host "  (Stream Dock registers new profile folders itself on next start)"
}

# --- 3. bridge DLL ---
Step 'Installing the profile bridge'
Copy-Item $dllSrc (Join-Path $StreamDockDir 'sdbridge.dll') -Force
Write-Host "  sdbridge.dll -> $StreamDockDir"

# --- 4. code cave ---
Step 'Patching Stream Dock to load the bridge automatically'
& $patcher -Exe $exe

Step 'Done'
Write-Host 'Start Stream Dock. The Elite plugin will now switch profiles as the game state'
Write-Host 'changes - ship, SRV, on foot and fighter each get their own profile.'
Write-Host ''
Write-Host 'To undo everything, run Uninstall.bat.'
