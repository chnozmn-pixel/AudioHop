<#
.SYNOPSIS
    Removes AudioHop for the current user.

.DESCRIPTION
    Stops AudioHop, removes the start-with-Windows entry, the Start Menu
    shortcut and the program folder. Settings in %APPDATA%\AudioHop are kept
    unless -RemoveSettings is given.

.PARAMETER InstallDir
    Where AudioHop was installed. Defaults to %LOCALAPPDATA%\Programs\AudioHop.

.PARAMETER RemoveSettings
    Also delete %APPDATA%\AudioHop (settings and any custom .lang files).

.EXAMPLE
    irm https://raw.githubusercontent.com/chnozmn-pixel/AudioHop/main/uninstall.ps1 | iex
#>
[CmdletBinding()]
param(
    [string] $InstallDir,
    [switch] $RemoveSettings
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$appName = 'AudioHop'

Write-Host ""
Write-Host "$appName uninstaller" -ForegroundColor Cyan
Write-Host ""

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$appName"
}

$running = Get-Process -Name $appName -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Write-Host "  Stopped the running copy."
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name $appName -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name $appName
    Write-Host "  Removed the start-with-Windows entry."
}

$shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$appName.lnk"
if (Test-Path -LiteralPath $shortcut) {
    Remove-Item -LiteralPath $shortcut -Force
    Write-Host "  Removed the Start Menu entry."
}

if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Host "  Removed $InstallDir."
} else {
    Write-Host "  Nothing installed at $InstallDir."
}

$settings = Join-Path $env:APPDATA $appName
if ($RemoveSettings) {
    if (Test-Path -LiteralPath $settings) {
        Remove-Item -LiteralPath $settings -Recurse -Force
        Write-Host "  Removed $settings."
    }
} elseif (Test-Path -LiteralPath $settings) {
    Write-Host "  Kept your settings in $settings (pass -RemoveSettings to delete them)."
}

Write-Host ""
Write-Host "$appName has been removed." -ForegroundColor Green
Write-Host ""
