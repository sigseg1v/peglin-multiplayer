param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [Parameter(Mandatory = $true)]
    [string]$Command
)

# Optional launch wrapper for environments that need Steam's muvm
#
# Behavior:
#   - If MUVM_SKIP_WRAP=1, or muvm is unavailable, or arch is not aarch64:
#     run $Command in-process (no-op passthrough). Safe on x86_64 Linux,
#     macOS, and Windows.
#   - Otherwise re-exec $Command inside `muvm`, forwarding common Multipeglin
#     debug env vars so force-seed / force-node / debug still apply.
#
# Force passthrough: MUVM_SKIP_WRAP=1 just dev

$ErrorActionPreference = "Stop"

if ($env:MUVM_SKIP_WRAP -eq "1") {
    Invoke-Expression $Command
    exit $LASTEXITCODE
}

$uname = Get-Command uname -ErrorAction SilentlyContinue
$arch = if ($uname) { & uname -m 2>$null } else { $null }
$muvm = Get-Command muvm -ErrorAction SilentlyContinue
if ($arch -ne "aarch64" -or -not $muvm) {
    Invoke-Expression $Command
    exit $LASTEXITCODE
}

$exports = @("export MUVM_SKIP_WRAP=1")
foreach ($name in @(
        "PEGLIN_SEED",
        "MULTIPEGLIN_FORCE_LEVEL",
        "MULTIPEGLIN_FORCE_NODE",
        "MULTIPEGLIN_DEBUG",
        "PROTON_DIR"
    )) {
    $val = [Environment]::GetEnvironmentVariable($name)
    if ($val) {
        $escaped = $val -replace "'", "'\\''"
        $exports += "export $name='$escaped'"
    }
}

$bashCmd = ($exports -join "; ") + "; cd '$Root'; $Command"
Write-Host "==> muvm: forwarding launch into Steam muvm (Steam must be running)..."
& muvm -- /usr/bin/bash -lc $bashCmd
exit $LASTEXITCODE
