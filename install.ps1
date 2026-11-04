# G-Helper Custom - one-step installer
# Copies the prebuilt GHelper.exe to %LOCALAPPDATA%\GHelper and starts it.
# Requires .NET 10 Desktop Runtime (the script installs it via winget if missing).

$ErrorActionPreference = "Stop"

$src    = Join-Path $PSScriptRoot "dist\GHelper.exe"
$dstDir = Join-Path $env:LOCALAPPDATA "GHelper"
$dst    = Join-Path $dstDir "GHelper.exe"

if (-not (Test-Path $src)) {
    Write-Host "[x] dist\GHelper.exe not found. Run this script from the repository root." -ForegroundColor Red
    exit 1
}

# 1. .NET 10 Desktop Runtime
$hasRuntime = Get-ChildItem "C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App" -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -match "^10\." }

if (-not $hasRuntime) {
    Write-Host "[!] .NET 10 Desktop Runtime not found, installing via winget..." -ForegroundColor Yellow
    winget install --id Microsoft.DotNet.DesktopRuntime.10 --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[x] Install failed. Download manually: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[+] .NET 10 Desktop Runtime: $($hasRuntime.Name)" -ForegroundColor Green
}

# 2. Stop running instance
$running = Get-Process GHelper -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running G-Helper (PID $($running.Id))..." -ForegroundColor Yellow
    try { $running | Stop-Process -Force -ErrorAction Stop } catch {
        Write-Host "[!] Could not stop it (needs admin). Close G-Helper from the tray, then re-run." -ForegroundColor Red
        exit 1
    }
    Start-Sleep -Seconds 2
}

# 3. Install
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
Copy-Item $src $dst -Force
Write-Host "[+] Installed to $dst" -ForegroundColor Green

# 4. Start
Start-Process $dst
Write-Host "[+] Started. Tick 'Run on startup' at the bottom of the main window to register autostart." -ForegroundColor Green
Write-Host ""
Write-Host "Added features (tick the checkboxes at the bottom of the main window):" -ForegroundColor Cyan
Write-Host "  - Lock screen silent : switch to Silent mode on Win+L, restore on unlock"
Write-Host "  - Dual fan link      : GPU fan follows the CPU fan speed"
