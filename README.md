# PeglinMods

A modding framework and multiplayer spectator mode for [Peglin](https://store.steampowered.com/app/1296610/Peglin/) (v2.0.7, Unity 2022.3, Mono).

Built with BepInEx 5 + HarmonyX. Runs on Linux via Proton/Wine.

## Quick Start

```bash
just dev       # build + install BepInEx + deploy plugin + launch game + tail logs
```

That's it. On first run it downloads BepInEx automatically.

## All Commands

```bash
just build     # compile (debug)
just publish   # compile (release) + copy DLLs to build/
just setup     # download + install BepInEx into release/ (auto-run by dev/deploy)
just deploy    # build + deploy plugin into release/BepInEx/plugins/
just dev       # build + deploy + launch game + tail logs
just log       # tail the dev log file
just clean     # remove build artifacts
just uninstall # remove BepInEx from release/ (restore to vanilla)
```

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/) 8+
- [just](https://github.com/casey/just) command runner
- Proton or Wine (for running the game on Linux)

## Project Structure

```
src/
  PeglinMods.Core/         Core plugin: crash reporter disable, Harmony bootstrap
  PeglinMods.Spectator/    Multiplayer spectator mode
release/                   Game files (do not modify directly)
install/                   Install/uninstall scripts for end users
```

## Development Workflow

1. Build, deploy, and launch with live logs:
   ```bash
   just dev
   ```

2. Press **F7** in-game to open the Multiplayer overlay.

## Multiplayer (Spectator Mode)

One player hosts, another spectates in real-time.

- **Host**: Press F7 > Host Game. Share the displayed IP:PORT code.
- **Join**: Press F7 > Join Game. Enter the host's code and click Connect.

The host's game events are captured via static delegate subscriptions, serialized as JSON, and sent over LiteNetLib UDP to the spectator client, which replays them by invoking the same game delegates locally.

## Architecture


Key design:
- **Event-sourced host-authoritative** networking
- **IServerHandler / IClientHandler** pairs per event type
- **LiteNetLib** UDP transport with ReliableOrdered delivery
- **System.Text.Json** serialization
- **Dependency injection** via lightweight service container
- **Version checking** with handshake protocol on connect
