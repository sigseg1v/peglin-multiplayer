# PeglinMods development commands

root := justfile_directory()
src := root / "src"
game := root / "release"
plugins := game / "BepInEx" / "plugins" / "PeglinMods"
logfile := game / "BepInEx" / "logs" / "peglinmods_dev.log"

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
    @echo ""
    @echo "To install: ./install/install.sh {{game}}"

# Build debug, deploy to game dir, launch game, tail logs
dev:
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
    # Copy System.Text.Json transitive deps
    for dep in System.Text.Encodings.Web System.Buffers System.Memory System.Numerics.Vectors System.Runtime.CompilerServices.Unsafe; do
        f="{{src}}/PeglinMods.Spectator/bin/Debug/net462/${dep}.dll"
        [ -f "$f" ] && cp "$f" {{plugins}}/ || true
    done

    echo "==> Setting up BepInEx (if needed)..."
    # Ensure BepInEx core is deployed (install script handles this properly,
    # but for quick dev we just need winhttp.dll + doorstop_config.ini + core/)
    if [ ! -f "{{game}}/winhttp.dll" ]; then
        echo "    BepInEx not installed in release/. Run: ./install/install.sh {{game}}"
        exit 1
    fi

    echo "==> Creating dev log file..."
    mkdir -p "$(dirname {{logfile}})"
    : > {{logfile}}

    echo "==> Launching game..."
    {{root}}/launch.sh &
    GAME_PID=$!

    echo "==> Tailing logs (Ctrl+C to stop)..."
    echo "    Log: {{logfile}}"
    echo ""

    # Wait a moment for game to start writing, then tail
    sleep 1
    tail -f {{logfile}} || true

    # If tail exits, also stop the game
    kill $GAME_PID 2>/dev/null || true

# Deploy plugin to game dir without launching
deploy:
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
