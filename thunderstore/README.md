# Multipeglin

Cooperative multiplayer mod for Peglin. Play through runs together with friends!

## Features

- Host/join cooperative multiplayer sessions of up to 4 players
- Full game state synchronization (map, battles, deck, relics, enemies)
- Per-player decks, relics, health, and gold in coop
- Turn-based combat, ball physics synchronization
- Shop, treasure, and event scene synchronization
- Multiplayer save-and-continue system

## Installation

Install via [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or
[Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager).

**Manual installation:** Extract all DLLs to `BepInEx/plugins/Multipeglin/`.

## Usage

1. Launch Peglin with the mod installed
2. Click "Multiplayer" on the main menu
3. **Host**: Click "Host" to start a session. It will host on Steam. Or click "Continue" if you had a previous run you want to continue with the same players.
4. **Join**: Select a hosted game from your Steam friends list and join.
5. All clients select their class and click Ready.
6. Host selects Cruciball level, and clicks start game.

## State

- Most of the game works. There is some slight desync possible on super complex peg layouts such as complex moving pegs, but it is 90% correct.
- Some event choices don't make sense so it defaults to host choice (eg. if host selects to skip an event, but 1 player selects to do a battle, it's just going to use what the host picked)
- End-of-battle shot to determine navigation let all players participate at the same time and will choose the slot that got the most hits
- Most relics apply their logic correctly for the owner players shot, but some with really crazy effects might have been missed; let me know if you find anything broken
- If an event is really bad and softlocks, there is a 45 second timeout after which the host will be able to select "Force continue" to force the clients to go to the next map node. There is also a 15 second timeout after a shot occurs where if there are no orbs on the board anymore it will pass to the next players turn.

## Requirements

- BepInEx 5 (installed via BepInExPack_Peglin)
- All players must have the same mod version installed
