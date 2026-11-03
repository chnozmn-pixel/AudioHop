<#
.SYNOPSIS
    Prepares an AudioHop release: rebuilds the executable and stamps the
    version, hash and date into the Scoop and winget manifests.

.DESCRIPTION
    Run this before tagging a release. It builds AudioHop.exe with build.cmd,
    computes its SHA256 and rewrites packaging/scoop/audiohop.json and the
    three packaging/winget manifests to match. It touches nothing outside this
    repository and publishes nothing - tagging and uploading stay manual.

.PARAMETER Version
    The version to stamp, without the leading v, for example 1.0.1.

.PARAMETER SkipBuild
    Use the AudioHop.exe that is already there instead of rebuilding.

.EXAMPLE
    .\release.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

Write-Host ""
Write-Host "Preparing AudioHop $Version" -ForegroundColor Cyan
Write-Host ""

if (-not $SkipBuild) {
    Write-Host "  Building..."
    & (Join-Path $root 'build.cmd')
    if ($LASTEXITCODE -ne 0) { throw "build.cmd failed with exit code $LASTEXITCODE." }
}

$exe = Join-Path $root 'AudioHop.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "AudioHop.exe is missing. Run build.cmd first." }

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToUpperInvariant()
$size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1KB)
$date = (Get-Date).ToString('yyyy-MM-dd')

Write-Host "  AudioHop.exe  $size KB"
Write-Host "  SHA256        $hash"
Write-Host ""

function Edit-Manifest {
    param(
        [string] $Path,
        [scriptblock] $Transform
    )
    $full = Join-Path $root $Path
    $text = Get-Content -LiteralPath $full -Raw
    $new  = & $Transform $text
    if ($new -ne $text) {
        # No BOM: winget and Scoop both want plain UTF-8.
        [IO.File]::WriteAllText($full, $new, (New-Object Text.UTF8Encoding $false))
        Write-Host "  Updated $Path"
    } else {
        Write-Host "  $Path already current"
    }
}

Edit-Manifest 'packaging/scoop/audiohop.json' {
    param([string] $t)
    $t = $t -replace '"version": "[^"]*"', "`"version`": `"$Version`""
    $t = $t -replace '/download/v[^/]+/AudioHop\.exe', "/download/v$Version/AudioHop.exe"
    $t -replace '"hash": "[^"]*"', "`"hash`": `"$($hash.ToLowerInvariant())`""
}

foreach ($name in @(
    'packaging/winget/chnozmn-pixel.AudioHop.yaml',
    'packaging/winget/chnozmn-pixel.AudioHop.locale.en-US.yaml',
    'packaging/winget/chnozmn-pixel.AudioHop.installer.yaml'
)) {
    Edit-Manifest $name {
        param([string] $t)
        $t = $t -replace 'PackageVersion: .*', "PackageVersion: $Version"
        $t = $t -replace '/download/v[^/]+/AudioHop\.exe', "/download/v$Version/AudioHop.exe"
        $t = $t -replace 'InstallerSha256: .*', "InstallerSha256: $hash"
        $t = $t -replace 'ReleaseDate: .*', "ReleaseDate: $date"
        $t -replace '/releases/tag/v[\d.]+', "/releases/tag/v$Version"
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Green
Write-Host "  1. Note the changes for $Version in CHANGELOG.md."
Write-Host "  2. git commit -am `"Release v$Version`" && git tag v$Version"
Write-Host "  3. Create the GitHub release for v$Version and attach AudioHop.exe."
Write-Host "  4. Check: irm https://raw.githubusercontent.com/chnozmn-pixel/AudioHop/main/install.ps1 | iex"
Write-Host ""
