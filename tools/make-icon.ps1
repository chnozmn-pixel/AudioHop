<#
.SYNOPSIS
    Builds the .ico files AudioHop ships with, from the SVG artwork.

.DESCRIPTION
    Rasterizes an SVG with headless Edge, then packs the sizes Windows asks for
    into one .ico: 16-128 as 32-bit BGRA bitmaps and 256 as an embedded PNG,
    which is what every Windows version since Vista expects.

    With no arguments it builds both icons:

      assets\icon.svg -> assets\AudioHop.ico   the full tile, for the
                                               executable and the windows
      assets\tray.svg -> assets\tray.ico       just the switch, which stays
                                               readable at 16 px in the tray

    Run it after changing the artwork; build.cmd only consumes the .ico files.

.PARAMETER Sizes
    Icon sizes to pack. Defaults to 16 through 256.

.EXAMPLE
    .\tools\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $Svg,
    [string] $Out,
    [int[]]  $Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

# No arguments: build every icon the app ships with.
if (-not $Svg -and -not $Out) {
    & $PSCommandPath -Svg (Join-Path $root 'assets\icon.svg') -Out (Join-Path $root 'assets\AudioHop.ico')
    # The tray never asks for more than 48 px, so stop there and keep the
    # embedded resource small.
    & $PSCommandPath -Svg (Join-Path $root 'assets\tray.svg') -Out (Join-Path $root 'assets\tray.ico') `
        -Sizes 16, 20, 24, 32, 40, 48
    return
}

if (-not $Svg) { $Svg = Join-Path $root 'assets\icon.svg' }
if (-not $Out) { $Out = Join-Path $root 'assets\AudioHop.ico' }

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is needed to rasterize the SVG.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("audiohop-icon-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # --- rasterize once at 512 and scale down from there --------------------
    Copy-Item -LiteralPath $Svg -Destination (Join-Path $work 'icon.svg')
    $page = Join-Path $work 'render.html'
    @'
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;background:transparent}img{display:block;width:512px;height:512px}</style>
<img src="icon.svg">
'@ | Set-Content -LiteralPath $page -Encoding UTF8

    $big = Join-Path $work 'icon512.png'
    # Edge chatters on stderr even when it succeeds, so don't let that abort us.
    $quiet = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $edge --headless --disable-gpu --allow-file-access-from-files --hide-scrollbars `
        --default-background-color=00000000 --force-device-scale-factor=1 `
        --virtual-time-budget=4000 --window-size=512,512 --screenshot="$big" `
        ("file:///" + $page.Replace('\', '/')) 2>&1 | Out-Null
    $ErrorActionPreference = $quiet
    if (-not (Test-Path -LiteralPath $big)) { throw 'Edge produced no PNG.' }

    $source = [Drawing.Bitmap]::FromFile($big)

    $sizes  = $Sizes
    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode  = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode      = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([Drawing.Color]::Transparent)
        $g.DrawImage($source, (New-Object Drawing.Rectangle 0, 0, $size, $size))
        $g.Dispose()
        $frames += ,@($size, $bmp)
    }

    function Get-IconFrameBytes {
        param([int] $Size, [Drawing.Bitmap] $Bitmap)

        if ($Size -ge 256) {
            # PNG-compressed entry: smaller, and what Windows expects at 256.
            $ms = New-Object IO.MemoryStream
            $Bitmap.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
            return $ms.ToArray()
        }

        # 32-bit BGRA DIB: BITMAPINFOHEADER, bottom-up pixels, empty AND mask.
        $stride   = $Size * 4
        $maskRow  = [math]::Ceiling($Size / 32) * 4
        $pixels   = New-Object byte[] ($stride * $Size)
        $data = $Bitmap.LockBits(
            (New-Object Drawing.Rectangle 0, 0, $Size, $Size),
            [Drawing.Imaging.ImageLockMode]::ReadOnly,
            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for ($y = 0; $y -lt $Size; $y++) {
                $src = [IntPtr]::Add($data.Scan0, $data.Stride * ($Size - 1 - $y))
                [Runtime.InteropServices.Marshal]::Copy($src, $pixels, $y * $stride, $stride)
            }
        } finally {
            $Bitmap.UnlockBits($data)
        }

        $ms = New-Object IO.MemoryStream
        $w  = New-Object IO.BinaryWriter $ms
        $w.Write([uint32] 40)          # biSize
        $w.Write([int32]  $Size)       # biWidth
        $w.Write([int32]  ($Size * 2)) # biHeight: colour + mask
        $w.Write([uint16] 1)           # biPlanes
        $w.Write([uint16] 32)          # biBitCount
        $w.Write([uint32] 0)           # biCompression = BI_RGB
        $w.Write([uint32] ($stride * $Size + $maskRow * $Size))
        $w.Write([int32] 0); $w.Write([int32] 0)
        $w.Write([uint32] 0); $w.Write([uint32] 0)
        $w.Write($pixels)
        $w.Write((New-Object byte[] ($maskRow * $Size)))
        $w.Flush()
        return $ms.ToArray()
    }

    $bodies = @()
    foreach ($frame in $frames) { $bodies += ,([byte[]] (Get-IconFrameBytes -Size $frame[0] -Bitmap $frame[1])) }

    $ico    = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($ico)
    $writer.Write([uint16] 0); $writer.Write([uint16] 1); $writer.Write([uint16] $frames.Count)

    $offset = 6 + 16 * $frames.Count
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $size = $frames[$i][0]
        $dim  = [byte] $(if ($size -ge 256) { 0 } else { $size })
        $writer.Write($dim)
        $writer.Write($dim)
        $writer.Write([byte] 0)                # palette colours
        $writer.Write([byte] 0)                # reserved
        $writer.Write([uint16] 1)              # planes
        $writer.Write([uint16] 32)             # bit count
        $writer.Write([uint32] $bodies[$i].Length)
        $writer.Write([uint32] $offset)
        $offset += $bodies[$i].Length
    }
    foreach ($body in $bodies) { $writer.Write([byte[]] $body, 0, $body.Length) }
    $writer.Flush()

    [IO.File]::WriteAllBytes($Out, $ico.ToArray())

    foreach ($frame in $frames) { $frame[1].Dispose() }
    $source.Dispose()

    Write-Host ("Wrote {0} ({1:N0} bytes, sizes: {2})" -f $Out, (Get-Item -LiteralPath $Out).Length, ($sizes -join ', '))
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
