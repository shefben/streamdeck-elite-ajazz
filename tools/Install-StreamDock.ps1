<#
.SYNOPSIS
    Installs the built plugin into Stream Dock (Ajazz / Mirabox), applying the changes that
    Stream Dock needs but Elgato's manifest schema forbids.

.DESCRIPTION
    Stream Dock reimplements Elgato's plugin protocol, so the same build runs on both. Two
    things differ, and neither can live in the shared manifest.json:

    1. Controllers. Elgato types this as a strict enum of "Keypad" and "Encoder" only
       (https://schemas.elgato.com/streamdeck/plugins/manifest.json), so an action that also
       declares "Information" fails validation there. Stream Dock uses "Information" for the
       screen-only displays - on an AKP153E, the 3 panels that are not keys - and its own
       plugins declare ["Keypad","Information"]. Actions that do nothing on press are the ones
       that belong there, so this script adds "Information" to exactly those.

    2. Install location. Stream Dock loads from %APPDATA%\HotSpot\StreamDock\plugins and only
       scans it at startup, so it has to be restarted around the copy.

.PARAMETER Configuration
    Build configuration to install from. Defaults to Release.

.PARAMETER SkipBuild
    Install whatever is already in bin\<Configuration> instead of rebuilding first.

.PARAMETER NoRestart
    Leave Stream Dock closed after installing.

.PARAMETER CreateProfiles
    Also create the Elite Main / OnFoot / InSRV / InFighter profiles that automatic profile
    switching needs. The plugin switches by name and ships no profiles of its own, so without
    these the switching silently does nothing. Requires python on PATH; backs up the profile
    store and DataCache.db first.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipBuild,
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'Elite\Elite.csproj'
$buildDir  = Join-Path $repoRoot "Elite\bin\$Configuration\com.mhwlng.elite.sdPlugin"
$pluginDir = Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins\com.mhwlng.elite.sdPlugin'

# Actions whose KeyPressed does nothing - pure readouts. These are the ones worth placing on a
# display that cannot be pressed. Everything else stays keypad-only.
$informationActions = @(
    'com.mhwlng.elite.exobiology'
    'com.mhwlng.elite.explorationsignals'
    'com.mhwlng.elite.gravity'
    'com.mhwlng.elite.headingaltitude'
    'com.mhwlng.elite.latlonginfo'
    'com.mhwlng.elite.navinfo'
    'com.mhwlng.elite.navtarget'
    'com.mhwlng.elite.odysseyshipstatus'
    'com.mhwlng.elite.planetinfo'
    'com.mhwlng.elite.shipstatus'
    'com.mhwlng.elite.legalbalance'
    'com.mhwlng.elite.destination'
    'com.mhwlng.elite.suitvitals'
    'com.mhwlng.elite.missions'
    'com.mhwlng.elite.carrier'
    'com.mhwlng.elite.colonisationdepot'
    'com.mhwlng.elite.docking'
    'com.mhwlng.elite.targetinfo'
    'com.mhwlng.elite.fuelscoop'
)

function Get-StreamDockExe {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Stream Dock AJAZZ Global\Stream Dock AJAZZ.exe"
        "$env:ProgramFiles\Stream Dock AJAZZ Global\Stream Dock AJAZZ.exe"
        "${env:ProgramFiles(x86)}\Stream Dock\Stream Dock.exe"
        "$env:ProgramFiles\Stream Dock\Stream Dock.exe"
    )

    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Stop-StreamDock {
    $running = Get-Process | Where-Object { $_.Path -like '*Stream Dock*' }

    if (-not $running) { return $false }

    Write-Host 'Stopping Stream Dock ...'
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 4

    return $true
}

if (-not $SkipBuild) {
    Write-Host "Building $Configuration ..."
    & dotnet build $project -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path $buildDir)) {
    throw "No build output at $buildDir. Build the plugin first, or drop -SkipBuild."
}

$exe = Get-StreamDockExe
$wasRunning = Stop-StreamDock

Write-Host "Installing to $pluginDir ..."

if (Test-Path $pluginDir) { Remove-Item -Recurse -Force $pluginDir }

# robocopy uses exit codes 0-7 for success; only 8+ is a real failure.
$null = robocopy $buildDir $pluginDir /E /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }
$global:LASTEXITCODE = 0

$manifestPath = Join-Path $pluginDir 'manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

$patched = 0

foreach ($action in $manifest.Actions) {
    if ($informationActions -notcontains $action.UUID) { continue }

    $controllers = @('Keypad', 'Information')

    if ($action.PSObject.Properties.Name -contains 'Controllers') {
        $action.Controllers = $controllers
    }
    else {
        $action | Add-Member -NotePropertyName 'Controllers' -NotePropertyValue $controllers
    }

    $patched++
}

$manifest | ConvertTo-Json -Depth 32 | Set-Content $manifestPath -Encoding UTF8

Write-Host "Patched $patched action(s) to also accept the Information controller."

$missing = $informationActions | Where-Object { $_ -notin ($manifest.Actions.UUID) }
if ($missing) { Write-Warning "Not found in manifest: $($missing -join ', ')" }

if ($NoRestart) {
    Write-Host 'Done. Stream Dock not restarted (-NoRestart).'
    return
}

if (-not $exe) {
    Write-Warning 'Stream Dock executable not found - start it yourself to load the plugin.'
    return
}

if ($wasRunning -or -not (Get-Process | Where-Object { $_.Path -like '*Stream Dock*' })) {
    Write-Host 'Starting Stream Dock ...'
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
}

Write-Host 'Done.'
