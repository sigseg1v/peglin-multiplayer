# Multipeglin development commands
# All scripts use PowerShell (pwsh) for cross-platform compatibility.

set shell := ["pwsh", "-NoProfile", "-Command"]

root := justfile_directory()
src := root / "src"
game := root / "release"
plugins := game / "BepInEx" / "plugins" / "Multipeglin"
logfile := game / "BepInEx" / "logs" / "multipeglin_log.log"

bepinex_version := "5.4.23.2"
bepinex_zip := "BepInEx_win_x64_" + bepinex_version + ".zip"
bepinex_url := "https://github.com/BepInEx/BepInEx/releases/download/v" + bepinex_version + "/" + bepinex_zip
bepinex_cache := root / "vendor" / bepinex_zip

# r2modman profile to source Thunderstore mods from (Linux flatpak default).
# Override with $env:R2MODMAN_PROFILE = '/some/path' before invoking sync.
r2modman_profile_default := ".var/app/com.github.ebkr.r2modman/config/r2modmanPlus-local/Peglin/profiles/Default"

# Build debug
build:
    dotnet build '{{src}}/Multipeglin.sln' -c Debug --nologo

# Build release and copy to build/
publish:
    dotnet build '{{src}}/Multipeglin.sln' -c Release --nologo; \
    if ($LASTEXITCODE -ne 0) { Write-Host '==> Build failed. Aborting publish.' -ForegroundColor Red; exit 1 } \
    New-Item -ItemType Directory -Path '{{root}}/build' -Force | Out-Null; \
    Copy-Item '{{src}}/Multipeglin.Core/bin/Release/netstandard2.1/Multipeglin.Core.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/Multipeglin/bin/Release/netstandard2.1/Multipeglin.dll' '{{root}}/build/'; \
    Copy-Item '{{src}}/Multipeglin.CustomOrbs/bin/Release/netstandard2.1/Multipeglin.CustomOrbs.dll' '{{root}}/build/'; \
    Write-Host "`nPublish output:"; \
    Get-ChildItem '{{root}}/build/*.dll' | Format-Table Name, Length

# Install BepInEx into release/ (downloads once, cached in vendor/)
setup:
    if (Test-Path '{{game}}/winhttp.dll') { \
        Write-Host 'BepInEx already installed in release/'; \
    } else { \
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
        Write-Host 'BepInEx {{bepinex_version}} installed to release/'; \
    }

# Restore steam_appid.txt from a dev-network-player backup if a prior run was
# interrupted. All other recipes depend on this so they never launch against
# the wrong AppID after an aborted network-player session.
[private]
_restore-appid:
    $steamAppId = Join-Path '{{game}}' 'steam_appid.txt'; \
    $steamAppIdBak = "$steamAppId.netplayer"; \
    if (Test-Path $steamAppIdBak) { \
        Remove-Item $steamAppId -Force -ErrorAction SilentlyContinue; \
        Move-Item $steamAppIdBak $steamAppId -Force; \
        Write-Host '==> Recovered steam_appid.txt from prior dev-network-player run'; \
    }

# Deploy plugin DLLs to game dir
[private]
copy-plugins config="Debug":
    $ErrorActionPreference = 'Stop'; \
    New-Item -ItemType Directory -Path '{{plugins}}' -Force | Out-Null; \
    $bin = '{{src}}/Multipeglin/bin/{{config}}/netstandard2.1'; \
    Copy-Item '{{src}}/Multipeglin.Core/bin/{{config}}/netstandard2.1/Multipeglin.Core.dll' '{{plugins}}/'; \
    Copy-Item "$bin/Multipeglin.dll" '{{plugins}}/'; \
    Copy-Item "$bin/LiteNetLib.dll" '{{plugins}}/'; \
    Copy-Item "$bin/NLog.dll" '{{plugins}}/'; \
    Copy-Item '{{src}}/Multipeglin.CustomOrbs/bin/{{config}}/netstandard2.1/Multipeglin.CustomOrbs.dll' '{{plugins}}/'

# Build debug and deploy plugins. Aborts on build failure so no recipe ever
# launches, deploys, or packages a stale binary. dotnet's exit code is checked
# explicitly because pwsh ';' does not stop on a failed native command, and the
# failure would otherwise be masked by later commands in the same recipe.
[private]
build-deploy config="Debug":
    dotnet build '{{src}}/Multipeglin.sln' -c {{config}} --nologo -v quiet; \
    if ($LASTEXITCODE -ne 0) { Write-Host '==> Build failed. Aborting so no stale binary is launched.' -ForegroundColor Red; exit 1 } \
    just copy-plugins {{config}}

