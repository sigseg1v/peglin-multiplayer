#!/usr/bin/env bash
# PeglinMods Uninstaller
# Cleanly removes BepInEx and PeglinMods from a Peglin installation.
#
# Usage:
#   ./uninstall.sh /path/to/Peglin
#   ./uninstall.sh --mods-only /path/to/Peglin    # Remove only PeglinMods, keep BepInEx

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }
step()  { echo -e "${CYAN}==>${NC} $*"; }

MODS_ONLY=false
GAME_DIR=""

for arg in "$@"; do
    case "$arg" in
        --mods-only) MODS_ONLY=true ;;
        --help|-h)
            echo "PeglinMods Uninstaller"
            echo ""
            echo "Usage:"
            echo "  ./uninstall.sh /path/to/Peglin              Remove BepInEx + all mods"
            echo "  ./uninstall.sh --mods-only /path/to/Peglin  Remove only PeglinMods"
            exit 0
            ;;
        *) GAME_DIR="$arg" ;;
    esac
done

if [ -z "$GAME_DIR" ]; then
    error "Please provide the game directory:"
    echo "  ./uninstall.sh /path/to/Peglin"
    exit 1
fi

if [ ! -f "$GAME_DIR/Peglin.exe" ]; then
    error "Peglin.exe not found in: $GAME_DIR"
    exit 1
fi

echo ""
echo "========================================="
echo "  PeglinMods Uninstaller"
echo "========================================="
echo ""

if $MODS_ONLY; then
    step "Removing PeglinMods only (keeping BepInEx)..."
    rm -rf "$GAME_DIR/BepInEx/plugins/PeglinMods"
    info "Removed BepInEx/plugins/PeglinMods/"
else
    step "Removing BepInEx and all mods..."

    # BepInEx files
    rm -f "$GAME_DIR/winhttp.dll"
    rm -f "$GAME_DIR/doorstop_config.ini"
    rm -f "$GAME_DIR/.doorstop_version"
    rm -rf "$GAME_DIR/BepInEx"

    info "Removed BepInEx framework and all plugins"
fi

# Restore crash handler if we disabled it
CRASH_DISABLED="$GAME_DIR/UnityCrashHandler64.exe.disabled_by_mods"
CRASH_ORIGINAL="$GAME_DIR/UnityCrashHandler64.exe"
if [ -f "$CRASH_DISABLED" ] && [ ! -f "$CRASH_ORIGINAL" ]; then
    mv "$CRASH_DISABLED" "$CRASH_ORIGINAL"
    info "Restored crash handler"
fi

# Also check for launch.sh disabled version
CRASH_DISABLED2="$GAME_DIR/UnityCrashHandler64.exe.disabled"
if [ -f "$CRASH_DISABLED2" ] && [ ! -f "$CRASH_ORIGINAL" ]; then
    mv "$CRASH_DISABLED2" "$CRASH_ORIGINAL"
    info "Restored crash handler"
fi

echo ""
info "Uninstall complete. Game is back to vanilla state."
echo ""
