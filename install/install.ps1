# Multipeglin Installer
# Installs BepInEx 5 and Multipeglin into a Peglin game directory.
# BepInEx and our plugin are bundled alongside this script.
#
# Usage:
#   ./install.ps1                     # Auto-detect game directory
#   ./install.ps1 /path/to/Peglin     # Specify game directory
#   ./install.ps1 -Help
param(
    [Parameter(Position=0)]
    [string]$GameDir,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$BepInExVersion = "5.4.23.2"
$PluginDllName = "Multipeglin.Core.dll"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$BepInExBundle = Join-Path $ScriptDir "bepinex"
$PluginsBundle = Join-Path $ScriptDir "plugins"

function Write-Info($msg)  { Write-Host "[INFO] $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "[ERROR] $msg" -ForegroundColor Red }
function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }

if ($Help) {
    Write-Host "Multipeglin Installer"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  ./install.ps1                     Auto-detect Peglin game directory"
    Write-Host "  ./install.ps1 /path/to/Peglin     Use specified game directory"
    Write-Host ""
    Write-Host "This script will:"
    Write-Host "  1. Install BepInEx $BepInExVersion (mod loader framework)"
    Write-Host "  2. Install Multipeglin (cooperative multiplayer mod)"
    Write-Host ""
    Write-Host "To uninstall, run: ./uninstall.ps1 /path/to/Peglin"
    exit 0
}

# --- Validate bundle ---
if (-not (Test-Path (Join-Path $BepInExBundle "winhttp.dll"))) {
    Write-Err "BepInEx bundle not found at: $BepInExBundle"
    Write-Err "Run 'just publish --package' first to create the distributable package."
    exit 1
}

if (-not (Test-Path (Join-Path $PluginsBundle "Multipeglin/$PluginDllName"))) {
    Write-Err "Plugin bundle not found at: $PluginsBundle/Multipeglin/$PluginDllName"
    Write-Err "Run 'just publish --package' first to create the distributable package."
    exit 1
}

# --- Locate game directory ---
function Find-GameDir {
    $SearchPaths = @(
        "$HOME/.steam/steam/steamapps/common/Peglin",
        "$HOME/.local/share/Steam/steamapps/common/Peglin",
        "$env:ProgramFiles/Steam/steamapps/common/Peglin",
        "${env:ProgramFiles(x86)}/Steam/steamapps/common/Peglin",
        "C:/Program Files/Steam/steamapps/common/Peglin",
        "C:/Program Files (x86)/Steam/steamapps/common/Peglin"
    )
    foreach ($path in $SearchPaths) {
        if ($path -and (Test-Path (Join-Path $path "Peglin.exe"))) {
            return $path
        }
    }
    return $null
}

if (-not $GameDir) {
    Write-Step "Auto-detecting Peglin installation..."
    $GameDir = Find-GameDir
    if ($GameDir) {
        Write-Info "Found Peglin at: $GameDir"
    } else {
        Write-Err "Could not auto-detect Peglin installation."
        Write-Host "Please provide the path: ./install.ps1 /path/to/Peglin"
        exit 1
    }
}

if (-not (Test-Path (Join-Path $GameDir "Peglin.exe"))) {
    Write-Err "Peglin.exe not found in: $GameDir"
    exit 1
}

Write-Host ""
Write-Host "========================================="
Write-Host "  Multipeglin Installer"
Write-Host "========================================="
Write-Host ""
Write-Info "Game directory: $GameDir"
Write-Host ""

# --- Install BepInEx ---
Write-Step "Installing BepInEx $BepInExVersion..."

Copy-Item (Join-Path $BepInExBundle "winhttp.dll") $GameDir -Force
Copy-Item (Join-Path $BepInExBundle "doorstop_config.ini") $GameDir -Force
$CoreDest = Join-Path $GameDir "BepInEx/core"
New-Item -ItemType Directory -Path $CoreDest -Force | Out-Null
Copy-Item (Join-Path $BepInExBundle "BepInEx/core/*") $CoreDest -Recurse -Force

foreach ($dir in @("plugins", "patchers", "config")) {
    New-Item -ItemType Directory -Path (Join-Path $GameDir "BepInEx/$dir") -Force | Out-Null
}

Write-Info "BepInEx $BepInExVersion installed"

# --- Install Multipeglin ---
Write-Step "Installing Multipeglin..."
$PluginDest = Join-Path $GameDir "BepInEx/plugins/Multipeglin"
New-Item -ItemType Directory -Path $PluginDest -Force | Out-Null
Copy-Item (Join-Path $PluginsBundle "Multipeglin/*") $PluginDest -Recurse -Force
Write-Info "Copied Multipeglin to BepInEx/plugins/"

# --- Configure BepInEx ---
$CfgPath = Join-Path $GameDir "BepInEx/config/BepInEx.cfg"
if (-not (Test-Path $CfgPath)) {
    @"
[Logging.Console]
## Disable the BepInEx console window - logs go to BepInEx/logs/ instead
Enabled = false

[Logging.Disk]
## Log to file
Enabled = true

[Preloader.Entrypoint]
Assembly = UnityEngine.CoreModule.dll
Type = Application
Method = .cctor
"@ | Set-Content $CfgPath
    Write-Info "Created default BepInEx.cfg"
}

# --- Done ---
Write-Host ""
Write-Host "========================================="
Write-Host "  Installation Complete!"
Write-Host "========================================="
Write-Host ""
Write-Info "BepInEx $BepInExVersion + Multipeglin installed to:"
Write-Info "  $GameDir"
Write-Host ""

if ($IsLinux) {
    Write-Host "If launching via Proton/Wine, set Steam launch options:"
    Write-Host '  WINEDLLOVERRIDES="winhttp=n,b" %command%'
    Write-Host ""
}

Write-Host "To uninstall: ./uninstall.ps1 $GameDir"
