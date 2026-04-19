# Multipeglin

A cooperative multiplayer mod for [Peglin](https://store.steampowered.com/app/1296610/Peglin/) (v2.0.7, Unity 2022.3, Mono).

Built with BepInEx 5 + HarmonyX. Cross-platform: Windows (native) and Linux (Proton/Wine).
All scripts use PowerShell (pwsh) for portability.

## Quick Start

```bash
just dev       # build + install BepInEx + deploy plugin + launch game + tail logs
```

That's it. On first run it downloads BepInEx automatically.

## All Commands

```bash
just build      # compile (debug)
just publish    # compile (release) + copy DLLs to build/
just package    # compile (release) + create Thunderstore package zip in dist/
just setup      # download + install BepInEx into release/ (auto-run by dev/deploy)
just deploy     # build + deploy plugin into release/BepInEx/plugins/
just dev        # build + deploy + launch game + tail logs
just dev-multi  # build + deploy + launch two game instances (host + client)
just dev-network-player  # build + deploy + launch one instance under Steam's Spacewar AppID (480) for real cross-machine Steam networking tests
just log        # tail the dev log file
just clean      # remove build artifacts
just uninstall  # remove BepInEx + reset Proton prefixes (full reset)
```

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/) 8+
- [just](https://github.com/casey/just) command runner
- [PowerShell](https://github.com/PowerShell/PowerShell) (pwsh) - cross-platform scripting
- Linux only: Proton or Wine (for running the game)

## Project Structure

```
src/
  Multipeglin.Core/        Core plugin: Harmony bootstrap
  Multipeglin/             Multiplayer mod
thunderstore/              Thunderstore packaging (manifest, icon, README)
release/                   Game files (do not modify directly)
```

## Development Workflow

1. Build, deploy, and launch with live logs:
   ```bash
   just dev
   ```

2. Launch the game, click **Multiplayer** on the main menu.
   - **Host**: Click Host Game. Share the displayed IP:PORT code.
   - **Join**: Click Join Game. Enter the host's address and click Connect.

### Testing Steam networking across two machines

`just dev-multi` starts two instances on one machine, but both skip Steam init so they can't exercise the Steam transport. To test real Steam lobbies / friend joins / overlay invites on two separate machines, use:

```bash
just dev-network-player
```

Run it on **both** machines. It swaps `release/steam_appid.txt` to Valve's free **Spacewar** AppID (`480`) for the duration of the launch, so Steam lets any account host and join lobbies without owning Peglin on that account. The recipe restores the original AppID (`1296610`) on exit, on Ctrl+C, and as a dependency of `just dev` / `just dev-multi` / `just deploy` so a crashed run never leaves Spacewar set.

Both machines must run `dev-network-player` — the friend-list filter only matches players whose current AppID equals the local process's AppID, so a Spacewar host is invisible to a vanilla-Peglin joiner and vice versa.

## Multiplayer

Cooperative multiplayer with per-player classes, decks, relics, and turn-based battles.

The host's game events are captured via static delegate subscriptions, serialized as JSON, and sent over LiteNetLib UDP to the multiplayer client, which replays them by invoking the same game delegates locally.

## Architecture

Key design:
- **Event-sourced host-authoritative** networking
- **IServerHandler / IClientHandler** pairs per event type
- **LiteNetLib** UDP transport with ReliableOrdered delivery
- **System.Text.Json** serialization
- **Dependency injection** via lightweight service container
- **Version checking** with handshake protocol on connect

## Troubleshooting

**Game crashes immediately on launch (no window, no logs)**

The Proton/Wine prefix is likely corrupted. Run:
```bash
just uninstall
just dev
```
This removes BepInEx **and** deletes the Proton prefixes at `~/.steam/steam/steamapps/compatdata/1296610/` and `1296611/`. Proton recreates them automatically on the next launch.

Signs of a corrupted prefix: the game crashes with exit code 1 before writing `Player.log`, BepInEx `LogOutput.log` is stale or missing, and `steam-*.log` shows `err:steam:run_process Failed to create process ... : 2`.

**Game works through Steam but not `just dev`**

Same fix — the prefix was set up by Steam (inside its pressure-vessel container) and may not be compatible with a direct `proton run` invocation. Resetting it with `just uninstall` lets Proton rebuild it cleanly.

**`just dev-multi` — first instance crashes, second works**

The first instance uses prefix `1296610` and the second uses `1296611`. If only the first crashes, the `1296610` prefix is corrupted. `just uninstall` resets both.

**BepInEx loads but game crashes shortly after**

Check `release/BepInEx/LogOutput.log` and `Player.log` (in the Proton prefix under `drive_c/users/steamuser/AppData/LocalLow/Red Nexus Games Inc/Peglin/`). Common causes:
- Stale plugin DLLs in `release/BepInEx/plugins/` — run `just clean && just dev`
- BepInEx cache out of sync — delete `release/BepInEx/cache/` and relaunch
