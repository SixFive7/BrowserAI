# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
  Packs the four rendered PNGs of one icon candidate into a multi-size Windows .ico.

.DESCRIPTION
  16 / 32 / 48 are written as 32-bit BGRA DIB entries (BITMAPINFOHEADER, doubled
  height, bottom-up rows, an all-zero AND mask). 256 is written as the PNG file
  verbatim, which is the Vista+ PNG-compressed entry and is why the file stays small.

.EXAMPLE
  pwsh -File .\Make-Ico.ps1 -Candidate 1 -Verify
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Candidate,
    [string] $PngDir,
    [string] $Out,
    [int[]] $Sizes = @(16, 32, 48, 256),
    [switch] $Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$stem = 'candidate-{0:D2}' -f $Candidate
if (-not $PngDir) { $PngDir = Join-Path $here 'png' }
if (-not $Out)    { $Out    = Join-Path $here "$stem.ico" }

function Get-DibEntry([string] $Path, [int] $Size) {
    $bmp = [System.Drawing.Bitmap]::new($Path)
    try {
        if ($bmp.Width -ne $Size -or $bmp.Height -ne $Size) {
            throw "$Path is $($bmp.Width)x$($bmp.Height), expected ${Size}x${Size}"
        }
        $ms = [System.IO.MemoryStream]::new()
        $bw = [System.IO.BinaryWriter]::new($ms)
        $maskStride = [int][Math]::Floor(($Size + 31) / 32) * 4
        $maskBytes  = $maskStride * $Size
        # BITMAPINFOHEADER
        $bw.Write([uint32]40)             # biSize
        $bw.Write([int32]$Size)           # biWidth
        $bw.Write([int32]($Size * 2))     # biHeight  (XOR + AND stacked)
        $bw.Write([uint16]1)              # biPlanes
        $bw.Write([uint16]32)             # biBitCount
        $bw.Write([uint32]0)              # biCompression = BI_RGB
        $bw.Write([uint32]($Size * $Size * 4 + $maskBytes))
        $bw.Write([int32]0); $bw.Write([int32]0)
        $bw.Write([uint32]0); $bw.Write([uint32]0)
        # XOR: BGRA, bottom-up
        for ($y = $Size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $Size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G)
                $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        # AND mask: all zero, the alpha channel carries the shape
        $bw.Write((New-Object byte[] $maskBytes))
        $bw.Flush()
        return ,$ms.ToArray()
    }
    finally { $bmp.Dispose() }
}

$entries = foreach ($size in ($Sizes | Sort-Object)) {
    $png = Join-Path $PngDir ('{0}-{1:D3}.png' -f $stem, $size)
    if (-not (Test-Path -LiteralPath $png)) { throw "missing render: $png" }
    if ($size -ge 256) {
        [pscustomobject]@{ Size = $size; Kind = 'PNG'; Data = [System.IO.File]::ReadAllBytes($png) }
    } else {
        [pscustomobject]@{ Size = $size; Kind = 'DIB'; Data = (Get-DibEntry -Path $png -Size $size) }
    }
}

$fs = [System.IO.File]::Create($Out)
try {
    $bw = [System.IO.BinaryWriter]::new($fs)
    $bw.Write([uint16]0)                 # reserved
    $bw.Write([uint16]1)                 # type 1 = icon
    $bw.Write([uint16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim)
        $bw.Write([byte]0)               # colour count (0 = truecolour)
        $bw.Write([byte]0)               # reserved
        $bw.Write([uint16]1)             # planes
        $bw.Write([uint16]32)            # bit count
        $bw.Write([uint32]$e.Data.Length)
        $bw.Write([uint32]$offset)
        $offset += $e.Data.Length
    }
    foreach ($e in $entries) { $bw.Write([byte[]]$e.Data, 0, $e.Data.Length) }
    $bw.Flush()
}
finally { $fs.Dispose() }

Write-Host "wrote $Out ($((Get-Item $Out).Length) bytes, $($entries.Count) entries)"

if ($Verify) {
    $bytes = [System.IO.File]::ReadAllBytes($Out)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    Write-Host ("reserved={0} type={1} count={2}" -f [BitConverter]::ToUInt16($bytes,0), [BitConverter]::ToUInt16($bytes,2), $count)
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + 16 * $i
        $w = $bytes[$o]; $h = $bytes[$o+1]
        $len = [BitConverter]::ToUInt32($bytes, $o+8)
        $off = [BitConverter]::ToUInt32($bytes, $o+12)
        $magic = $bytes[$off..($off+7)]
        $isPng = ($magic[0] -eq 0x89 -and $magic[1] -eq 0x50 -and $magic[2] -eq 0x4E -and $magic[3] -eq 0x47)
        $payload = if ($isPng) { 'PNG' } else { 'BMP/DIB (biSize=' + [BitConverter]::ToUInt32($bytes,$off) + ')' }
        $dw = if ($w -eq 0) { 256 } else { $w }
        $dh = if ($h -eq 0) { 256 } else { $h }
        '{0,3}x{1,-3} planes={2} bpp={3,2} bytes={4,-7} offset={5,-7} payload={6}' -f `
            $dw, $dh, [BitConverter]::ToUInt16($bytes,$o+4), [BitConverter]::ToUInt16($bytes,$o+6), $len, $off, $payload
    }
    $ico = [System.Drawing.Icon]::new($Out, 32, 32)
    try { Write-Host "System.Drawing round-trip at 32: $($ico.Width)x$($ico.Height)" } finally { $ico.Dispose() }
}
