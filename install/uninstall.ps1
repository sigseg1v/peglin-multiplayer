# PeglinMods Uninstaller
# Cleanly removes BepInEx and PeglinMods from a Peglin installation.
#
# Usage:
#   ./uninstall.ps1 /path/to/Peglin
#   ./uninstall.ps1 -ModsOnly /path/to/Peglin
param(
    [Parameter(Position=0)]
    [string]$GameDir,
    [switch]$ModsOnly,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Info($msg)  { Write-Host "[INFO] $msg" -ForegroundColor Green }
function Write-Err($msg)   { Write-Host "[ERROR] $msg" -ForegroundColor Red }
function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }

if ($Help) {
    Write-Host "PeglinMods Uninstaller"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  ./uninstall.ps1 /path/to/Peglin              Remove BepInEx + all mods"
    Write-Host "  ./uninstall.ps1 -ModsOnly /path/to/Peglin    Remove only PeglinMods"
    exit 0
}

if (-not $GameDir) {
    Write-Err "Please provide the game directory: ./uninstall.ps1 /path/to/Peglin"
    exit 1
}

if (-not (Test-Path (Join-Path $GameDir "Peglin.exe"))) {
    Write-Err "Peglin.exe not found in: $GameDir"
    exit 1
}

Write-Host ""
Write-Host "========================================="
Write-Host "  PeglinMods Uninstaller"
Write-Host "========================================="
Write-Host ""

if ($ModsOnly) {
    Write-Step "Removing PeglinMods only (keeping BepInEx)..."
    $ModDir = Join-Path $GameDir "BepInEx/plugins/PeglinMods"
    if (Test-Path $ModDir) { Remove-Item $ModDir -Recurse -Force }
    Write-Info "Removed BepInEx/plugins/PeglinMods/"
} else {
    Write-Step "Removing BepInEx and all mods..."
    foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) {
        $p = Join-Path $GameDir $f
        if (Test-Path $p) { Remove-Item $p -Force }
    }
    $BepDir = Join-Path $GameDir "BepInEx"
    if (Test-Path $BepDir) { Remove-Item $BepDir -Recurse -Force }
    Write-Info "Removed BepInEx framework and all plugins"
}

# Restore crash handler
foreach ($suffix in @(".disabled_by_mods", ".disabled")) {
    $disabled = Join-Path $GameDir "UnityCrashHandler64.exe$suffix"
    $original = Join-Path $GameDir "UnityCrashHandler64.exe"
    if ((Test-Path $disabled) -and -not (Test-Path $original)) {
        Move-Item $disabled $original
        Write-Info "Restored crash handler"
        break
    }
}

Write-Host ""
Write-Info "Uninstall complete. Game is back to vanilla state."
