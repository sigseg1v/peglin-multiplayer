# PeglinMods

A modding framework and multiplayer mode for [Peglin](https://store.steampowered.com/app/1296610/Peglin/) (v2.0.7, Unity 2022.3, Mono).

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
just setup      # download + install BepInEx into release/ (auto-run by dev/deploy)
just deploy     # build + deploy plugin into release/BepInEx/plugins/
just dev        # build + deploy + launch game + tail logs
just dev-multi  # build + deploy + launch two game instances (host + client)
just log        # tail the dev log file
just clean      # remove build artifacts
just uninstall  # remove BepInEx from release/ (restore to vanilla)
```

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/) 8+
- [just](https://github.com/casey/just) command runner
- [PowerShell](https://github.com/PowerShell/PowerShell) (pwsh) - cross-platform scripting
- Linux only: Proton or Wine (for running the game)

## Project Structure

```
src/
  PeglinMods.Core/         Core plugin: crash reporter disable, Harmony bootstrap
  PeglinMods.Multiplayer/    Multiplayer mode
release/                   Game files (do not modify directly)
install/                   Install/uninstall scripts for end users
```

## Development Workflow

1. Build, deploy, and launch with live logs:
   ```bash
   just dev
   ```

2. Launch the game, click **Multiplayer** on the main menu.
   - **Host**: Click Host Game. Share the displayed IP:PORT code.
   - **Join**: Click Join Game. Enter the host's address and click Connect.

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
