<#
.SYNOPSIS
    Code-cave patcher for Stream Dock AJAZZ, so it auto-loads the profile bridge DLL.

.DESCRIPTION
    Adds a small section to Stream Dock AJAZZ.exe and redirects its entry point through a stub
    that LoadLibraryW's sdbridge.dll, then jumps to the original entry point. No original code is
    overwritten - a new section and a new entry point are added, and the original startup still
    runs. The executable is backed up to Stream Dock AJAZZ.exe.bak first.

    This is a byte-for-byte port of bridge-native/patch-streamdock.py, so the installer needs no
    Python. Only the build whose entry point is the known value is patched; anything else is left
    alone.

.PARAMETER Exe
    Path to Stream Dock AJAZZ.exe.

.PARAMETER Revert
    Restore the executable from its .bak backup.
#>

[CmdletBinding()]
param(
    [string] $Exe = "C:\Program Files (x86)\Stream Dock AJAZZ Global\Stream Dock AJAZZ.exe",
    [switch] $Revert
)

$ErrorActionPreference = 'Stop'

$DLL_NAME        = 'sdbridge.dll'
$SECTION_NAME    = [byte[]]@(0x2E,0x73,0x64,0x62,0x72,0x64,0x67,0x00)   # ".sdbrdg\0"
$EXPECT_ENTRY    = 0x3395E0
$EXPECT_LOADLIB  = 0x385188

function Get-U16($b,$o) { [BitConverter]::ToUInt16($b,$o) }
function Get-U32($b,$o) { [BitConverter]::ToUInt32($b,$o) }
function Set-U16($b,$o,$v) { [Array]::Copy([BitConverter]::GetBytes([uint16]$v),0,$b,$o,2) }
function Set-U32($b,$o,$v) { [Array]::Copy([BitConverter]::GetBytes([uint32]$v),0,$b,$o,4) }
function Align($v,$a) { [uint32](([uint64]$v + $a - 1) -band (-bnot ([uint64]$a - 1))) }

