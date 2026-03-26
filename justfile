# PeglinMods development commands
# All scripts use PowerShell (pwsh) for cross-platform compatibility.

set shell := ["pwsh", "-NoProfile", "-Command"]

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
    dotnet build '{{src}}/PeglinMods.sln' -c Debug --nologo

# Build release and copy to build/
publish:
    dotnet build '{{src}}/PeglinMods.sln' -c Release --nologo; \
    New-Item -ItemType Directory -Path '{{root}}/build' -Force | Out-Null; \
    Copy-Item '{{src}}/PeglinMods.Core/bin/Release/net462/PeglinMods.Core.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/PeglinMods.Spectator/bin/Release/net462/PeglinMods.Spectator.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/PeglinMods.Spectator/bin/Release/net462/LiteNetLib.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/PeglinMods.Spectator/bin/Release/net462/System.Text.Json.dll' '{{root}}/build/'; \
    Write-Host "`nPublish output:"; \
    Get-ChildItem '{{root}}/build/*.dll' | Format-Table Name, Length

# Install BepInEx into release/ (downloads once, cached in vendor/)
setup:
    if (Test-Path '{{game}}/winhttp.dll') { \
        Write-Host 'BepInEx already installed in release/'; \
        return; \
    } \
    Write-Host '==> Downloading BepInEx {{bepinex_version}}...'; \
    New-Item -ItemType Directory -Path '{{root}}/vendor' -Force | Out-Null; \
    if (-not (Test-Path '{{bepinex_cache}}')) { \
        Invoke-WebRequest -Uri '{{bepinex_url}}' -OutFile '{{bepinex_cache}}'; \
    } \
    Write-Host '==> Extracting BepInEx to release/...'; \
    Expand-Archive -Path '{{bepinex_cache}}' -DestinationPath '{{game}}' -Force; \
    foreach ($d in @('plugins','patchers','config')) { \
        New-Item -ItemType Directory -Path (Join-Path '{{game}}' "BepInEx/$d") -Force | Out-Null; \
    } \
    Write-Host 'BepInEx {{bepinex_version}} installed to release/'

# Deploy plugin DLLs to game dir
[private]
copy-plugins config="Debug":
    New-Item -ItemType Directory -Path '{{plugins}}' -Force | Out-Null; \
    $bin = '{{src}}/PeglinMods.Spectator/bin/{{config}}/net462'; \
    Copy-Item '{{src}}/PeglinMods.Core/bin/{{config}}/net462/PeglinMods.Core.dll' '{{plugins}}/'; \
    Copy-Item "$bin/PeglinMods.Spectator.dll" '{{plugins}}/'; \
    Copy-Item "$bin/LiteNetLib.dll" '{{plugins}}/'; \
    Copy-Item "$bin/System.Text.Json.dll" '{{plugins}}/'; \
    foreach ($dep in @('System.Text.Encodings.Web','System.Buffers','System.Memory','System.Numerics.Vectors','System.Runtime.CompilerServices.Unsafe')) { \
        $f = "$bin/$dep.dll"; \
        if (Test-Path $f) { Copy-Item $f '{{plugins}}/' } \
    }

# Build debug, deploy to game dir, launch game, tail logs
dev: setup
    dotnet build '{{src}}/PeglinMods.sln' -c Debug --nologo -v quiet; \
    just copy-plugins Debug; \
    New-Item -ItemType Directory -Path (Split-Path '{{logfile}}') -Force | Out-Null; \
    '' | Set-Content '{{logfile}}'; \
    Write-Host '==> Launching game...'; \
    $game = Start-Process pwsh -ArgumentList '-NoProfile','-File','{{root}}/launch.ps1' -PassThru; \
    Write-Host "==> Tailing logs (Ctrl+C to stop)"; \
    Write-Host "    Log: {{logfile}}`n"; \
    Start-Sleep 1; \
    Get-Content '{{logfile}}' -Wait

# Deploy plugin to game dir without launching
deploy: setup
    dotnet build '{{src}}/PeglinMods.sln' -c Debug --nologo -v quiet; \
    just copy-plugins Debug; \
    Write-Host "Deployed to {{plugins}}"

# Tail the dev log
log:
    Get-Content '{{logfile}}' -Wait

# Clean build artifacts
clean:
    Remove-Item '{{root}}/build','{{root}}/dist','{{root}}/vendor' -Recurse -Force -ErrorAction SilentlyContinue; \
    Remove-Item '{{src}}/PeglinMods.Core/bin','{{src}}/PeglinMods.Core/obj' -Recurse -Force -ErrorAction SilentlyContinue; \
    Remove-Item '{{src}}/PeglinMods.Spectator/bin','{{src}}/PeglinMods.Spectator/obj' -Recurse -Force -ErrorAction SilentlyContinue; \
    Write-Host 'Cleaned'

# Remove BepInEx from release/ (restore to vanilla)
uninstall:
    foreach ($f in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { \
        $p = Join-Path '{{game}}' $f; \
        if (Test-Path $p) { Remove-Item $p -Force } \
    }; \
    $bep = Join-Path '{{game}}' 'BepInEx'; \
    if (Test-Path $bep) { Remove-Item $bep -Recurse -Force }; \
    Write-Host 'BepInEx removed from release/'
