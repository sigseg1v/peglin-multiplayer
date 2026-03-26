# PeglinMods development commands

root := justfile_directory()
src := root / "src"
game := root / "release"
plugins := game / "BepInEx" / "plugins" / "PeglinMods"
logfile := game / "BepInEx" / "logs" / "peglinmods_dev.log"

bepinex_version := "5.4.23.2"
bepinex_zip := "BepInEx_win_x64_" + bepinex_version + ".zip"
bepinex_url := "https://github.com/BepInEx/BepInEx/releases/download/v" + bepinex_version + "/" + bepinex_zip
bepinex_cache := root / "vendor" / bepinex_zip

# Build debug
build:
    dotnet build {{src}}/PeglinMods.sln -c Debug --nologo

# Build release and copy to build/
publish:
    dotnet build {{src}}/PeglinMods.sln -c Release --nologo
    mkdir -p {{root}}/build
    cp {{src}}/PeglinMods.Core/bin/Release/net462/PeglinMods.Core.dll {{root}}/build/
    cp {{src}}/PeglinMods.Spectator/bin/Release/net462/PeglinMods.Spectator.dll {{root}}/build/
    cp {{src}}/PeglinMods.Spectator/bin/Release/net462/LiteNetLib.dll {{root}}/build/
    cp {{src}}/PeglinMods.Spectator/bin/Release/net462/System.Text.Json.dll {{root}}/build/
    @echo ""
    @echo "Publish output:"
    @ls -la {{root}}/build/*.dll

# Install BepInEx into release/ (downloads once, cached in vendor/)
setup:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ -f "{{game}}/winhttp.dll" ] && [ -d "{{game}}/BepInEx/core" ]; then
        echo "BepInEx already installed in release/"
        exit 0
    fi
    echo "==> Downloading BepInEx {{bepinex_version}} (cached in vendor/)..."
    mkdir -p "{{root}}/vendor"
    if [ ! -f "{{bepinex_cache}}" ]; then
        curl -fSL --progress-bar "{{bepinex_url}}" -o "{{bepinex_cache}}"
    fi
    echo "==> Extracting BepInEx to release/..."
    unzip -qo "{{bepinex_cache}}" -d "{{game}}"
    mkdir -p "{{game}}/BepInEx/plugins" "{{game}}/BepInEx/patchers" "{{game}}/BepInEx/config"
    echo "BepInEx {{bepinex_version}} installed to release/"

# Build debug, deploy to game dir, launch game, tail logs
dev: setup
    #!/usr/bin/env bash
    set -euo pipefail

    echo "==> Building (Debug)..."
    dotnet build {{src}}/PeglinMods.sln -c Debug --nologo -v quiet

    echo "==> Deploying to game dir..."
    mkdir -p {{plugins}}
    cp {{src}}/PeglinMods.Core/bin/Debug/net462/PeglinMods.Core.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/PeglinMods.Spectator.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/LiteNetLib.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/System.Text.Json.dll {{plugins}}/
    for dep in System.Text.Encodings.Web System.Buffers System.Memory System.Numerics.Vectors System.Runtime.CompilerServices.Unsafe; do
        f="{{src}}/PeglinMods.Spectator/bin/Debug/net462/${dep}.dll"
        [ -f "$f" ] && cp "$f" {{plugins}}/ || true
    done

    echo "==> Creating dev log file..."
    mkdir -p "$(dirname {{logfile}})"
    : > {{logfile}}

    echo "==> Launching game..."
    {{root}}/launch.sh &
    GAME_PID=$!

    echo "==> Tailing logs (Ctrl+C to stop)..."
    echo "    Log: {{logfile}}"
    echo ""

    sleep 1
    tail -f {{logfile}} || true

    kill $GAME_PID 2>/dev/null || true

# Deploy plugin to game dir without launching
deploy: setup
    #!/usr/bin/env bash
    set -euo pipefail
    echo "==> Building (Debug)..."
    dotnet build {{src}}/PeglinMods.sln -c Debug --nologo -v quiet
    echo "==> Deploying..."
    mkdir -p {{plugins}}
    cp {{src}}/PeglinMods.Core/bin/Debug/net462/PeglinMods.Core.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/PeglinMods.Spectator.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/LiteNetLib.dll {{plugins}}/
    cp {{src}}/PeglinMods.Spectator/bin/Debug/net462/System.Text.Json.dll {{plugins}}/
    for dep in System.Text.Encodings.Web System.Buffers System.Memory System.Numerics.Vectors System.Runtime.CompilerServices.Unsafe; do
        f="{{src}}/PeglinMods.Spectator/bin/Debug/net462/${dep}.dll"
        [ -f "$f" ] && cp "$f" {{plugins}}/ || true
    done
    @echo "Deployed to {{plugins}}"

# Tail the dev log
log:
    tail -f {{logfile}}

# Clean build artifacts
clean:
    rm -rf {{root}}/build {{root}}/dist {{root}}/vendor
    rm -rf {{src}}/PeglinMods.Core/bin {{src}}/PeglinMods.Core/obj
    rm -rf {{src}}/PeglinMods.Spectator/bin {{src}}/PeglinMods.Spectator/obj

# Remove BepInEx from release/ (restore to vanilla)
uninstall:
    rm -f {{game}}/winhttp.dll {{game}}/doorstop_config.ini {{game}}/.doorstop_version
    rm -rf {{game}}/BepInEx
    @echo "BepInEx removed from release/"
