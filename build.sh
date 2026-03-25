#!/usr/bin/env bash
# Build PeglinMods and optionally create a distributable package.
#
# Usage:
#   ./build.sh                  Build plugin (Release)
#   ./build.sh Debug            Build plugin (Debug)
#   ./build.sh --package        Build + create distributable package in dist/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$SCRIPT_DIR/src"
BUILD_DIR="$SCRIPT_DIR/build"

# Pinned BepInEx version - update this when upgrading
BEPINEX_VERSION="5.4.23.2"
BEPINEX_ZIP_NAME="BepInEx_win_x64_${BEPINEX_VERSION}.zip"
BEPINEX_CACHE="$SCRIPT_DIR/vendor/$BEPINEX_ZIP_NAME"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/$BEPINEX_ZIP_NAME"

PACKAGE=false
CONFIG="Release"

for arg in "$@"; do
    case "$arg" in
        --package) PACKAGE=true ;;
        Debug|Release) CONFIG="$arg" ;;
    esac
done

# --- Build ---
echo "Building PeglinMods ($CONFIG)..."
dotnet build "$SRC_DIR/PeglinMods.sln" -c "$CONFIG" --nologo

mkdir -p "$BUILD_DIR"
cp "$SRC_DIR/PeglinMods.Core/bin/$CONFIG/net462/PeglinMods.Core.dll" "$BUILD_DIR/"

echo ""
echo "Build output: $BUILD_DIR/"
ls -la "$BUILD_DIR/PeglinMods.Core.dll"

if ! $PACKAGE; then
    echo ""
    echo "To create a distributable package: ./build.sh --package"
    exit 0
fi

# --- Package ---
echo ""
echo "Creating distributable package..."

DIST_DIR="$SCRIPT_DIR/dist/peglinmods"
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

# Download BepInEx if not cached
mkdir -p "$SCRIPT_DIR/vendor"
if [ ! -f "$BEPINEX_CACHE" ]; then
    echo "Downloading BepInEx ${BEPINEX_VERSION}..."
    curl -fSL --progress-bar "$BEPINEX_URL" -o "$BEPINEX_CACHE"
fi

# Extract BepInEx into the package's bepinex/ directory
TMPDIR=$(mktemp -d)
trap 'rm -rf "$TMPDIR"' EXIT
unzip -qo "$BEPINEX_CACHE" -d "$TMPDIR/bepinex"

mkdir -p "$DIST_DIR/bepinex/BepInEx"
cp "$TMPDIR/bepinex/winhttp.dll" "$DIST_DIR/bepinex/"
cp "$TMPDIR/bepinex/doorstop_config.ini" "$DIST_DIR/bepinex/"
cp -r "$TMPDIR/bepinex/BepInEx/core" "$DIST_DIR/bepinex/BepInEx/core"

# Bundle our plugin
mkdir -p "$DIST_DIR/plugins/PeglinMods"
cp "$BUILD_DIR/PeglinMods.Core.dll" "$DIST_DIR/plugins/PeglinMods/"

# Bundle install/uninstall scripts
cp "$SCRIPT_DIR/install/install.sh" "$DIST_DIR/"
cp "$SCRIPT_DIR/install/uninstall.sh" "$DIST_DIR/"
chmod +x "$DIST_DIR/install.sh" "$DIST_DIR/uninstall.sh"

# Create the distributable archive
ARCHIVE="$SCRIPT_DIR/dist/peglinmods-$(date +%Y%m%d).tar.gz"
tar -czf "$ARCHIVE" -C "$SCRIPT_DIR/dist" peglinmods/

echo ""
echo "Distributable package created:"
echo "  $ARCHIVE"
echo ""
echo "Contents:"
find "$DIST_DIR" -type f | sort | sed "s|$DIST_DIR/|  |"
echo ""
echo "Users extract this and run: ./install.sh /path/to/Peglin"
