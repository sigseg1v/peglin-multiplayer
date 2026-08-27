param(
    [string]$Level = "",
    [int]$Players = 2,
    [string]$Root,
    [string]$Game,
    [switch]$SkipMuvmWrap
)

# Shared multi-instance launcher used by `just dev-multi`.
# Starts N windowed Peglin instances with separate Proton prefixes / log files.
# Optionally routes through scripts/muvm-wrap.ps1 (no-op outside muvm hosts).

$ErrorActionPreference = "Stop"

if ($Players -lt 1) { $Players = 1 }

if (-not $SkipMuvmWrap) {
    $inner = "pwsh -NoProfile -File '$PSCommandPath' -Level '$Level' -Players $Players -Root '$Root' -Game '$Game' -SkipMuvmWrap"
    & (Join-Path $Root "scripts/muvm-wrap.ps1") -Root $Root -Command $inner
    exit $LASTEXITCODE
}

$windowArgs = @("-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720")
$compatBase = Join-Path $HOME ".steam/steam/steamapps/compatdata"
$launchScript = Join-Path $Root "launch.ps1"

if ($Level) {
    $env:MULTIPEGLIN_FORCE_LEVEL = $Level
    Write-Host "==> Force level: $Level"
}

Write-Host "==> Launching $Players instance(s)"
$env:SKIP_STEAM_INIT = "1"

$steamAppId = Join-Path $Game "steam_appid.txt"
$steamAppIdBak = "$steamAppId.devmulti"
if (Test-Path $steamAppId) {
    Move-Item $steamAppId $steamAppIdBak -Force
}

try {
    for ($i = 1; $i -le $Players; $i++) {
        $name = "PEGLIN$i"
        $compatId = 1296609 + $i
        Write-Host "==> Launching $name (windowed, compatdata=$compatId)..."
        $env:MULTIPEGLIN_INSTANCE = $name
        $env:MULTIPEGLIN_PLAYER_NAME = $name
        $env:MULTIPEGLIN_LOGNAME = "multipeglin_$name.log"
        $env:STEAM_COMPAT_DATA_PATH = "$compatBase/$compatId"
        Start-Process pwsh -ArgumentList (@("-NoProfile", "-File", $launchScript) + $windowArgs)
        if ($i -lt $Players) {
            Start-Sleep 5
        }
    }
}
finally {
    if (Test-Path $steamAppIdBak) {
        Move-Item $steamAppIdBak $steamAppId -Force
    }
    Remove-Item Env:\MULTIPEGLIN_INSTANCE, Env:\MULTIPEGLIN_PLAYER_NAME, Env:\MULTIPEGLIN_LOGNAME,
        Env:\STEAM_COMPAT_DATA_PATH, Env:\MULTIPEGLIN_FORCE_LEVEL, Env:\MULTIPEGLIN_FORCE_NODE, Env:\SKIP_STEAM_INIT `
        -ErrorAction SilentlyContinue
}