function Invoke-Patch([byte[]] $data) {
    if ((Get-U16 $data 0) -ne 0x5A4D) { throw 'not a PE file (no MZ header)' }
    $pe = Get-U32 $data 0x3C
    if ((Get-U32 $data $pe) -ne 0x00004550) { throw 'not a PE file (no PE signature)' }

    $coff = $pe + 4
    $numSections = Get-U16 $data ($coff + 2)
    $opt = $coff + 20
    if ((Get-U16 $data $opt) -ne 0x20B) { throw 'not a 64-bit PE' }

    $entryRva      = Get-U32 $data ($opt + 16)
    $sectionAlign  = Get-U32 $data ($opt + 32)
    $fileAlign     = Get-U32 $data ($opt + 36)
    $sizeOfHeaders = Get-U32 $data ($opt + 60)
    $numDirs       = Get-U32 $data ($opt + 108)
    $dirTable      = $opt + 112
    $sizeOpt       = Get-U16 $data ($coff + 16)
    $secTable      = $opt + $sizeOpt

    for ($i = 0; $i -lt $numSections; $i++) {
        $name = $data[($secTable + $i*40)..($secTable + $i*40 + 7)]
        if ((Compare-Object $name $SECTION_NAME -SyncWindow 0) -eq $null) {
            throw 'already patched (.sdbrdg section present)'
        }
    }

    if ($entryRva -ne $EXPECT_ENTRY) {
        throw ("entry point is 0x{0:X}, expected 0x{1:X} - different build; refusing to patch" -f $entryRva, $EXPECT_ENTRY)
    }

    $last   = $secTable + ($numSections - 1) * 40
    $lastVa = Get-U32 $data ($last + 12)
    $lastVs = Get-U32 $data ($last + 8)
    $newVa  = Align ($lastVa + $lastVs) $sectionAlign

    $rawSize = $fileAlign
    $newRaw  = Align $data.Length $fileAlign

    $headerEnd = $secTable + $numSections * 40
    if ($headerEnd + 40 -gt $sizeOfHeaders) { throw 'no room in the header for another section' }

    # cave body
    $body = New-Object byte[] $rawSize
    $wpath = [System.Text.Encoding]::Unicode.GetBytes($DLL_NAME + "`0")
    [Array]::Copy($wpath, 0, $body, 0, $wpath.Length)

    $pathOff = 0x00
    $stubOff = 0x80
    $pathRva = $newVa + $pathOff
    $stubRva = $newVa + $stubOff

    $stub = New-Object System.Collections.Generic.List[byte]
    $cur = $stubRva
    function Rip($target,$insn,$len) {
        $v = [int64]$target - [int64]$insn - [int64]$len
        return [uint32]($v -band 0xFFFFFFFFL)
    }

    $stub.AddRange([byte[]]@(0x48,0x83,0xEC,0x28)); $cur += 4                       # sub rsp,28h
    $d = Rip $pathRva $cur 7
    $stub.AddRange([byte[]]@(0x48,0x8D,0x0D)); $stub.AddRange([BitConverter]::GetBytes([uint32]$d)); $cur += 7   # lea rcx,[rip+path]
    $d = Rip $EXPECT_LOADLIB $cur 6
    $stub.AddRange([byte[]]@(0xFF,0x15)); $stub.AddRange([BitConverter]::GetBytes([uint32]$d)); $cur += 6         # call [rip+loadlib]
    $stub.AddRange([byte[]]@(0x48,0x83,0xC4,0x28)); $cur += 4                       # add rsp,28h
    $d = Rip $EXPECT_ENTRY $cur 7
    $stub.AddRange([byte[]]@(0x48,0x8D,0x05)); $stub.AddRange([BitConverter]::GetBytes([uint32]$d)); $cur += 7    # lea rax,[rip+oep]
    $stub.AddRange([byte[]]@(0xFF,0xE0)); $cur += 2                                 # jmp rax

    [Array]::Copy($stub.ToArray(), 0, $body, $stubOff, $stub.Count)

    # section header
    $hdr = New-Object byte[] 40
    [Array]::Copy($SECTION_NAME, 0, $hdr, 0, 8)
    Set-U32 $hdr 8  $rawSize
    Set-U32 $hdr 12 $newVa
    Set-U32 $hdr 16 $rawSize
    Set-U32 $hdr 20 $newRaw
    Set-U32 $hdr 36 (0x20000000 -bor 0x40000000)   # executable | readable
    [Array]::Copy($hdr, 0, $data, $headerEnd, 40)

    # header fixups
    Set-U16 $data ($coff + 2) ($numSections + 1)
    Set-U32 $data ($opt + 16) $stubRva
    Set-U32 $data ($opt + 56) (Align ($newVa + $rawSize) $sectionAlign)

    if ($numDirs -gt 4) {
        Set-U32 $data ($dirTable + 4*8) 0
        Set-U32 $data ($dirTable + 4*8 + 4) 0
    }

    if ($newRaw -gt $data.Length) {
        $grown = New-Object byte[] ($newRaw + $rawSize)
        [Array]::Copy($data, 0, $grown, 0, $data.Length)
        $data = $grown
    } elseif ($data.Length -lt $newRaw + $rawSize) {
        $grown = New-Object byte[] ($newRaw + $rawSize)
        [Array]::Copy($data, 0, $grown, 0, $data.Length)
        $data = $grown
    }
    [Array]::Copy($body, 0, $data, $newRaw, $rawSize)

    return ,$data
}

$bak = "$Exe.bak"

if ($Revert) {
    if (-not (Test-Path $bak)) { throw "no backup at $bak" }
    Copy-Item $bak $Exe -Force
    Write-Host "restored $Exe from backup"
    return
}

if (-not (Test-Path $Exe)) { throw "executable not found: $Exe" }

if (-not (Test-Path $bak)) {
    Copy-Item $Exe $bak
    Write-Host "backed up -> $bak"
}

$original = [System.IO.File]::ReadAllBytes($Exe)
$patched  = Invoke-Patch $original
[System.IO.File]::WriteAllBytes($Exe, $patched)
Write-Host "patched $Exe"
