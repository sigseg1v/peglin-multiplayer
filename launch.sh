#!/usr/bin/env bash
# Launch Peglin via Proton or Wine without Steam
# Adjust the paths below to match your system.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_DIR="$SCRIPT_DIR/release"
GAME_EXE="$GAME_DIR/Peglin.exe"

# --- Configuration (edit these) ---

# Option 1: Proton (recommended if you have Steam installed)
# Auto-detects the newest stable Proton. Override by setting PROTON_DIR before running.
if [ -z "${PROTON_DIR:-}" ]; then
    PROTON_DIR=$(
        find "$HOME/.steam/steam/steamapps/common/" -maxdepth 1 -name 'Proton *' -type d \
        | grep -v Experimental \
        | sort -t' ' -k2 -Vr \
        | head -n1
    )
    # Fall back to Experimental if no stable version found
    if [ -z "$PROTON_DIR" ]; then
        PROTON_DIR="$HOME/.steam/steam/steamapps/common/Proton - Experimental"
    fi
fi

# Wine prefix - reuse Peglin's existing Steam prefix (app ID 1296610)
# or point to a fresh directory and Proton will create one
COMPAT_DATA="$HOME/.steam/steam/steamapps/compatdata/1296610"

STEAM_DIR="$HOME/.steam/steam"

# Option 2: Plain Wine (uncomment USE_WINE=1 to use instead of Proton)
# USE_WINE=1

# --- End configuration ---

if [ ! -f "$GAME_EXE" ]; then
    echo "ERROR: Game executable not found at $GAME_EXE"
    exit 1
fi

if [ "${USE_WINE:-0}" = "1" ]; then
    echo "Launching Peglin via Wine..."
    cd "$GAME_DIR"
    exec wine "$GAME_EXE" "$@"
else
    if [ ! -f "$PROTON_DIR/proton" ]; then
        echo "ERROR: Proton not found at $PROTON_DIR/proton"
        echo "Available Proton versions:"
        ls "$HOME/.steam/steam/steamapps/common/" 2>/dev/null | grep -i proton || echo "  (none found)"
        echo ""
        echo "Edit PROTON_DIR in this script, or set USE_WINE=1 to use plain Wine."
        exit 1
    fi

    # Create compat data dir if it doesn't exist
    mkdir -p "$COMPAT_DATA"

    echo "Launching Peglin via Proton..."
    cd "$GAME_DIR"
    STEAM_COMPAT_DATA_PATH="$COMPAT_DATA" \
    STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM_DIR" \
    exec "$PROTON_DIR/proton" run "$GAME_EXE" "$@"
fi
