# Launch Peglin via Proton or Wine without Steam
# Works on both Windows (native) and Linux (Proton/Wine)
param(
    [switch]$UseWine
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$GameDir = Join-Path $ScriptDir "release"
$GameExe = Join-Path $GameDir "Peglin.exe"

# --- Disable crash reporter ---
$CrashHandler = Join-Path $GameDir "UnityCrashHandler64.exe"
if (Test-Path $CrashHandler) {
    Move-Item $CrashHandler "$CrashHandler.disabled" -Force
    Write-Host "Disabled Unity crash reporter"
}

if (-not (Test-Path $GameExe)) {
    Write-Error "Game executable not found at $GameExe"
    exit 1
}

# --- Windows: run directly ---
if ($IsWindows -or (-not $IsLinux -and -not $IsMacOS)) {
    Write-Host "Launching Peglin..."
    & $GameExe @args
    exit $LASTEXITCODE
}

# --- Linux: use Proton or Wine ---
$env:WINEDLLOVERRIDES = "winhttp=n,b"

if ($UseWine) {
    Write-Host "Launching Peglin via Wine..."
    Set-Location $GameDir
    & wine $GameExe @args
    exit $LASTEXITCODE
}

# Find Proton
$ProtonDir = $env:PROTON_DIR
if (-not $ProtonDir) {
    $SteamCommon = "$HOME/.steam/steam/steamapps/common"
    if (Test-Path $SteamCommon) {
        $ProtonDir = Get-ChildItem $SteamCommon -Directory -Filter "Proton *" |
            Where-Object { $_.Name -notmatch "Experimental" } |
            Sort-Object Name -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $ProtonDir) {
        $ProtonDir = "$SteamCommon/Proton - Experimental"
    }
}

$ProtonBin = Join-Path $ProtonDir "proton"
if (-not (Test-Path $ProtonBin)) {
    Write-Error "Proton not found at $ProtonBin"
    if (Test-Path "$HOME/.steam/steam/steamapps/common") {
        Write-Host "Available Proton versions:"
        Get-ChildItem "$HOME/.steam/steam/steamapps/common" -Directory -Filter "*roton*" |
            ForEach-Object { Write-Host "  $($_.Name)" }
    }
    exit 1
}

# Use existing STEAM_COMPAT_DATA_PATH if set (for multi-instance), else default
$CompatData = $env:STEAM_COMPAT_DATA_PATH
if (-not $CompatData) {
    $CompatData = "$HOME/.steam/steam/steamapps/compatdata/1296610"
}
New-Item -ItemType Directory -Path $CompatData -Force | Out-Null

$env:STEAM_COMPAT_DATA_PATH = $CompatData
$env:STEAM_COMPAT_CLIENT_INSTALL_PATH = "$HOME/.steam/steam"

Write-Host "Launching Peglin via Proton..."
Set-Location $GameDir
& $ProtonBin run $GameExe @args
