#!/usr/bin/env bash
# PeglinMods Installer
# Installs BepInEx 5 and PeglinMods.Core into a Peglin game directory.
# BepInEx and our plugin are bundled alongside this script - no downloads needed.
#
# Usage:
#   ./install.sh                     # Auto-detect game directory
#   ./install.sh /path/to/Peglin     # Specify game directory
#   ./install.sh --help

set -euo pipefail

BEPINEX_VERSION="5.4.23.2"
PLUGIN_DLL_NAME="PeglinMods.Core.dll"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# The distributable package layout is:
#   install/
#     install.sh         (this script)
#     uninstall.sh
#     bepinex/           (pinned BepInEx release, extracted)
#       winhttp.dll
#       doorstop_config.ini
#       BepInEx/
#         core/
#         ...
#     plugins/
#       PeglinMods/
#         PeglinMods.Core.dll

BEPINEX_BUNDLE="$SCRIPT_DIR/bepinex"
PLUGINS_BUNDLE="$SCRIPT_DIR/plugins"

# --- Color output ---
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }
step()  { echo -e "${CYAN}==>${NC} $*"; }

# --- Help ---
if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    echo "PeglinMods Installer"
    echo ""
    echo "Usage:"
    echo "  ./install.sh                     Auto-detect Peglin game directory"
    echo "  ./install.sh /path/to/Peglin     Use specified game directory"
    echo "  ./install.sh --help              Show this help"
    echo ""
    echo "This script will:"
    echo "  1. Install BepInEx ${BEPINEX_VERSION} (mod loader framework)"
    echo "  2. Install PeglinMods.Core (crash reporter disable + mod infrastructure)"
    echo ""
    echo "To uninstall, run: ./uninstall.sh /path/to/Peglin"
    exit 0
fi

# --- Validate bundle ---
if [ ! -d "$BEPINEX_BUNDLE" ] || [ ! -f "$BEPINEX_BUNDLE/winhttp.dll" ]; then
    error "BepInEx bundle not found at: $BEPINEX_BUNDLE"
    error "Run ./build.sh --package first to create the distributable package."
    exit 1
fi

if [ ! -d "$PLUGINS_BUNDLE" ] || [ ! -f "$PLUGINS_BUNDLE/PeglinMods/$PLUGIN_DLL_NAME" ]; then
    error "Plugin bundle not found at: $PLUGINS_BUNDLE/PeglinMods/$PLUGIN_DLL_NAME"
    error "Run ./build.sh --package first to create the distributable package."
    exit 1
fi

# --- Locate game directory ---
find_game_dir() {
    local search_paths=(
        "$HOME/.steam/steam/steamapps/common/Peglin"
        "$HOME/.local/share/Steam/steamapps/common/Peglin"
        "$HOME/.steam/root/steamapps/common/Peglin"
        "/opt/steam/steamapps/common/Peglin"
    )

    # Check additional Steam library folders from libraryfolders.vdf
    local vdf="$HOME/.steam/steam/steamapps/libraryfolders.vdf"
    if [ -f "$vdf" ]; then
        while IFS= read -r line; do
            local libpath
            libpath=$(echo "$line" | grep -oP '"path"\s+"\K[^"]+' 2>/dev/null || true)
            if [ -n "$libpath" ]; then
                search_paths+=("$libpath/steamapps/common/Peglin")
            fi
        done < "$vdf"
    fi

    for path in "${search_paths[@]}"; do
        if [ -f "$path/Peglin.exe" ]; then
            echo "$path"
            return 0
        fi
    done
    return 1
}

GAME_DIR="${1:-}"

if [ -z "$GAME_DIR" ]; then
    step "Auto-detecting Peglin installation..."
    if GAME_DIR=$(find_game_dir); then
        info "Found Peglin at: $GAME_DIR"
    else
        error "Could not auto-detect Peglin installation."
        echo "Please provide the path to your Peglin game directory:"
        echo "  ./install.sh /path/to/Peglin"
        echo ""
        echo "The directory should contain Peglin.exe"
        exit 1
    fi
fi

# Validate game directory
if [ ! -f "$GAME_DIR/Peglin.exe" ]; then
    error "Peglin.exe not found in: $GAME_DIR"
    error "Please provide the correct path to your Peglin installation."
    exit 1
fi

if [ ! -d "$GAME_DIR/Peglin_Data/Managed" ]; then
    error "Peglin_Data/Managed not found. Is this a valid Peglin installation?"
    exit 1
fi

echo ""
echo "========================================="
echo "  PeglinMods Installer"
echo "========================================="
echo ""
info "Game directory: $GAME_DIR"
echo ""

# --- Install BepInEx ---
step "Installing BepInEx ${BEPINEX_VERSION}..."

if [ -f "$GAME_DIR/winhttp.dll" ] && [ -d "$GAME_DIR/BepInEx/core" ]; then
    info "BepInEx already installed - updating files"
fi

# Copy BepInEx framework files (doorstop dll + config at game root, BepInEx/ tree)
cp "$BEPINEX_BUNDLE/winhttp.dll" "$GAME_DIR/"
cp "$BEPINEX_BUNDLE/doorstop_config.ini" "$GAME_DIR/"
cp -r "$BEPINEX_BUNDLE/BepInEx/core" "$GAME_DIR/BepInEx/core"

# Create directories BepInEx expects
mkdir -p "$GAME_DIR/BepInEx/plugins"
mkdir -p "$GAME_DIR/BepInEx/patchers"
mkdir -p "$GAME_DIR/BepInEx/config"

info "BepInEx ${BEPINEX_VERSION} installed"

# --- Install PeglinMods ---
step "Installing PeglinMods.Core..."

cp -r "$PLUGINS_BUNDLE/PeglinMods" "$GAME_DIR/BepInEx/plugins/"
info "Copied PeglinMods to BepInEx/plugins/"

# --- Configure BepInEx ---
BEPINEX_CFG="$GAME_DIR/BepInEx/config/BepInEx.cfg"
if [ ! -f "$BEPINEX_CFG" ]; then
    cat > "$BEPINEX_CFG" << 'BEPINEX_CONFIG'
[Logging.Console]
## Enable the BepInEx console (useful for debugging mods)
Enabled = true

[Logging.Disk]
## Log to file
Enabled = true

[Preloader.Entrypoint]
Assembly = UnityEngine.CoreModule.dll
Type = Application
Method = .cctor
BEPINEX_CONFIG
    info "Created default BepInEx.cfg with console enabled"
fi

# --- Done ---
echo ""
echo "========================================="
echo "  Installation Complete!"
echo "========================================="
echo ""
info "BepInEx ${BEPINEX_VERSION} + PeglinMods.Core installed to:"
info "  $GAME_DIR"
echo ""
echo "If launching via Proton/Wine, you may need to set:"
echo "  WINEDLLOVERRIDES=\"winhttp=n,b\" before launching"
echo ""
echo "  For Steam: Right-click Peglin > Properties > Launch Options:"
echo "    WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"
echo ""
echo "  For manual launch with the included launch.sh:"
echo "    The script handles this automatically."
echo ""
echo "To uninstall: ./uninstall.sh $GAME_DIR"
echo ""