# Build debug, deploy to game dir, launch game, tail logs
dev: setup _restore-appid build-deploy
    New-Item -ItemType Directory -Path (Split-Path '{{logfile}}') -Force | Out-Null; \
    [IO.File]::Create('{{logfile}}').Close(); \
    Write-Host '==> Launching game...'; \
    $game = Start-Process pwsh -ArgumentList '-NoProfile','-File','{{root}}/launch.ps1' -PassThru; \
    Write-Host "==> Tailing logs (Ctrl+C to stop)"; \
    Write-Host "    Log: {{logfile}}`n"; \
    Start-Sleep 1; \
    Get-Content '{{logfile}}' -Wait

# Build, deploy, launch N windowed instances for multiplayer testing.
# Each instance writes to its own multipeglin_PEGLIN<N>.log with [HOST]/[CLIENT] tags.
# Optional: pass level to force a starting act, e.g. just dev-multi 3 (Mines)
#   Acts: 1=Forest, 2=Castle, 3=Mines, 4=Core
#   With floor: just dev-multi 3-2
# Optional: pass player count (default 2), e.g. just dev-multi 1 4 launches 4 instances on Forest
# Optional: set PEGLIN_SEED env var for deterministic seeds, e.g.
#   PEGLIN_SEED=12345 just dev-multi 2
dev-multi level="" players="2": setup _restore-appid build-deploy
    $logsDir = Split-Path '{{logfile}}'; \
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null; \
    $hostLog = Join-Path $logsDir 'multipeglin_PEGLIN1.log'; \
    [IO.File]::Create($hostLog).Close(); \
    $windowArgs = @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720'); \
    $compatBase = "$HOME/.steam/steam/steamapps/compatdata"; \
    $playerCount = [int]'{{players}}'; \
    if ($playerCount -lt 1) { $playerCount = 1 } \
    if ('{{level}}' -ne '') { \
        $env:MULTIPEGLIN_FORCE_LEVEL = '{{level}}'; \
        Write-Host "==> Force level: {{level}}"; \
    } \
    Write-Host "==> Launching $playerCount instance(s)"; \
    $env:SKIP_STEAM_INIT = '1'; \
    $steamAppId = Join-Path '{{game}}' 'steam_appid.txt'; \
    $steamAppIdBak = "$steamAppId.devmulti"; \
    if (Test-Path $steamAppId) { Move-Item $steamAppId $steamAppIdBak -Force } \
    for ($i = 1; $i -le $playerCount; $i++) { \
        $name = "PEGLIN$i"; \
        $compatId = 1296609 + $i; \
        Write-Host "==> Launching $name (windowed, compatdata=$compatId)..."; \
        $env:MULTIPEGLIN_INSTANCE = $name; \
        $env:MULTIPEGLIN_PLAYER_NAME = $name; \
        $env:MULTIPEGLIN_LOGNAME = "multipeglin_$name.log"; \
        $env:STEAM_COMPAT_DATA_PATH = "$compatBase/$compatId"; \
        Start-Process pwsh -ArgumentList (@('-NoProfile','-File','{{root}}/launch.ps1') + $windowArgs); \
        if ($i -lt $playerCount) { Start-Sleep 5 } \
    } \
    if (Test-Path $steamAppIdBak) { Move-Item $steamAppIdBak $steamAppId -Force } \
    Remove-Item Env:\MULTIPEGLIN_INSTANCE,Env:\MULTIPEGLIN_PLAYER_NAME,Env:\MULTIPEGLIN_LOGNAME,Env:\STEAM_COMPAT_DATA_PATH,Env:\MULTIPEGLIN_FORCE_LEVEL,Env:\SKIP_STEAM_INIT -ErrorAction SilentlyContinue; \
    Write-Host "==> Tailing host log (Ctrl+C to stop)"; \
    Write-Host "    Host: $hostLog"; \
    Write-Host "    Clients: $logsDir/multipeglin_PEGLIN<N>.log`n"; \
    Start-Sleep 1; \
    Get-Content $hostLog -Wait

