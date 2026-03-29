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
    Copy-Item '{{src}}/PeglinMods.Core/bin/Release/netstandard2.1/PeglinMods.Core.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/PeglinMods.Multiplayer/bin/Release/netstandard2.1/PeglinMods.Multiplayer.dll' '{{root}}/build/'; \
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
    $bin = '{{src}}/PeglinMods.Multiplayer/bin/{{config}}/netstandard2.1'; \
    Copy-Item '{{src}}/PeglinMods.Core/bin/{{config}}/netstandard2.1/PeglinMods.Core.dll' '{{plugins}}/'; \
    Copy-Item "$bin/PeglinMods.Multiplayer.dll" '{{plugins}}/'; \
    Copy-Item "$bin/LiteNetLib.dll" '{{plugins}}/'

# Build debug, deploy to game dir, launch game, tail logs
dev: setup
    dotnet build '{{src}}/PeglinMods.sln' -c Debug --nologo -v quiet; \
    just copy-plugins Debug; \
    New-Item -ItemType Directory -Path (Split-Path '{{logfile}}') -Force | Out-Null; \
    [IO.File]::Create('{{logfile}}').Close(); \
    Write-Host '==> Launching game...'; \
    $game = Start-Process pwsh -ArgumentList '-NoProfile','-File','{{root}}/launch.ps1' -PassThru; \
    Write-Host "==> Tailing logs (Ctrl+C to stop)"; \
    Write-Host "    Log: {{logfile}}`n"; \
    Start-Sleep 1; \
    Get-Content '{{logfile}}' -Wait

# Build, deploy, launch TWO windowed instances for multiplayer testing.
# Both host and client write to peglinmods_shared.log with [HOST]/[CLIENT] tags.
dev-multi: setup
    dotnet build '{{src}}/PeglinMods.sln' -c Debug --nologo -v quiet; \
    just copy-plugins Debug; \
    $logsDir = Split-Path '{{logfile}}'; \
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null; \
    $sharedLog = Join-Path $logsDir 'peglinmods_shared.log'; \
    [IO.File]::Create($sharedLog).Close(); \
    $windowArgs = @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720'); \
    $compatBase = "$HOME/.steam/steam/steamapps/compatdata"; \
    Write-Host '==> Launching HOST (windowed)...'; \
    $env:STEAM_COMPAT_DATA_PATH = "$compatBase/1296610"; \
    Start-Process pwsh -ArgumentList (@('-NoProfile','-File','{{root}}/launch.ps1') + $windowArgs); \
    Start-Sleep 2; \
    Write-Host '==> Launching CLIENT (windowed)...'; \
    $env:STEAM_COMPAT_DATA_PATH = "$compatBase/1296611"; \
    Start-Process pwsh -ArgumentList (@('-NoProfile','-File','{{root}}/launch.ps1') + $windowArgs); \
    Remove-Item Env:\STEAM_COMPAT_DATA_PATH -ErrorAction SilentlyContinue; \
    Write-Host "==> Tailing shared log (Ctrl+C to stop)"; \
    Write-Host "    Log: $sharedLog`n"; \
    Start-Sleep 1; \
    Get-Content $sharedLog -Wait

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
    Remove-Item '{{src}}/PeglinMods.Multiplayer/bin','{{src}}/PeglinMods.Multiplayer/obj' -Recurse -Force -ErrorAction SilentlyContinue; \
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
