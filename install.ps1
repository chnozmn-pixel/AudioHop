<#
.SYNOPSIS
    Installs AudioHop for the current user.

.DESCRIPTION
    Downloads the latest AudioHop release, puts it in
    %LOCALAPPDATA%\Programs\AudioHop, adds a Start Menu shortcut and starts it.
    No administrator rights are needed and nothing is written outside the
    current user's profile.

.PARAMETER Version
    Release tag to install, for example v1.0.0. Defaults to the latest release.

.PARAMETER InstallDir
    Where to put AudioHop.exe. Defaults to %LOCALAPPDATA%\Programs\AudioHop.

.PARAMETER AutoStart
    Also register AudioHop to start with Windows.

.PARAMETER NoShortcut
    Skip the Start Menu shortcut.

.PARAMETER NoLaunch
    Install but do not start the app.

.EXAMPLE
    irm https://raw.githubusercontent.com/chnozmn-pixel/AudioHop/main/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/chnozmn-pixel/AudioHop/main/install.ps1))) -AutoStart
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $InstallDir,
    [switch] $AutoStart,
    [switch] $NoShortcut,
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo    = 'chnozmn-pixel/AudioHop'
$appName = 'AudioHop'
$exeName = 'AudioHop.exe'

function Write-Step { param([string] $Message) Write-Host "  $Message" }

Write-Host ""
Write-Host "$appName installer" -ForegroundColor Cyan
Write-Host ""

# TLS 1.2 for Windows PowerShell 5.1, whose default is still TLS 1.0.
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
}
$target = Join-Path $InstallDir $exeName

# --- Work out which release to fetch -----------------------------------------

$api = if ($Version) {
    "https://api.github.com/repos/$repo/releases/tags/$Version"
} else {
    "https://api.github.com/repos/$repo/releases/latest"
}

Write-Step 'Looking up the release...'
try {
    $release = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = "$appName-installer" }
} catch {
    throw "Could not reach the GitHub release API ($api): $($_.Exception.Message)"
}

$asset = $release.assets | Where-Object { $_.name -eq $exeName } | Select-Object -First 1
if (-not $asset) {
    $asset = $release.assets | Where-Object { $_.name -like '*.exe' } | Select-Object -First 1
}
if (-not $asset) {
    throw "Release $($release.tag_name) has no .exe asset to install."
}

Write-Step "Release $($release.tag_name), $([math]::Round($asset.size / 1KB)) KB"

# --- Stop a running copy so the file can be replaced -------------------------

$running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue
if ($running) {
    Write-Step 'Closing the running copy...'
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

# --- Download ----------------------------------------------------------------

if (-not (Test-Path -LiteralPath $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$temp = Join-Path $InstallDir "$exeName.download"
Write-Step "Downloading to $InstallDir ..."
try {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp -UseBasicParsing
} catch {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    throw "Download failed: $($_.Exception.Message)"
}
Move-Item -LiteralPath $temp -Destination $target -Force

# Clear the "downloaded from the internet" mark so Windows does not nag.
try { Unblock-File -LiteralPath $target } catch { }

# --- Start Menu shortcut -----------------------------------------------------

$shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$appName.lnk"
if ($NoShortcut) {
    Write-Step 'Skipping the Start Menu shortcut.'
} else {
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($shortcut)
        $link.TargetPath       = $target
        $link.WorkingDirectory = $InstallDir
        $link.Description      = 'Switch the Windows sound device'
        $link.Save()
        Write-Step 'Added a Start Menu entry.'
    } catch {
        Write-Warning "Could not create the Start Menu shortcut: $($_.Exception.Message)"
    }
}

# --- Start with Windows ------------------------------------------------------

if ($AutoStart) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $runKey -Name $appName -Value "`"$target`""
    Write-Step 'Registered to start with Windows.'
}

# --- Launch ------------------------------------------------------------------

if (-not $NoLaunch) {
    Start-Process -FilePath $target -WorkingDirectory $InstallDir
    Write-Step 'Started. Look for the tray icon near the clock.'
}

Write-Host ""
Write-Host "Installed $appName $($release.tag_name) to $target" -ForegroundColor Green
Write-Host "First run opens a short setup wizard - no hotkey is assigned until you pick one."
Write-Host "Uninstall: irm https://raw.githubusercontent.com/$repo/main/uninstall.ps1 | iex"
Write-Host ""