# Launch one game instance with Steam enabled against Spacewar AppID (480).
# Run this on TWO machines (e.g. main PC + laptop/VM with a free Steam account) to
# test Steam networking end-to-end without both owning Peglin. BOTH machines must
# use this recipe — Steam lobbies/friends/P2P are scoped per-AppID, so a 1296610
# instance and a 480 instance can't see each other.
dev-network-player: setup _restore-appid build-deploy
    New-Item -ItemType Directory -Path (Split-Path '{{logfile}}') -Force | Out-Null; \
    [IO.File]::Create('{{logfile}}').Close(); \
    $steamAppId = Join-Path '{{game}}' 'steam_appid.txt'; \
    $steamAppIdBak = "$steamAppId.netplayer"; \
    try { \
        if (Test-Path $steamAppId) { Move-Item $steamAppId $steamAppIdBak -Force }; \
        Set-Content -Path $steamAppId -Value '480' -NoNewline; \
        Write-Host '==> Using Spacewar AppID (480) for Steam networking'; \
        Write-Host '==> Launching game...'; \
        Start-Process pwsh -ArgumentList '-NoProfile','-File','{{root}}/launch.ps1'; \
        Write-Host '==> Waiting for Steam init to consume steam_appid.txt...'; \
        $deadline = (Get-Date).AddSeconds(120); \
        while ((Get-Date) -lt $deadline) { \
            if ((Test-Path '{{logfile}}') -and ((Get-Content '{{logfile}}' -Raw) -match '\[Steam\] Transport constructed')) { break } \
            Start-Sleep -Milliseconds 500; \
        } \
        Start-Sleep 1; \
        Remove-Item $steamAppId -Force -ErrorAction SilentlyContinue; \
        if (Test-Path $steamAppIdBak) { Move-Item $steamAppIdBak $steamAppId -Force }; \
        Write-Host "==> Tailing logs (Ctrl+C to stop)"; \
        Write-Host "    Log: {{logfile}}`n"; \
        Get-Content '{{logfile}}' -Wait; \
    } finally { \
        if (Test-Path $steamAppIdBak) { \
            Remove-Item $steamAppId -Force -ErrorAction SilentlyContinue; \
            Move-Item $steamAppIdBak $steamAppId -Force; \
            Write-Host '==> Restored steam_appid.txt'; \
        } \
    }

# Copy Thunderstore mods (TestingFloor, PeglinCore, MMHOOK, AutoHookGenPatcher,
# DetourContext_Dispose_Fix, HideManager, ...) from a r2modman profile into
# release/BepInEx so they load alongside Multipeglin. Records what was copied
# to release/BepInEx/.synced-thunderstore-mods so `unsync-thunderstore-mods`
# can reverse it. Override the source with $env:R2MODMAN_PROFILE.
sync-thunderstore-mods: setup
    $src = if ($env:R2MODMAN_PROFILE) { $env:R2MODMAN_PROFILE } else { Join-Path $HOME '{{r2modman_profile_default}}' }; \
    $srcBep = Join-Path $src 'BepInEx'; \
    if (-not (Test-Path $srcBep)) { Write-Error "Source profile not found: $srcBep"; exit 1 } \
    $dstBep = Join-Path '{{game}}' 'BepInEx'; \
    $tracker = Join-Path $dstBep '.synced-thunderstore-mods'; \
    $synced = New-Object System.Collections.Generic.List[string]; \
    foreach ($d in @('plugins','patchers')) { \
        $sd = Join-Path $srcBep $d; \
        if (-not (Test-Path $sd)) { continue } \
        $dd = Join-Path $dstBep $d; \
        New-Item -ItemType Directory -Path $dd -Force | Out-Null; \
        Get-ChildItem $sd -Directory | Where-Object { $_.Name -ne 'Multipeglin' } | ForEach-Object { \
            $target = Join-Path $dd $_.Name; \
            if (Test-Path $target) { Remove-Item $target -Recurse -Force } \
            Copy-Item -Recurse $_.FullName $dd; \
            $synced.Add("$d/$($_.Name)"); \
            Write-Host "  + $d/$($_.Name)"; \
        } \
    } \
    Set-Content -Path $tracker -Value $synced; \
    Write-Host "Thunderstore mods synced from $src"

# Reverse `sync-thunderstore-mods`. Reads the tracker file and removes only
# what was copied; leaves Multipeglin and core BepInEx files alone.
unsync-thunderstore-mods:
    $dstBep = Join-Path '{{game}}' 'BepInEx'; \
    $tracker = Join-Path $dstBep '.synced-thunderstore-mods'; \
    if (-not (Test-Path $tracker)) { Write-Host 'No tracked Thunderstore mods to remove'; exit 0 } \
    Get-Content $tracker | Where-Object { $_ } | ForEach-Object { \
        $target = Join-Path $dstBep $_; \
        if (Test-Path $target) { \
            Remove-Item $target -Recurse -Force; \
            Write-Host "  - $_"; \
        } \
    }; \
    Remove-Item $tracker -Force -ErrorAction SilentlyContinue; \
    Write-Host 'Thunderstore mods removed'

# Deploy plugin to game dir without launching
deploy: setup _restore-appid build-deploy
    Write-Host "Deployed to {{plugins}}"

# Tail the dev log
log:
    Get-Content '{{logfile}}' -Wait

thunderstore := root / "thunderstore"

# Build Release, create Thunderstore package zip in dist/
package:
    dotnet build '{{src}}/Multipeglin.sln' -c Release --nologo; \
    if ($LASTEXITCODE -ne 0) { Write-Host '==> Build failed. Aborting package.' -ForegroundColor Red; exit 1 } \
    $version = (Get-Content '{{thunderstore}}/manifest.json' | ConvertFrom-Json).version_number; \
    $dist = '{{root}}/dist'; \
    $staging = Join-Path $dist 'staging'; \
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue; \
    New-Item -ItemType Directory -Path $staging -Force | Out-Null; \
    $bin = '{{src}}/Multipeglin/bin/Release/netstandard2.1'; \
    Copy-Item '{{thunderstore}}/manifest.json' $staging/; \
    Copy-Item '{{thunderstore}}/icon.png' $staging/; \
    Copy-Item '{{thunderstore}}/README.md' $staging/; \
    Copy-Item '{{thunderstore}}/CHANGELOG.md' $staging/; \
    Copy-Item '{{src}}/Multipeglin.Core/bin/Release/netstandard2.1/Multipeglin.Core.dll' $staging/; \
    Copy-Item "$bin/Multipeglin.dll" $staging/; \
    Copy-Item "$bin/LiteNetLib.dll" $staging/; \
    Copy-Item "$bin/NLog.dll" $staging/; \
    $zipName = "Multipeglin-$version.zip"; \
    $zipPath = Join-Path $dist $zipName; \
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue; \
    Compress-Archive -Path "$staging/*" -DestinationPath $zipPath; \
    Remove-Item $staging -Recurse -Force; \
    $size = [math]::Round((Get-Item $zipPath).Length / 1KB, 1); \
    Write-Host "`nPackage created: dist/$zipName ($size KB)"; \
    Write-Host 'Contents:'; \
    [System.IO.Compression.ZipFile]::OpenRead($zipPath).Entries | ForEach-Object { Write-Host "  $_" }

# Clean build artifacts
clean:
    Remove-Item '{{root}}/build','{{root}}/dist','{{root}}/vendor' -Recurse -Force -ErrorAction SilentlyContinue; \
    Remove-Item '{{src}}/Multipeglin.Core/bin','{{src}}/Multipeglin.Core/obj' -Recurse -Force -ErrorAction SilentlyContinue; \
    Remove-Item '{{src}}/Multipeglin/bin','{{src}}/Multipeglin/obj' -Recurse -Force -ErrorAction SilentlyContinue; \
    Write-Host 'Cleaned'

# Remove BepInEx from release/ and reset Proton prefixes (restore to vanilla).
# Also runs unsync-thunderstore-mods first so any synced Thunderstore mods
# (TestingFloor etc.) are reported and removed by name before BepInEx is nuked.
uninstall: unsync-thunderstore-mods
    foreach ($f in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { \
        $p = Join-Path '{{game}}' $f; \
        if (Test-Path $p) { Remove-Item $p -Force } \
    }; \
    $bep = Join-Path '{{game}}' 'BepInEx'; \
    if (Test-Path $bep) { Remove-Item $bep -Recurse -Force }; \
    Write-Host 'BepInEx removed from release/'; \
    $compatBase = "$HOME/.steam/steam/steamapps/compatdata"; \
    foreach ($id in @('1296610','1296611')) { \
        $pfx = Join-Path $compatBase $id; \
        if (Test-Path $pfx) { \
            Remove-Item $pfx -Recurse -Force; \
            Write-Host "Removed Proton prefix: $pfx"; \
        } \
    }; \
    Write-Host 'Proton prefixes cleared (will be recreated on next launch)'
